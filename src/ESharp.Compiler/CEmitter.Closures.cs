using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// Heap closures and events (issue #41). A delegate value is always { fn, env }. A capturing lambda that is only borrowed (passed
/// as an argument, or held in a local that is only called and passed on) keeps the old, cheap form: env points at a struct of
/// pointers on the stack of the method that made it. A capturing lambda that is kept (stored in a field, a static, an array
/// element or a property, added to an event, returned, passed to an [Owned] parameter, or held in a local that is kept or freed)
/// is a heap closure: the variables it captures live in reference-counted frames (one per C# scope that declares such
/// variables, allocated when the scope is entered, so a loop body gets a fresh frame per iteration, as in C#), and env points
/// at the innermost frame the lambda uses. A frame holds a reference to its enclosing frame. The scope that made a frame holds
/// one reference, dropped when the scope ends (gcc's cleanup attribute, so return, break and continue are covered); each heap
/// closure holds one more, dropped by Heap.Free(ref d). An event is a list of { fn, env } pairs that owns one reference per
/// handler: += of a new lambda hands it over, += of any other delegate value takes a new reference, -= drops one, and
/// Heap.Free(ref E) drops them all.
/// </summary>
internal sealed partial class CEmitter
{
    private bool _usesClosures;

    /// <summary>A heap frame: the captured variables of one scope (a block, or a function's parameters and top-level locals).</summary>
    private sealed class Frame
    {
        public string Struct = "";
        public string Var = "";
        public SyntaxNode Scope = null!;
        public SyntaxNode Function = null!;
        public readonly List<ISymbol> Vars = new();
        public INamedTypeSymbol? SelfType;   // set when the frame also holds 'this' (a method's body frame)
        public Frame? Up;                     // the enclosing frame in the same function
    }

    private readonly Dictionary<SyntaxNode, Dictionary<SyntaxNode, Frame>> _framePlans = new();
    private readonly Dictionary<LambdaExpressionSyntax, bool> _heapLambdas = new();
    private readonly HashSet<SyntaxNode> _heapBusy = new();
    private readonly Dictionary<ISymbol, bool> _escapes = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _escapeBusy = new(SymbolEqualityComparer.Default);

    /// <summary>The function being emitted (method, accessor, constructor, local function or lambda node), and for a heap lambda the frames its env reaches.</summary>
    private SyntaxNode? _fnNode;
    private List<Frame> _outerFrames = new();

    private static bool IsFunctionNode(SyntaxNode n) =>
        n is LambdaExpressionSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax or BaseMethodDeclarationSyntax
        || n is ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax or IndexerDeclarationSyntax };

    private static SyntaxNode? FunctionOf(SyntaxNode n)
    {
        for (var p = n.Parent; p != null; p = p.Parent)
            if (IsFunctionNode(p)) return p;
        return null;
    }

    private static BlockSyntax? BodyBlock(SyntaxNode fn) => fn switch
    {
        LambdaExpressionSyntax l => l.Block,
        _ => BodyOf(fn).Block,
    };

    /// <summary>The scope whose frame holds <paramref name="v"/> (the function node for parameters and the body's own locals), or null if it cannot be framed.</summary>
    private static SyntaxNode? FrameScopeOf(ISymbol v, out SyntaxNode? fn)
    {
        fn = null;
        // A setter's implicit 'value' has no syntax of its own: it belongs to the accessor.
        if (v is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.PropertySet } setter } && v.DeclaringSyntaxReferences.IsEmpty)
        {
            fn = setter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            return fn != null && IsFunctionNode(fn) ? fn : null;
        }
        var syntax = v.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax == null) return null;
        fn = FunctionOf(syntax);
        if (fn == null) return null;
        if (v is IParameterSymbol) return fn;
        if (syntax is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax { Parent: BlockSyntax b } } })
            return b == BodyBlock(fn) ? fn : b;
        return null;
    }

    private static string TreeHash(SyntaxTree tree)
    {
        uint h = 17;
        foreach (var ch in tree.FilePath) h = h * 31 + ch;
        return h.ToString("x");
    }

    private static string FrameField(ISymbol v) => "v_" + v.Name;

    // ------------------------------------------------------------------ which lambdas are kept

    /// <summary>A capturing lambda whose value is kept beyond the statement that makes it: it gets a heap closure.</summary>
    private bool IsHeapLambda(LambdaExpressionSyntax lam)
    {
        if (_heapLambdas.TryGetValue(lam, out var known)) return known;
        if (IsInlinedLinqLambda(lam)) return _heapLambdas[lam] = false;
        var (locals, usesThis) = FindCaptures(lam);
        if (locals.Count == 0 && !usesThis) return _heapLambdas[lam] = false;
        // Asked again while deciding (a lambda that reaches itself through locals): answer "kept", which is always memory-safe
        // (at worst a closure that is never freed), so a cycle can never leave a stack closure behind.
        if (!_heapBusy.Add(lam)) return true;
        var site = Classify(lam);
        var kept = site.Kind switch
        {
            SiteKind.Kept => true,
            SiteKind.Local => Escapes(site.Local!),
            _ => false,   // Borrowed: the stack form; Unknown and Reject: the stack path reports ES0099 unless it is an argument or a local's initializer
        };
        if (site.Kind == SiteKind.Reject) ReportOnce(lam, site.Reason!);
        _heapBusy.Remove(lam);
        return _heapLambdas[lam] = kept;
    }

    /// <summary>
    /// The expression whose value <paramref name="node"/> becomes: through parentheses, casts, <c>!</c>, the branches of <c>?:</c>,
    /// both operands of <c>??</c> and the arms of a switch expression (all of these pass the delegate on unchanged).
    /// </summary>
    private static SyntaxNode ValueSite(SyntaxNode node, out SyntaxNode? parent)
    {
        parent = node.Parent;
        while (true)
        {
            var through = parent is ParenthesizedExpressionSyntax or CastExpressionSyntax
                || parent is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression }
                || parent is ConditionalExpressionSyntax c && c.Condition != node
                || parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression }
                || parent is SwitchExpressionArmSyntax arm && arm.Expression == node
                || parent is SwitchExpressionSyntax sw && sw.GoverningExpression != node;
            if (!through) return node;
            node = parent!;
            parent = parent!.Parent;
        }
    }

    private IParameterSymbol? ParameterOf(ArgumentSyntax a)
    {
        if (a.Parent is not BaseArgumentListSyntax { Parent: { } call } list) return null;
        if (Model(a).GetSymbolInfo(call).Symbol is not IMethodSymbol m) return null;
        if (a.NameColon != null) return m.Parameters.FirstOrDefault(p => p.Name == a.NameColon.Name.Identifier.Text);
        var index = list.Arguments.IndexOf(a);
        return index < m.Parameters.Length ? m.Parameters[index] : m.Parameters.LastOrDefault(p => p.IsParams);
    }

    private static bool IsOwned(IParameterSymbol? p) => p != null && HasAttribute(p, "OwnedAttribute");

    private bool IsHeapFreeArgument(ArgumentSyntax a) =>
        a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) && a.Parent?.Parent is InvocationExpressionSyntax inv &&
        Model(inv).GetSymbolInfo(inv).Symbol is IMethodSymbol { Name: "Free", ContainingType.Name: "Heap" } hf &&
        hf.ContainingNamespace?.ToDisplayString() == "ESharp.Platform";

    private enum SiteKind { Borrowed, Kept, Local, Unknown, Reject }

    private readonly record struct Site(SiteKind Kind, ISymbol? Local = null, string? Reason = null);

    private readonly HashSet<SyntaxNode> _reported = new();

    private void ReportOnce(SyntaxNode at, string what)
    {
        if (_reported.Add(at)) Unsupported(at, what);
    }

    /// <summary>
    /// Where a delegate value goes. Borrowed: called, compared, removed from an event, or passed to a non-generic delegate parameter
    /// (the callee may not keep it, ES0012). Kept: stored, returned, added to an event, freed, or given to an [Owned] parameter.
    /// Local: copied into a local (kept if that local is). Reject: a place E# cannot track (ES0099). Unknown: anything else, which
    /// callers treat as kept for a delegate local (heap: safe) and as ES0099 for a capturing lambda; never as the stack.
    /// </summary>
    private Site Classify(SyntaxNode e)
    {
        var node = ValueSite(e, out var p);
        switch (p)
        {
            case InvocationExpressionSyntax inv when inv.Expression == node:
            case MemberAccessExpressionSyntax { Name.Identifier.Text: "Invoke", Parent: InvocationExpressionSyntax } ma when ma.Expression == node:
            case ConditionalAccessExpressionSyntax ca when ca.Expression == node:
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression or (int)SyntaxKind.NotEqualsExpression }:
                return new Site(SiteKind.Borrowed);
            case ArgumentSyntax { Parent: TupleExpressionSyntax }:
                return new Site(SiteKind.Reject, Reason: "a capturing delegate in a tuple");
            case ArgumentSyntax arg:
            {
                if (IsHeapFreeArgument(arg)) return new Site(SiteKind.Kept);
                var prm = ParameterOf(arg);
                if (IsOwned(prm)) return new Site(SiteKind.Kept);
                if (prm == null) return new Site(SiteKind.Unknown);
                if (prm.OriginalDefinition.Type is ITypeParameterSymbol)
                    return new Site(SiteKind.Reject, Reason: "a capturing delegate passed as a generic argument (T); the callee could keep it, so pass it to a delegate-typed parameter, or mark the parameter [Owned]");
                if (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) return new Site(SiteKind.Kept);
                return new Site(SiteKind.Borrowed);
            }
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } } vd }:
                return Model(vd).GetDeclaredSymbol(vd) is ILocalSymbol local ? new Site(SiteKind.Local, local) : new Site(SiteKind.Unknown);
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax or EventFieldDeclarationSyntax } }:
            case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax }:
                return new Site(SiteKind.Kept);
            case AssignmentExpressionSyntax a when a.Left == node:
                return new Site(SiteKind.Borrowed);   // written, not read
            case AssignmentExpressionSyntax a when a.Right == node:
                if (a.IsKind(SyntaxKind.SimpleAssignmentExpression) || a.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                    return Model(a).GetSymbolInfo(a.Left).Symbol switch
                    {
                        ILocalSymbol { RefKind: RefKind.None } l => new Site(SiteKind.Local, l),
                        IParameterSymbol { RefKind: RefKind.None } prm => new Site(SiteKind.Local, prm),
                        _ => new Site(SiteKind.Kept),
                    };
                if (Model(a).GetSymbolInfo(a.Left).Symbol is IEventSymbol)
                {
                    if (a.IsKind(SyntaxKind.AddAssignmentExpression)) return new Site(SiteKind.Kept);
                    if (a.IsKind(SyntaxKind.SubtractAssignmentExpression)) return new Site(SiteKind.Borrowed);   // only compared
                }
                return new Site(SiteKind.Unknown);
            case ReturnStatementSyntax or ArrowExpressionClauseSyntax or LambdaExpressionSyntax or InitializerExpressionSyntax:
                return new Site(SiteKind.Kept);
            default:
                return new Site(SiteKind.Unknown);
        }
    }

    /// <summary>True when the value of <paramref name="e"/> is kept (stored, added to an event, returned, freed, given to an [Owned] parameter).</summary>
    private bool KeptAt(SyntaxNode e)
    {
        var site = Classify(e);
        return site.Kind == SiteKind.Kept || site.Kind == SiteKind.Local && Escapes(site.Local!);
    }

    /// <summary>
    /// A delegate local (or value parameter) escapes when its value is kept somewhere, freed, captured by a heap closure, or used
    /// anywhere E# does not recognise (then it is made a heap closure, which is safe). A use E# cannot track reports ES0099.
    /// </summary>
    private bool Escapes(ISymbol v)
    {
        var type = v switch { ILocalSymbol l => l.Type, IParameterSymbol p => p.Type, _ => null };
        if (type?.TypeKind != TypeKind.Delegate) return false;
        if (_escapes.TryGetValue(v, out var known)) return known;
        if (!_escapeBusy.Add(v)) return true;   // a cycle: assume kept (memory-safe), see IsHeapLambda
        var result = false;
        var syntax = v.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var fn = syntax == null ? null : FunctionOf(syntax);
        if (fn != null)
            foreach (var id in fn.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (id.Identifier.Text != v.Name || !SymbolEqualityComparer.Default.Equals(Model(id).GetSymbolInfo(id).Symbol, v)) continue;
                for (var anc = id.Parent; anc != null && anc != fn; anc = anc.Parent)
                    if (anc is LambdaExpressionSyntax inner && IsHeapLambda(inner)) { result = true; break; }
                if (result) break;
                var site = Classify(id);
                switch (site.Kind)
                {
                    case SiteKind.Kept or SiteKind.Unknown:
                        result = true;
                        break;
                    case SiteKind.Local:
                        result = !SymbolEqualityComparer.Default.Equals(site.Local, v) && Escapes(site.Local!);
                        break;
                    case SiteKind.Reject:
                        ReportOnce(id, site.Reason!);
                        result = true;
                        break;
                }
                if (result) break;
            }
        _escapeBusy.Remove(v);
        return _escapes[v] = result;
    }

    /// <summary>A borrowed delegate parameter (not [Owned], not a setter's value).</summary>
    private static bool IsBorrowedParameter(ISymbol? s) =>
        s is IParameterSymbol { RefKind: RefKind.None, Type.TypeKind: TypeKind.Delegate } p && !IsOwned(p)
        && p.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertySet };

    /// <summary>Backstop for ES0012: a local that is kept must not hold a borrowed delegate parameter (it may point at the caller's stack).</summary>
    private void CheckBorrowedCopy(ISymbol local, ExpressionSyntax value, SyntaxNode at)
    {
        var v = Strip(value);
        if (v is IdentifierNameSyntax id && IsBorrowedParameter(Model(id).GetSymbolInfo(id).Symbol) && Escapes(local))
            ReportOnce(at, $"keeping the borrowed delegate parameter '{id.Identifier.Text}' through the local '{local.Name}' (the caller's closure may live on its stack); mark the parameter [Owned]");
    }

    /// <summary>
    /// Whether an event's += gets a reference of its own to hand over (a new lambda, a call's result) or must take one (a variable,
    /// field, property or element that keeps its own). Null (after ES0099) when the branches of ?: or ?? differ.
    /// </summary>
    private bool? IsFreshDelegate(ExpressionSyntax e)
    {
        e = Strip(e);
        switch (e)
        {
            case LambdaExpressionSyntax:
            case InvocationExpressionSyntax:
                return true;
            case CastExpressionSyntax cast:
                return IsFreshDelegate(cast.Expression);
            case ConditionalExpressionSyntax c:
                return Merge(new[] { c.WhenTrue, c.WhenFalse }, e);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b:
                return Merge(new[] { b.Left, b.Right }, e);
            case SwitchExpressionSyntax sw:
                return Merge(sw.Arms.Select(arm => arm.Expression).ToArray(), e);
            default:
                return false;
        }

        bool? Merge(ExpressionSyntax[] parts, SyntaxNode at)
        {
            bool? result = null;
            foreach (var part in parts)
            {
                if (IsNullConstant(part)) continue;   // null holds nothing either way
                var f = IsFreshDelegate(part);
                if (f == null) return null;
                if (result != null && result != f)
                {
                    ReportOnce(at, "+= of a value that is a new delegate on one branch and an existing one on another; split it into separate statements");
                    return null;
                }
                result = f;
            }
            return result ?? false;
        }
    }

    // ------------------------------------------------------------------ frames

    /// <summary>The frames of one function: which of its scopes hold variables captured by heap closures.</summary>
    private Dictionary<SyntaxNode, Frame> FramePlan(SyntaxNode fn)
    {
        if (_framePlans.TryGetValue(fn, out var plan)) return plan;
        plan = new Dictionary<SyntaxNode, Frame>();
        _framePlans[fn] = plan;
        Frame FrameFor(SyntaxNode scope)
        {
            if (!plan.TryGetValue(scope, out var f))
            {
                var hash = TreeHash(scope.SyntaxTree);
                plan[scope] = f = new Frame { Struct = $"ES_frame_{hash}_{scope.SpanStart}", Var = $"es_fr{scope.SpanStart}", Scope = scope, Function = fn };
            }
            return f;
        }
        foreach (var lam in fn.DescendantNodes().OfType<LambdaExpressionSyntax>())
        {
            if (!IsHeapLambda(lam)) continue;
            var (locals, usesThis) = FindCaptures(lam);
            foreach (var v in locals)
            {
                var scope = FrameScopeOf(v, out var owner);
                if (scope == null || owner != fn) continue;
                var f = FrameFor(scope);
                if (!f.Vars.Contains(v, SymbolEqualityComparer.Default)) f.Vars.Add(v);
            }
            if (usesThis && fn is not LambdaExpressionSyntax && ContainingTypeOf(fn) is { } self && IsHeapClass(self))
                FrameFor(fn).SelfType = self;
        }
        foreach (var f in plan.Values)
        {
            if (f.Scope == fn) continue;
            for (var p = f.Scope.Parent; p != null; p = p.Parent)
            {
                if (p == fn) { f.Up = plan.GetValueOrDefault(fn); break; }
                if (p is BlockSyntax b && plan.TryGetValue(b, out var up) && b != BodyBlock(fn)) { f.Up = up; break; }
            }
        }
        return plan;
    }

    private INamedTypeSymbol? ContainingTypeOf(SyntaxNode fn)
    {
        var model = Model(fn);
        var sym = fn is ArrowExpressionClauseSyntax arrow ? model.GetEnclosingSymbol(arrow.Expression.SpanStart) : model.GetDeclaredSymbol(fn);
        return sym?.ContainingType;
    }

    /// <summary>The frames reachable at <paramref name="pos"/> in function <paramref name="fn"/>, innermost first; through a heap lambda they go on into the function around it.</summary>
    private List<Frame> FrameChain(SyntaxNode pos, SyntaxNode fn)
    {
        var plan = FramePlan(fn);
        var chain = new List<Frame>();
        var body = BodyBlock(fn);
        for (var p = pos.Parent; p != null && p != fn; p = p.Parent)
            if (p is BlockSyntax b && b != body && plan.TryGetValue(b, out var f)) chain.Add(f);
        if (plan.TryGetValue(fn, out var bf)) chain.Add(bf);
        if (fn is LambdaExpressionSyntax outer && IsHeapLambda(outer) && FunctionOf(outer) is { } outerFn)
            chain.AddRange(EnvChain(outer, outerFn));
        return chain;
    }

    /// <summary>The frames a heap lambda's env reaches: from the innermost frame holding something it captures, outwards.</summary>
    private List<Frame> EnvChain(LambdaExpressionSyntax lam, SyntaxNode fn)
    {
        var chain = FrameChain(lam, fn);
        var (locals, usesThis) = FindCaptures(lam);
        var first = chain.FindIndex(f => f.Vars.Any(v => locals.Contains(v, SymbolEqualityComparer.Default)) || usesThis && f.SelfType != null);
        return first < 0 ? new List<Frame>() : chain.Skip(first).ToList();
    }

    private string FrameRef(Frame f)
    {
        var i = _outerFrames.IndexOf(f);
        return i >= 0 ? $"_up{i}" : f.Var;
    }

    /// <summary>Allocates the frame of a scope as it is entered, and points the scope's captured variables at it.</summary>
    private void EmitFrame(SyntaxNode scope)
    {
        if (_fnNode == null || !FramePlan(_fnNode).TryGetValue(scope, out var f)) return;
        _usesClosures = true;
        var fields = new StringBuilder();
        foreach (var v in f.Vars)
        {
            var type = v is ILocalSymbol l ? l.Type : ((IParameterSymbol)v).Type;
            if (type is IArrayTypeSymbol) { Unsupported(scope, $"a stored lambda that captures the array '{v.Name}'"); continue; }
            fields.Append(' ').Append(Decl(type, FrameField(v), scope)).Append(';');
        }
        if (f.SelfType != null) fields.Append($" {TypeName(f.SelfType)} *{ThisName};");
        _spans[f.Struct] = $"struct {f.Struct} {{ es_frame hdr;{fields} }};\n";
        var up = f.Up != null ? f.Up.Var : _outerFrames.Count > 0 ? "_env" : "NULL";
        Line($"struct {f.Struct} *{f.Var} __attribute__((cleanup(es_frame_drop))) = (struct {f.Struct} *)es_frame_new(sizeof(struct {f.Struct}), {up});");
        foreach (var v in f.Vars)
        {
            if (v is IParameterSymbol p) Line($"{f.Var}->{FrameField(v)} = {(_captures.TryGetValue(p, out var cur) ? cur : p.Name)};");
            _captures[v] = $"{f.Var}->{FrameField(v)}";
        }
        if (f.SelfType != null) Line($"{f.Var}->{ThisName} = {ThisName};");
    }

    /// <summary>The start of a heap lambda's function: pointers to the frames its env reaches, and its captured variables mapped into them.</summary>
    private void HeapLambdaPrologue(LambdaExpressionSyntax lam, IMethodSymbol m)
    {
        var fn = FunctionOf(lam)!;
        _outerFrames = EnvChain(lam, fn);
        for (var i = 0; i < _outerFrames.Count; i++)
            Line($"struct {_outerFrames[i].Struct} *_up{i} __attribute__((unused)) = (struct {_outerFrames[i].Struct} *){(i == 0 ? "_env" : $"_up{i - 1}->hdr.up")};");
        var (locals, usesThis) = FindCaptures(lam);
        foreach (var v in locals)
        {
            var k = _outerFrames.FindIndex(f => f.Vars.Contains(v, SymbolEqualityComparer.Default));
            if (k >= 0) _captures[v] = $"_up{k}->{FrameField(v)}";
        }
        if (usesThis)
        {
            var k = _outerFrames.FindIndex(f => f.SelfType != null);
            if (k >= 0)
            {
                Line($"{TypeName(_outerFrames[k].SelfType!)} *{ThisName} __attribute__((unused)) = _up{k}->{ThisName};");
                _this = ThisKind.Pointer;
            }
        }
    }

    /// <summary>A heap closure value: the lambda's function and a new reference to the innermost frame it uses.</summary>
    private string HeapLambda(LambdaExpressionSyntax lam, IMethodSymbol m, string dt)
    {
        var fn = FunctionOf(lam);
        if (fn == null || fn != _fnNode) return Unsupported(lam, "a stored lambda here");
        var (locals, usesThis) = FindCaptures(lam);
        var chain = FrameChain(lam, fn);
        foreach (var v in locals)
        {
            if (v is IParameterSymbol { Type.TypeKind: TypeKind.Delegate } dp && !IsOwned(dp) && dp.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertySet })
                return Unsupported(lam, $"a stored lambda that captures the borrowed delegate parameter '{v.Name}' (the caller's closure may live on its stack); mark the parameter [Owned]");
            if (!chain.Any(f => f.Vars.Contains(v, SymbolEqualityComparer.Default)))
                return Unsupported(lam, FrameScopeOf(v, out var owner) == null && owner != null
                    ? $"a stored lambda that captures '{v.Name}', which is declared in a for, foreach, using, switch section, pattern or out variable; copy it into a local declared in a block"
                    : $"a stored lambda that captures '{v.Name}' from outside a lambda that is only borrowed (an argument); store the outer lambda too, or pass '{v.Name}' as a parameter");
        }
        if (usesThis && !chain.Any(f => f.SelfType != null))
            return Unsupported(lam, "a stored lambda that uses 'this' here (inside a lambda that is only borrowed, or in a struct)");
        var env = EnvChain(lam, fn);
        if (env.Count == 0) return Unsupported(lam, "a stored lambda here");
        _usesClosures = true;
        ValueSite(lam, out var site);
        var borrowedForRemoval = site is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SubtractAssignmentExpression };
        var envRef = FrameRef(env[0]);
        return $"(({dt}){{ {FuncName(m)}, {(borrowedForRemoval ? envRef : $"es_closure_retain({envRef})")} }})";
    }

    // ------------------------------------------------------------------ events

    private static bool IsFieldLikeEvent(IEventSymbol ev) =>
        ev.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax };

    private static string StaticEventName(IEventSymbol ev) => $"{TypeName(ev.ContainingType)}_{ev.Name}";

    /// <summary>The C lvalue of an event (its es_event list), or null after reporting why not.</summary>
    private string? EventPlace(ExpressionSyntax e, IEventSymbol ev)
    {
        e = Strip(e);
        if (!IsFieldLikeEvent(ev)) { Unsupported(e, "an event with add/remove accessors; declare it as 'event Action Name;'"); return null; }
        if (ev.ContainingType.TypeKind != TypeKind.Class || IsInterfaceType(ev.ContainingType)) { Unsupported(e, "an event outside a class"); return null; }
        var invoke = (ev.Type as INamedTypeSymbol)?.DelegateInvokeMethod;
        if (invoke == null || !invoke.ReturnsVoid || invoke.Parameters.Any(p => p.RefKind != RefKind.None))
        { Unsupported(e, "an event whose delegate type returns a value or has ref/out parameters"); return null; }
        _usesClosures = true;
        if (ev.IsStatic) return StaticEventName(ev);
        switch (e)
        {
            case IdentifierNameSyntax when _this == ThisKind.Pointer:
                return $"{ThisName}->{MemberPath(_thisType, ev.ContainingType, ev.Name)}";
            case MemberAccessExpressionSyntax ma:
                var recvType = Model(ma).GetTypeInfo(ma.Expression).Type;
                return $"{Expr(ma.Expression)}->{MemberPath(recvType, ev.ContainingType, ev.Name)}";
            default:
                Unsupported(e, "an event used here");
                return null;
        }
    }

    /// <summary><c>E += d</c> and <c>E -= d</c>.</summary>
    private string EventAssignment(AssignmentExpressionSyntax a, IEventSymbol ev)
    {
        if (!a.IsKind(SyntaxKind.AddAssignmentExpression) && !a.IsKind(SyntaxKind.SubtractAssignmentExpression))
            return Unsupported(a, "assigning an event; use += and -=, and Heap.Free(ref E) inside the class to drop every handler");
        var place = EventPlace(a.Left, ev);
        if (place == null) return "0";
        var dt = CType(ev.Type, a);
        var t = $"_h{++_temp}";
        var value = Expr(a.Right, top: true);
        if (a.IsKind(SyntaxKind.SubtractAssignmentExpression))
            return $"({{ {dt} {t} = {value}; es_event_remove(&({place}), (void (*)(void)){t}.fn, {t}.env); }})";
        // A new lambda's reference goes to the event; any other value (a variable, a field, a call) is shared, so the event takes its own.
        if (IsFreshDelegate(a.Right) is not bool fresh) return "0";
        return $"({{ {dt} {t} = {value}; es_event_add(&({place}), (void (*)(void)){t}.fn, {(fresh ? $"{t}.env" : $"es_closure_retain({t}.env)")}); }})";
    }

    /// <summary>Raising an event: every handler in order, on a snapshot of the count (handlers added meanwhile wait for the next raise; removals are deferred).</summary>
    private string EventRaise(IEventSymbol ev, ExpressionSyntax callee, ArgumentListSyntax args, IMethodSymbol invoke, SyntaxNode at)
    {
        var place = EventPlace(callee, ev);
        if (place == null) return "0";
        var dt = CType(ev.Type, at);
        var e = $"_ev{++_temp}";
        var sb = new StringBuilder($"({{ es_event *{e} = &({place}); ");
        var names = new List<string> { $"{e}->items[{e}_i].env" };
        for (var i = 0; i < args.Arguments.Count; i++)
        {
            var tmp = $"{e}_a{i}";
            sb.Append($"{Decl(invoke.Parameters[i].Type, tmp, at)} = {Expr(args.Arguments[i].Expression, top: true)}; ");
            names.Add(tmp);
        }
        sb.Append($"int32_t {e}_n = {e}->count; uint32_t {e}_s = es_event_begin({e}); ");
        sb.Append($"for (int32_t {e}_i = 0; {e}_i < {e}_n; {e}_i++) if ({e}->items[{e}_i].dead == 0 || {e}->items[{e}_i].dead > {e}_s) ((__typeof__((({dt} *)0)->fn)){e}->items[{e}_i].fn)({string.Join(", ", names)}); ");
        sb.Append($"es_event_end({e}); }})");
        return sb.ToString();
    }

    /// <summary><c>x?.Invoke(args)</c> on an event or a delegate whose type returns void.</summary>
    private string? ConditionalInvoke(ConditionalAccessExpressionSyntax ca)
    {
        if (ca.WhenNotNull is not InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax { Name.Identifier.Text: "Invoke" } } inv) return null;
        if (Model(inv).GetSymbolInfo(inv).Symbol is not IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } invoke) return null;
        if (Model(ca.Expression).GetSymbolInfo(ca.Expression).Symbol is IEventSymbol ev)
            return EventRaise(ev, ca.Expression, inv.ArgumentList, invoke, ca);
        if (!invoke.ReturnsVoid) return Unsupported(ca, "?.Invoke on a delegate that returns a value");
        var type = Model(ca.Expression).GetTypeInfo(ca.Expression).Type!;
        var t = $"_d{++_temp}";
        var args = inv.ArgumentList.Arguments.Select(a => Argument(a, invoke)).Prepend($"{t}.env");
        return $"({{ {CType(type, ca)} {t} = {Expr(ca.Expression, top: true)}; if ({t}.fn != NULL) {t}.fn({string.Join(", ", args)}); }})";
    }

    /// <summary>The C runtime for heap closures and events (needs es_new).</summary>
    private static void AppendClosureRuntime(StringBuilder result)
    {
        foreach (var line in new[]
        {
            "/* Heap closures: reference-counted frames of captured variables, each holding a reference to the frame around it. */",
            "typedef struct es_frame { int32_t rc; struct es_frame *up; } es_frame;",
            "static int32_t es_frames_live;",
            "static __attribute__((unused)) void *es_frame_new(size_t n, void *up)",
            "{",
            "    es_frame *f = (es_frame *)es_new(n);",
            "    f->rc = 1;",
            "    f->up = (es_frame *)up;",
            "    if (up != NULL) ((es_frame *)up)->rc++;",
            "    es_frames_live++;",
            "    return f;",
            "}",
            "static __attribute__((unused)) void es_frame_release(void *p)",
            "{",
            "    es_frame *f = (es_frame *)p;",
            "    while (f != NULL && --f->rc == 0)",
            "    {",
            "        es_frame *up = f->up;",
            "        free(f);",
            "        es_frames_live--;",
            "        f = up;",
            "    }",
            "}",
            "static __attribute__((unused)) void es_frame_drop(void *slot) { es_frame_release(*(void **)slot); }",
            "/* References held by closures (a stored lambda, or an event's copy of a delegate), for Heap.LiveClosures(). */",
            "static int32_t es_closures_live;",
            "static __attribute__((unused)) void *es_closure_retain(void *p)",
            "{",
            "    if (p != NULL) { ((es_frame *)p)->rc++; es_closures_live++; }",
            "    return p;",
            "}",
            "static __attribute__((unused)) void es_closure_release(void *p)",
            "{",
            "    if (p != NULL) { es_closures_live--; es_frame_release(p); }",
            "}",
            "/* Events: a list of handlers, each owning one reference to its closure. A raise works on a snapshot, as in C#: handlers added",
            "   meanwhile are not called, handlers removed meanwhile still are (dead is the stamp of the removal); entries are dropped once no raise runs. */",
            "typedef struct { void (*fn)(void); void *env; uint32_t dead; } es_dg;",
            "typedef struct { es_dg *items; int32_t count, cap, live, busy; uint32_t stamp; } es_event;",
            "static __attribute__((unused)) void es_event_compact(es_event *e)",
            "{",
            "    int32_t n = 0;",
            "    for (int32_t i = 0; i < e->count; i++)",
            "    {",
            "        if (e->items[i].dead != 0) es_closure_release(e->items[i].env);",
            "        else e->items[n++] = e->items[i];",
            "    }",
            "    e->count = n;",
            "    if (n == 0) { free(e->items); e->items = NULL; e->cap = 0; }",
            "}",
            "static __attribute__((unused)) void es_event_add(es_event *e, void (*fn)(void), void *env)",
            "{",
            "    if (fn == NULL) return;",
            "    if (e->count == e->cap)",
            "    {",
            "        int32_t cap = e->cap == 0 ? 4 : e->cap * 2;",
            "        es_dg *items = (es_dg *)es_new((size_t)cap * sizeof(es_dg));",
            "        for (int32_t i = 0; i < e->count; i++) items[i] = e->items[i];",
            "        free(e->items);",
            "        e->items = items;",
            "        e->cap = cap;",
            "    }",
            "    e->items[e->count].fn = fn;",
            "    e->items[e->count].env = env;",
            "    e->items[e->count].dead = 0;",
            "    e->count++;",
            "    e->live++;",
            "}",
            "static __attribute__((unused)) void es_event_remove(es_event *e, void (*fn)(void), void *env)",
            "{",
            "    if (fn == NULL) return;",
            "    for (int32_t i = e->count - 1; i >= 0; i--)",
            "        if (e->items[i].dead == 0 && e->items[i].fn == fn && e->items[i].env == env)",
            "        {",
            "            e->items[i].dead = ++e->stamp;",
            "            e->live--;",
            "            if (e->busy == 0) es_event_compact(e);",
            "            return;",
            "        }",
            "}",
            "static __attribute__((unused)) void es_event_clear(es_event *e)",
            "{",
            "    uint32_t s = ++e->stamp;",
            "    for (int32_t i = 0; i < e->count; i++) if (e->items[i].dead == 0) e->items[i].dead = s;",
            "    e->live = 0;",
            "    if (e->busy == 0) es_event_compact(e);",
            "}",
            "static __attribute__((unused)) uint32_t es_event_begin(es_event *e) { e->busy++; return ++e->stamp; }",
            "static __attribute__((unused)) void es_event_end(es_event *e) { if (--e->busy == 0 && e->live != e->count) es_event_compact(e); }",
        }) result.AppendLine(line);
    }
}
