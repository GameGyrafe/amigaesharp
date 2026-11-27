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
/// Heap.Free(ref E) drops them all. An [Owned] parameter or a setter given a value the caller keeps takes a new reference at the
/// call, as += does (issue #110); a new lambda or a local given away for good hands its reference over.
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
                // d += x and d -= x on a delegate that is not an event rebuild d (multicast): it owns its closures, so it is a heap value.
                if ((a.IsKind(SyntaxKind.AddAssignmentExpression) || a.IsKind(SyntaxKind.SubtractAssignmentExpression))
                    && Model(a).GetSymbolInfo(a.Left).Symbol is not IEventSymbol)
                    return new Site(SiteKind.Kept);
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
                else if (a.IsKind(SyntaxKind.AddAssignmentExpression)) return new Site(SiteKind.Kept);   // d += x: d takes a reference to x
                else if (a.IsKind(SyntaxKind.SubtractAssignmentExpression)) return new Site(SiteKind.Borrowed);   // only compared
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
    /// A call whose result is a reference the caller owns: a method of the program (not virtual, abstract, an interface member, extern
    /// or a delegate call) whose every return value is a new lambda, null, or a call that is fresh by the same rule (followed through
    /// ?:, ?? and switch expressions). Anything else (a method returning a field, a parameter or a local) is not: the conservative
    /// answer, because handing over a reference the call did not give is a use after free, while taking an extra one is at worst a leak.
    /// </summary>
    private bool IsFreshCall(InvocationExpressionSyntax inv, HashSet<IMethodSymbol> seen)
    {
        if (Model(inv).GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return false;
        if (m.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) || m.IsVirtual || m.IsAbstract || m.IsOverride || m.IsExtern
            || IsInterfaceType(m.ContainingType) || !seen.Add(m.OriginalDefinition)) return false;
        var decl = m.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (decl == null) return false;
        var (block, arrow) = BodyOf(decl);
        var values = new List<ExpressionSyntax>();
        if (arrow != null) values.Add(arrow.Expression);
        else if (block != null)
        {
            foreach (var r in block.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                if (FunctionOf(r) != decl) continue;   // a return inside a nested lambda or local function
                if (r.Expression == null) return false;
                values.Add(r.Expression);
            }
        }
        if (values.Count == 0) return false;
        return values.All(v => FreshValue(v, seen));
    }

    private bool FreshValue(ExpressionSyntax v, HashSet<IMethodSymbol> seen)
    {
        v = Strip(v);
        return v switch
        {
            LambdaExpressionSyntax => true,
            _ when IsNullConstant(v) => true,
            InvocationExpressionSyntax inv => IsFreshCall(inv, seen),
            CastExpressionSyntax c => FreshValue(c.Expression, seen),
            ConditionalExpressionSyntax c => FreshValue(c.WhenTrue, seen) && FreshValue(c.WhenFalse, seen),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b => FreshValue(b.Left, seen) && FreshValue(b.Right, seen),
            SwitchExpressionSyntax sw => sw.Arms.All(a => FreshValue(a.Expression, seen)),
            _ => false,
        };
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
                return true;
            case InvocationExpressionSyntax inv:
                if (IsFreshCall(inv, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))) return true;
                // The event takes a reference of its own; if the call made a new closure, the caller's one leaks.
                _diagnostics.Add(Diagnostic.Create(ESharp.Analyzers.Descriptors.ClosureNeverFreed, inv.GetLocation(),
                    $"The event takes its own reference to the delegate '{inv}' returns, because that method does not always return a new lambda; if it made a new closure, that reference is never freed (store the result in a variable, add it, and Heap.Free the variable)"));
                return false;
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

    // ------------------------------------------------------------------ values handed to [Owned] parameters and setters (issue #110)

    /// <summary>A property whose setter is a call that takes the value's reference (a set accessor with a body), not an auto-property's field store.</summary>
    private static bool IsHandoverSetter(IPropertySymbol? p) =>
        p is { SetMethod: not null, Type.TypeKind: TypeKind.Delegate } && !IsAutoProperty(p);

    /// <summary>An [Owned] delegate parameter taken by value: the callee takes a reference.</summary>
    private static bool IsHandoverParameter(IParameterSymbol? p) =>
        p is { RefKind: RefKind.None, Type.TypeKind: TypeKind.Delegate } && IsOwned(p);

    /// <summary>True when the value at <paramref name="node"/> (a value site, see <see cref="ValueSite"/>) is given to an [Owned] parameter or a setter.</summary>
    private bool IsHandoverSite(SyntaxNode node, SyntaxNode? parent) => parent switch
    {
        ArgumentSyntax arg => arg.Expression == node && arg.RefKindKeyword.IsKind(SyntaxKind.None) && IsHandoverParameter(ParameterOf(arg)),
        AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a =>
            a.Right == node && IsHandoverSetter(Model(a).GetSymbolInfo(a.Left).Symbol as IPropertySymbol),
        _ => false,
    };

    /// <summary>
    /// The C value given to an [Owned] parameter or a setter. The callee takes a reference, so a value whose reference the caller
    /// does not give up (a field, a property, an element, a call that may return a kept delegate, or a variable that is used again,
    /// freed, stored, captured or handed over more than once) gets a reference of its own first, as <c>E += x</c> does: then every
    /// owner frees its own. A new lambda, a call that always returns new closures, null, a method group and a variable handed
    /// over for good (see <see cref="MovesAt"/>) are handed over as they are.
    /// </summary>
    private string HandOver(ExpressionSyntax e, string value, ITypeSymbol type, string to)
    {
        if (type.TypeKind != TypeKind.Delegate || HandsOver(e, to, report: true) != false) return value;
        _usesClosures = true;
        var t = $"_ho{++_temp}";
        return $"({{ {CType(type, e)} {t} = {value}; es_closure_retain({t}.env); {t}; }})";
    }

    /// <summary>True: the value's reference goes to the callee. False: the callee needs one of its own. Null: reported (mixed branches).</summary>
    private bool? HandsOver(ExpressionSyntax e, string to, bool report)
    {
        e = Strip(e);
        switch (e)
        {
            case LambdaExpressionSyntax:
                return true;
            case IdentifierNameSyntax id when Model(id).GetSymbolInfo(id).Symbol is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }:
            {
                var v = Model(id).GetSymbolInfo(id).Symbol!;
                if (MovesAt(v, id)) return true;
                if (report) WarnUnfreedOriginal(v, id, to);
                return false;
            }
            case InvocationExpressionSyntax inv:
                if (IsFreshCall(inv, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))) return true;
                if (report && _reported.Add(inv))
                    _diagnostics.Add(Diagnostic.Create(ESharp.Analyzers.Descriptors.ClosureNeverFreed, inv.GetLocation(),
                        $"{to} takes its own reference to the delegate '{inv}' returns, because that method does not always return a new lambda; if it made a new closure, that reference is never freed (store the result in a variable, pass that, and Heap.Free the variable)"));
                return false;
            case CastExpressionSyntax cast:
                return HandsOver(cast.Expression, to, report);
            case ConditionalExpressionSyntax c:
                return Merge(new[] { c.WhenTrue, c.WhenFalse });
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b:
                return Merge(new[] { b.Left, b.Right });
            case SwitchExpressionSyntax sw:
                return Merge(sw.Arms.Select(arm => arm.Expression).ToArray());
            default:
                if (IsNullConstant(e) || Model(e).GetSymbolInfo(e).Symbol is IMethodSymbol) return true;   // nothing to hold: no closure
                return false;   // a field, a property, an element: they keep theirs
        }

        bool? Merge(ExpressionSyntax[] parts)
        {
            bool? result = null;
            foreach (var part in parts)
            {
                if (IsNullConstant(part)) continue;
                var f = HandsOver(part, to, report);
                if (f == null) return null;
                if (result != null && result != f)
                {
                    if (report) ReportOnce(e, $"a value given to {to} that hands over its reference on one branch and is shared on another; split it into separate statements");
                    return null;
                }
                result = f;
            }
            return result ?? true;
        }
    }

    /// <summary>
    /// True when the variable <paramref name="v"/> gives its reference up at <paramref name="at"/> (an [Owned] argument or a setter's
    /// value): it owns a reference (a local only ever given new lambdas, null or fresh calls; an [Owned] parameter; a setter's value),
    /// and <paramref name="at"/> is its last use: no use after it (also not in the same call), not in a loop that does not declare it,
    /// not inside a nested lambda or local function, and no other use that keeps or frees it (a store, a return, a copy, a second
    /// hand-over, Heap.Free, a capture by a stored closure) on a path that reaches it: uses on another branch of the same if, ?: or
    /// switch do not count, and a lambda on the stack (an argument) may call it in a statement that ends before. Every other case
    /// takes a new reference at the call, which can only leak (see <see cref="WarnUnfreedOriginal"/>), never free twice.
    /// </summary>
    private bool MovesAt(ISymbol v, IdentifierNameSyntax at)
    {
        if (v is IParameterSymbol p && !IsOwned(p) && p.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertySet }) return false;
        if (v is not (ILocalSymbol or IParameterSymbol)) return false;
        FrameScopeOf(v, out var fn);
        if (fn == null) return false;
        var decl = v is ILocalSymbol ? v.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() : null;
        if (v is ILocalSymbol && decl is not VariableDeclaratorSyntax) return false;
        if (fn.DescendantNodes().Any(n => n is GotoStatementSyntax or LabeledStatementSyntax)) return false;
        for (var anc = at.Parent; anc != null && anc != fn; anc = anc.Parent)
        {
            if (IsFunctionNode(anc)) return false;   // a nested lambda or local function may run it more than once
            var loopBody = anc switch
            {
                WhileStatementSyntax w => w.Statement,
                DoStatementSyntax d => d.Statement,
                ForStatementSyntax f => f.Statement,
                CommonForEachStatementSyntax fe => fe.Statement,
                _ => null,
            };
            if (loopBody != null && (decl == null || !loopBody.Span.Contains(decl.Span))) return false;
        }
        if (decl is VariableDeclaratorSyntax { Initializer: { } init } && !OwnedValue(v, init.Value)) return false;
        foreach (var u in fn.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (u == at || u.Identifier.Text != v.Name || !SymbolEqualityComparer.Default.Equals(Model(u).GetSymbolInfo(u).Symbol, v)) continue;
            var other = OnOtherBranch(u, at);
            var capture = CaptureOf(u, fn);
            if (capture == CaptureKind.Kept) return false;   // captured by a stored closure or a local function: it may use it later
            if (capture == CaptureKind.Borrowed && !other && !BorrowedCaptureEndsBefore(u, fn, at)) return false;
            var node = ValueSite(u, out var parent);
            if (parent is AssignmentExpressionSyntax set && set.Left == node)
            {
                if (set.IsKind(SyntaxKind.SimpleAssignmentExpression) && OwnedValue(v, set.Right)) continue;
                // d += x / d -= x on a variable that owns its list (it rebuilds it, still owned), before the hand-over: the list moves.
                if (IsOwnedCompound(set, v) && (other || u.SpanStart < at.SpanStart)) continue;
                return false;   // an alias, a compound assignment that does not own, one after the hand-over, or ??= (which also reads it)
            }
            if (other) continue;   // another branch of the same if, ?: or switch: it never runs together with the hand-over
            if (u.SpanStart > at.SpanStart) return false;   // read after the hand-over: the callee may have freed it by then
            if (parent is ArgumentSyntax arg && arg.Expression == node && IsHeapFreeArgument(arg)) return false;
            var site = Classify(u);
            if (site.Kind == SiteKind.Borrowed) continue;
            if (site.Kind == SiteKind.Kept && parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.AddAssignmentExpression } add && add.Right == node
                && Model(add).GetSymbolInfo(add.Left).Symbol is IEventSymbol) continue;   // the event took its own reference
            return false;
        }
        return true;
    }

    /// <summary>
    /// A value <paramref name="v"/> owns once given it: new (FreshValue), or, for a multicast target (a variable with an owned += or
    /// -=), also a method group or anything else <see cref="NewOrEmptyValue"/> accepts, as the += check does.
    /// </summary>
    private bool OwnedValue(ISymbol v, ExpressionSyntax value) =>
        FreshValue(value, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)) || IsCompoundTarget(v) && NewOrEmptyValue(value);

    /// <summary>True when <paramref name="set"/> is d += x or d -= x on the delegate variable <paramref name="v"/> (not an event) and v owns every value it holds (the check of <see cref="CheckCompoundTarget"/>).</summary>
    private bool IsOwnedCompound(AssignmentExpressionSyntax set, ISymbol v) =>
        (set.IsKind(SyntaxKind.AddAssignmentExpression) || set.IsKind(SyntaxKind.SubtractAssignmentExpression))
        && Model(set).GetSymbolInfo(set.Left).Symbol is not IEventSymbol && OwnsAllValues(v);

    private bool IsCompoundTarget(ISymbol v)
    {
        var fn = v.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var scope = fn == null ? null : FunctionOf(fn) ?? fn;
        return scope != null && scope.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a =>
            (a.IsKind(SyntaxKind.AddAssignmentExpression) || a.IsKind(SyntaxKind.SubtractAssignmentExpression))
            && SymbolEqualityComparer.Default.Equals(Model(a).GetSymbolInfo(a.Left).Symbol, v)) && OwnsAllValues(v);
    }

    private bool OwnsAllValues(ISymbol v)
    {
        var type = v switch { ILocalSymbol l => l.Type, IParameterSymbol p => p.Type, _ => null };
        if (type?.TypeKind != TypeKind.Delegate || IsBorrowedParameter(v)) return false;
        if (!_ownsValues.TryGetValue(v, out var owns)) _ownsValues[v] = owns = AllValuesFresh(v);
        return owns;
    }

    private enum CaptureKind { None, Borrowed, Kept }

    /// <summary>
    /// How the use <paramref name="u"/> sits in <paramref name="fn"/>: directly (None), only inside lambdas on the stack (Borrowed:
    /// no frame holds the variable, the lambdas only borrow it), or inside a stored closure or a local function (Kept).
    /// </summary>
    private CaptureKind CaptureOf(SyntaxNode u, SyntaxNode fn)
    {
        var kind = CaptureKind.None;
        for (var anc = u.Parent; anc != null && anc != fn; anc = anc.Parent)
        {
            if (!IsFunctionNode(anc)) continue;
            if (anc is not LambdaExpressionSyntax lam || IsHeapLambda(lam)) return CaptureKind.Kept;
            kind = CaptureKind.Borrowed;
        }
        return kind;
    }

    /// <summary>
    /// True when the stack lambdas around <paramref name="u"/> are done before <paramref name="at"/>: the outermost one is an argument
    /// (so it lives only for that call) in a statement that ends before <paramref name="at"/>.
    /// </summary>
    private bool BorrowedCaptureEndsBefore(SyntaxNode u, SyntaxNode fn, SyntaxNode at)
    {
        LambdaExpressionSyntax? outer = null;
        for (var anc = u.Parent; anc != null && anc != fn; anc = anc.Parent)
            if (anc is LambdaExpressionSyntax lam) outer = lam;
        if (outer == null || Classify(outer).Kind != SiteKind.Borrowed) return false;
        var stmt = outer.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
        return stmt != null && stmt.Span.End <= at.SpanStart;
    }

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> are on different branches of the same <c>if</c>/<c>else</c>, <c>?:</c>,
    /// switch statement or switch expression, so at most one of them runs (a loop around both is handled by the caller).
    /// </summary>
    private static bool OnOtherBranch(SyntaxNode a, SyntaxNode b)
    {
        var common = a.Parent;
        while (common != null && !common.Span.Contains(b.Span)) common = common.Parent;
        static bool In(SyntaxNode? part, SyntaxNode n) => part != null && part.Span.Contains(n.Span);
        return common switch
        {
            IfStatementSyntax i => In(i.Statement, a) && In(i.Else, b) || In(i.Else, a) && In(i.Statement, b),
            ConditionalExpressionSyntax c => In(c.WhenTrue, a) && In(c.WhenFalse, b) || In(c.WhenFalse, a) && In(c.WhenTrue, b),
            SwitchStatementSyntax sw => sw.Sections.FirstOrDefault(x => In(x, a)) is { } sa && sw.Sections.FirstOrDefault(x => In(x, b)) is { } sb && sa != sb
                && !sa.DescendantNodes().OfType<GotoStatementSyntax>().Any() && !sb.DescendantNodes().OfType<GotoStatementSyntax>().Any(),
            SwitchExpressionSyntax se => se.Arms.FirstOrDefault(x => In(x, a)) is { } ea && se.Arms.FirstOrDefault(x => In(x, b)) is { } eb && ea != eb
                && !In(ea.WhenClause, a) && !In(eb.WhenClause, b) && !In(ea.Pattern, a) && !In(eb.Pattern, b),
            _ => false,
        };
    }

    private readonly HashSet<ISymbol> _unfreedWarned = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// ES0013 when a variable that owns its reference is given to an [Owned] parameter or a setter that takes a new one (it is used
    /// again, handed over more than once, or in a loop) and nothing else frees or keeps the variable's own: it would leak. A capture
    /// by a lambda on the stack is only a use (no frame owns the variable), and a Heap.Free, store or return on another branch of
    /// the same if, ?: or switch does not free it on the hand-over's path.
    /// </summary>
    private void WarnUnfreedOriginal(ISymbol v, IdentifierNameSyntax at, string to)
    {
        if (_unfreedWarned.Contains(v)) return;
        if (v is IParameterSymbol p && !IsOwned(p) && p.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertySet }) return;
        FrameScopeOf(v, out var fn);
        if (fn == null) return;
        if (v is ILocalSymbol)
        {
            if (v.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax vd) return;
            var anyValue = false;
            if (vd.Initializer != null)
            {
                if (!OwnedValue(v, vd.Initializer.Value)) return;   // an alias: owns nothing
                anyValue = !IsNullConstant(vd.Initializer.Value);
            }
            foreach (var set in fn.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                if (set.Left is IdentifierNameSyntax l && l.Identifier.Text == v.Name && SymbolEqualityComparer.Default.Equals(Model(l).GetSymbolInfo(l).Symbol, v))
                {
                    if (IsOwnedCompound(set, v)) { anyValue |= set.IsKind(SyntaxKind.AddAssignmentExpression) && !IsNullConstant(set.Right); continue; }
                    if (!set.IsKind(SyntaxKind.SimpleAssignmentExpression) || !OwnedValue(v, set.Right)) return;
                    anyValue |= !IsNullConstant(set.Right);
                }
            if (!anyValue) return;
        }
        foreach (var u in fn.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (u.Identifier.Text != v.Name || !SymbolEqualityComparer.Default.Equals(Model(u).GetSymbolInfo(u).Symbol, v)) continue;
            if (CaptureOf(u, fn) == CaptureKind.Kept) return;   // captured by a stored closure: its frame owns it
            var node = ValueSite(u, out var parent);
            if (parent is AssignmentExpressionSyntax set && set.Left == node) continue;
            if (OnOtherBranch(u, at)) continue;   // freed, stored or given away only on a path that does not reach this hand-over
            if (parent is ArgumentSyntax arg && arg.Expression == node && IsHeapFreeArgument(arg)) return;   // freed
            if (IsHandoverSite(node, parent)) continue;
            var site = Classify(u);
            if (site.Kind == SiteKind.Borrowed) continue;
            if (site.Kind == SiteKind.Kept && parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.AddAssignmentExpression } add && add.Right == node
                && Model(add).GetSymbolInfo(add.Left).Symbol is IEventSymbol) continue;
            return;   // stored, returned or copied: that owner frees it
        }
        _unfreedWarned.Add(v);
        _diagnostics.Add(Diagnostic.Create(ESharp.Analyzers.Descriptors.ClosureNeverFreed, at.GetLocation(),
            $"'{v.Name}' is given to {to} but is used again, given more than once or given inside a loop, so the callee takes a reference of its own and '{v.Name}' keeps one that is never freed: call Heap.Free(ref {v.Name}) after its last use"));
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
        var owned = f.Vars.Where(v => FrameOwns(f, v) == Ownership.Strong).ToList();
        var fini = owned.Count == 0 ? "" :
            $"static void {f.Struct}_fini(es_frame *f) {{ {string.Concat(owned.Select(v => $"es_closure_release(((struct {f.Struct} *)f)->{FrameField(v)}.env); "))}}}\n";
        _spans[f.Struct] = $"struct {f.Struct} {{ es_frame hdr;{fields} }};\n{fini}";
        var up = f.Up != null ? f.Up.Var : _outerFrames.Count > 0 ? "_env" : "NULL";
        Line($"struct {f.Struct} *{f.Var} __attribute__((cleanup(es_frame_drop))) = (struct {f.Struct} *)es_frame_new(sizeof(struct {f.Struct}), {up});");
        if (owned.Count > 0) Line($"{f.Var}->hdr.fini = {f.Struct}_fini;");
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
        // Into a variable of the very frame the closure points at (see Ownership.Weak): no reference, or the two would hold each other.
        var intoOwnFrame = IntoWeakSlot(lam);
        return $"(({dt}){{ {FuncName(m)}, {(borrowedForRemoval || intoOwnFrame ? envRef : $"es_closure_retain({envRef})")} }})";
    }

    // ------------------------------------------------------------------ delegates owned by frames (issue #85)

    /// <summary>
    /// How a frame holds a delegate variable it captured. None: the frame does not own it (an alias of a field, a value that was
    /// stored, returned or handed to an [Owned] parameter, a borrowed parameter, or anything not followed): the frame never releases
    /// it, as before. Strong: the variable owns its reference (an [Owned] parameter, a setter's value, a local only ever given new
    /// closures, null or fresh calls, and only called, compared, passed to borrowed parameters, added to an event, which takes its own
    /// reference, or Heap.Free'd, which nulls it); the frame releases it when it is freed. Weak: Strong, but every closure the variable
    /// is given points at this same frame (a lambda over the variables of the scope that declares it, or over 'this'); such a value
    /// takes no reference at all, so the frame and the closure do not keep each other alive, and the frame releases nothing for it.
    /// </summary>
    private enum Ownership { None, Strong, Weak }

    private readonly Dictionary<ISymbol, Ownership> _ownership = new(SymbolEqualityComparer.Default);

    private Ownership FrameOwns(Frame f, ISymbol v)
    {
        if (_ownership.TryGetValue(v, out var known)) return known;
        _ownership[v] = Ownership.None;   // asked again while deciding: not owned (the old behaviour, always memory-safe)
        return _ownership[v] = DecideOwnership(f, v);
    }

    private Ownership DecideOwnership(Frame f, ISymbol v)
    {
        var values = new List<ExpressionSyntax>();
        var strong = false;
        switch (v)
        {
            case IParameterSymbol { RefKind: RefKind.None, Type.TypeKind: TypeKind.Delegate } p:
                if (!IsOwned(p) && p.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertySet }) return Ownership.None;
                strong = true;   // the caller handed over a reference
                break;
            case ILocalSymbol { RefKind: RefKind.None, Type.TypeKind: TypeKind.Delegate } l:
                if (l.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax vd) return Ownership.None;
                if (vd.Initializer != null) values.Add(vd.Initializer.Value);
                break;
            default:
                return Ownership.None;
        }
        var freed = false;
        foreach (var id in f.Function.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Identifier.Text != v.Name || !SymbolEqualityComparer.Default.Equals(Model(id).GetSymbolInfo(id).Symbol, v)) continue;
            var node = ValueSite(id, out var parent);
            if (parent is AssignmentExpressionSyntax set && set.Left == id)
            {
                if (!set.IsKind(SyntaxKind.SimpleAssignmentExpression) && !set.IsKind(SyntaxKind.CoalesceAssignmentExpression)) return Ownership.None;
                values.Add(set.Right);
                continue;
            }
            if (parent is ArgumentSyntax arg && arg.Expression == id && IsHeapFreeArgument(arg)) { freed = true; continue; }
            var site = Classify(id);
            if (site.Kind == SiteKind.Borrowed) continue;
            // E += v: the event takes a reference of its own (v is not a new delegate), so v still owns its one.
            if (site.Kind == SiteKind.Kept && parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.AddAssignmentExpression } add && add.Right == node
                && Model(add).GetSymbolInfo(add.Left).Symbol is IEventSymbol) continue;
            // Given to an [Owned] parameter or a setter that takes a reference of its own (issue #110): v still owns its one.
            if (site.Kind == SiteKind.Kept && IsHandoverSite(node, parent) && !MovesAt(v, id)) continue;
            return Ownership.None;   // copied, stored, returned, handed on: someone else may free it
        }
        var weak = false;
        foreach (var value in values)
        {
            if (!FreshValue(value, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))) return Ownership.None;
            foreach (var leaf in FreshLeaves(value))
            {
                if (leaf is not LambdaExpressionSyntax lam) { strong = true; continue; }   // a fresh call: a reference of its own
                if (!IsHeapLambda(lam) || FunctionOf(lam) is not { } lamFn) continue;     // no closure: a null env, nothing to hold
                var env = EnvChain(lam, lamFn);
                if (env.Count == 0) continue;
                if (env[0] == f) weak = true; else strong = true;
            }
        }
        return weak && !strong && !freed ? Ownership.Weak : Ownership.Strong;
    }

    /// <summary>The lambdas and calls a fresh value (see <see cref="FreshValue"/>) can be, through ?:, ??, casts and switch arms; null is left out.</summary>
    private static IEnumerable<ExpressionSyntax> FreshLeaves(ExpressionSyntax v)
    {
        v = Strip(v);
        switch (v)
        {
            case CastExpressionSyntax c:
                return FreshLeaves(c.Expression);
            case ConditionalExpressionSyntax c:
                return FreshLeaves(c.WhenTrue).Concat(FreshLeaves(c.WhenFalse));
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b:
                return FreshLeaves(b.Left).Concat(FreshLeaves(b.Right));
            case SwitchExpressionSyntax sw:
                return sw.Arms.SelectMany(a => FreshLeaves(a.Expression));
            case LambdaExpressionSyntax or InvocationExpressionSyntax:
                return new[] { v };
            default:
                return Array.Empty<ExpressionSyntax>();
        }
    }

    /// <summary>True when <paramref name="lam"/> is the value given to a delegate variable that its frame holds weakly.</summary>
    private bool IntoWeakSlot(LambdaExpressionSyntax lam)
    {
        var node = ValueSite(lam, out var site);
        var target = site switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd } => Model(vd).GetDeclaredSymbol(vd),
            AssignmentExpressionSyntax a when a.Right == node && (a.IsKind(SyntaxKind.SimpleAssignmentExpression) || a.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                => Model(a).GetSymbolInfo(a.Left).Symbol,
            _ => null,
        };
        if (target is not (ILocalSymbol or IParameterSymbol) || FrameScopeOf(target, out var owner) is not { } scope || owner == null) return false;
        return FramePlan(owner).TryGetValue(scope, out var f) && f.Vars.Contains(target, SymbolEqualityComparer.Default)
            && FrameOwns(f, target) == Ownership.Weak;
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

    /// <summary>
    /// <c>d += x</c> and <c>d -= x</c> on a delegate that is not an event (a local, parameter, field, static or auto-property), as in
    /// dotnet: d becomes a multicast delegate, { trampoline, es_mc } where the es_mc is a reference-counted list of { fn, env } that owns
    /// one reference per entry (the events' es_dg and closure references). Invoking it calls every entry in order and gives the last
    /// result; -= removes the last matching entry (or the last matching run, for a multicast operand), and removing the last entry
    /// leaves null. The old value's reference is dropped, so d must own its closure, as Heap.Free(ref d) assumes; Heap.Free(ref d)
    /// then releases the entries.
    /// </summary>
    private string DelegateCompound(AssignmentExpressionSyntax a, INamedTypeSymbol del, bool top)
    {
        var invoke = del.DelegateInvokeMethod;
        if (invoke == null) return Unsupported(a, "+= or -= on this delegate");
        if (Split.Active || LibMode) return Unsupported(a, "+= or -= on a delegate that is not an event in a precompiled package or an Amiga library (the multicast trampoline must be one function per program)");
        if (invoke.ReturnsByRef || invoke.ReturnsByRefReadonly || invoke.Parameters.Any(p => p.RefKind != RefKind.None))
            return Unsupported(a, "+= or -= on a delegate type with ref or out parameters or a ref return");
        if (WrittenProperty(a.Left) != null) return Unsupported(a, "+= or -= on a property with accessors; use an auto-property, a field or a local");
        if (IsRefVariable(Model(a).GetSymbolInfo(a.Left).Symbol) != null)
            return Unsupported(a, "+= or -= on a ref, out or in delegate variable");
        if (!RequireSimple(a.Left, "+= or -= on a delegate")) return "0";
        if (!CheckCompoundTarget(a)) return "0";
        _usesClosures = true;
        _usesMulticast = true;
        var dt = CType(del, a);
        var (add, sub) = MulticastHelpers(del, invoke, dt, a);
        var l = Expr(a.Left);
        string call;
        if (IsNullConstant(a.Right)) return "((void)0)";   // adding or removing null changes nothing
        if (a.IsKind(SyntaxKind.SubtractAssignmentExpression)) call = $"{sub}({l}, {Expr(a.Right, top: true)})";
        else
        {
            if (IsFreshDelegate(a.Right) is not bool fresh) return "0";
            call = $"{add}({l}, {Expr(a.Right, top: true)}, {(fresh ? 1 : 0)})";
        }
        var s = $"{l} = {call}";
        return top ? s : $"({s})";
    }

    private bool _usesMulticast;

    private readonly Dictionary<ISymbol, bool> _ownsValues = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// += and -= drop the reference of the value they replace, so the variable must own it: a borrowed parameter does not (the es_mc frame
    /// would leak, and Heap.Free is refused), and a variable that was ever given a copy of another delegate (a variable, a field, a parameter,
    /// a call that may return one) shares that owner's reference, so dropping it frees the other owner's closure.
    /// </summary>
    private bool CheckCompoundTarget(AssignmentExpressionSyntax a)
    {
        var sym = Model(a).GetSymbolInfo(a.Left).Symbol;
        if (IsBorrowedParameter(sym))
        {
            ReportOnce(a, $"+= or -= on the borrowed delegate parameter '{sym!.Name}' (the new value could not be freed); mark the parameter [Owned]");
            return false;
        }
        if (sym is not (ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol))
        {
            ReportOnce(a, "+= or -= on this kind of delegate variable (only a local, parameter, field or auto-property whose every value is a new lambda, a method or null)");
            return false;
        }
        if (!_ownsValues.TryGetValue(sym, out var owns))
            _ownsValues[sym] = owns = AllValuesFresh(sym);
        if (!owns)
            ReportOnce(a, $"+= or -= on '{sym.Name}', which is also given a copy of another delegate (a variable, field, parameter or a call that may return one): that copy shares the other owner's reference, which += would drop; give it only lambdas, methods or null, or build the list in a variable of its own");
        if (owns && CopyOutlivesCompound(sym) is { } why)
        {
            ReportOnce(a, why);
            return false;
        }
        return owns;
    }

    private readonly Dictionary<ISymbol, string?> _copyChecks = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// += and -= release the list the variable pointed at, so a copy of the variable taken before (<c>e = d</c>, <c>Field = d</c>, an
    /// initializer, a tuple element, a pattern variable, <c>new Action(d)</c>) must not be used after them. For a local or parameter the
    /// check is flow-sensitive: a copy is refused only when a += / -= can run after it was taken and the copy (or a copy of it) is used
    /// after that += / -= (by position, around a loop, or inside a lambda or local function); copies taken after the last += / -= and
    /// copies not used afterwards stay allowed. A copy stored anywhere but a local (a field, element, out parameter, tuple) cannot be
    /// followed, so it is refused when a += / -= can run after it. A field, static or auto-property can be changed from any method, so
    /// there the check is flow-insensitive: += / -= is refused when the symbol is read as a copy anywhere. Returns the message, or null.
    /// </summary>
    private string? CopyOutlivesCompound(ISymbol d)
    {
        if (_copyChecks.TryGetValue(d, out var known)) return known;
        return _copyChecks[d] = CopyOutlivesCompoundCore(d);
    }

    private string? CopyOutlivesCompoundCore(ISymbol d)
    {
        SyntaxNode? fn = null;
        if (d is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }) FrameScopeOf(d, out fn);
        var nodes = fn != null ? fn.DescendantNodes() : _compilation.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes());
        var copies = CopiesOf(d, nodes);
        // A += / -= whose value is used (Action e = (d += A)) hands out a copy of the new list too.
        foreach (var x in nodes.OfType<AssignmentExpressionSyntax>())
            if ((x.IsKind(SyntaxKind.AddAssignmentExpression) || x.IsKind(SyntaxKind.SubtractAssignmentExpression))
                && x.Parent is not ExpressionStatementSyntax && SymbolEqualityComparer.Default.Equals(Model(x).GetSymbolInfo(x.Left).Symbol, d))
                copies.Add((x, null));
        if (copies.Count == 0) return null;
        if (fn == null) fn = OnlyFunctionOf(d, copies);
        const string fix = "; read the copy before the +=, take it after the last +=, or build the list in a variable of its own";
        if (fn == null)
            return $"+= or -= on '{d.Name}', which is also read as a copy elsewhere (an assignment, initializer, tuple element or pattern variable): the copy shares the list's reference, which += or -= releases, so using it afterwards would read freed memory; this check cannot follow a field, static or property across methods, so it refuses every such copy{fix}";
        if (fn.DescendantNodes().Any(n => n is GotoStatementSyntax or LabeledStatementSyntax))
            return $"+= or -= on '{d.Name}', which is also read as a copy, in a method with goto or labels (the order of the copy and the += cannot be followed)";
        var sites = fn.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(x =>
            (x.IsKind(SyntaxKind.AddAssignmentExpression) || x.IsKind(SyntaxKind.SubtractAssignmentExpression))
            && SymbolEqualityComparer.Default.Equals(Model(x).GetSymbolInfo(x.Left).Symbol, d)).ToList();
        var seen = new HashSet<(ISymbol?, SyntaxNode)>();
        var work = new Queue<(SyntaxNode At, ISymbol? Dest)>(copies);
        while (work.Count > 0)
        {
            var (at, dest) = work.Dequeue();
            if (!seen.Add((dest, at))) continue;
            foreach (var s in sites)
            {
                if (!MayFollow(at, s, fn, d)) continue;
                if (dest == null)
                    return $"+= or -= on '{d.Name}' can run after '{d.Name}' was copied into something this check cannot follow (a field, element, out parameter, tuple or pattern variable): the copy shares the list's reference, which += or -= releases, so using it afterwards would read freed memory{fix}";
                FrameScopeOf(dest, out var efn);
                if (efn != fn)
                    return $"+= or -= on '{d.Name}' can run after '{d.Name}' was copied into '{dest.Name}' of another function{fix}";
                var decl = dest.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                foreach (var u in fn.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (u.Identifier.Text != dest.Name || !SymbolEqualityComparer.Default.Equals(Model(u).GetSymbolInfo(u).Symbol, dest)) continue;
                    var node = ValueSite(u, out var parent);
                    if (parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } w && w.Left == node) continue;   // only written
                    if (UsedAfter(u, s, fn, decl))
                        return $"'{dest.Name}' is a copy of '{d.Name}' and is used after '{d.Name}' += or -= (line {s.GetLocation().GetLineSpan().StartLinePosition.Line + 1}), which releases the list the copy points at: a use after free{fix}";
                }
            }
            if (dest == null) continue;
            // A copy of the copy: it dies with the same list.
            foreach (var u in fn.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (u.Identifier.Text != dest.Name || !SymbolEqualityComparer.Default.Equals(Model(u).GetSymbolInfo(u).Symbol, dest)) continue;
                var c = CopyAt(u);
                if (c != null) work.Enqueue(c.Value);
            }
        }
        return null;
    }

    /// <summary>
    /// For a field, static or property: the one method that holds every += / -= on it and every copy of it, when nothing can run in
    /// between from outside: a plain method with no attributes (but [EntryPoint]) that nothing calls (the entry point, say), so it cannot be re-entered
    /// through a call, a delegate or an override. Null otherwise (the check is then flow-insensitive).
    /// </summary>
    private SyntaxNode? OnlyFunctionOf(ISymbol d, List<(SyntaxNode At, ISymbol? Dest)> copies)
    {
        SyntaxNode? only = null;
        foreach (var tree in _compilation.SyntaxTrees)
            foreach (var x in tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!(x.IsKind(SyntaxKind.AddAssignmentExpression) || x.IsKind(SyntaxKind.SubtractAssignmentExpression))) continue;
                if (!SymbolEqualityComparer.Default.Equals(Model(x).GetSymbolInfo(x.Left).Symbol, d)) continue;
                var f = FunctionOf(x);
                if (f is not MethodDeclarationSyntax || only != null && only != f) return null;
                only = f;
            }
        if (only == null) return null;
        foreach (var (at, _) in copies) if (FunctionOf(at) != only) return null;
        var m = Model(only).GetDeclaredSymbol(only) as IMethodSymbol;
        if (m == null || m.GetAttributes().Any(at => at.AttributeClass?.Name != "EntryPointAttribute") || m.IsVirtual || m.IsOverride || m.IsAbstract || m.ExplicitInterfaceImplementations.Length > 0) return null;
        if (m.ContainingType.AllInterfaces.Any(i => i.GetMembers().Any(im => SymbolEqualityComparer.Default.Equals(m.ContainingType.FindImplementationForInterfaceMember(im), m)))) return null;
        foreach (var tree in _compilation.SyntaxTrees)
            foreach (var id in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
                if (id.Identifier.Text == m.Name && SymbolEqualityComparer.Default.Equals(Model(id).GetSymbolInfo(id).Symbol?.OriginalDefinition, m.OriginalDefinition)) return null;
        return only;
    }

    private bool Nested(SyntaxNode n, SyntaxNode fn)
    {
        for (var anc = n.Parent; anc != null && anc != fn; anc = anc.Parent)
            if (IsFunctionNode(anc)) return true;
        return false;
    }

    private static bool CommonLoop(SyntaxNode a, SyntaxNode b, SyntaxNode fn, SyntaxNode? declOutside)
    {
        for (var anc = a.Parent; anc != null && anc != fn; anc = anc.Parent)
        {
            var body = anc switch
            {
                WhileStatementSyntax w => w.Statement,
                DoStatementSyntax dd => dd.Statement,
                ForStatementSyntax f => f.Statement,
                CommonForEachStatementSyntax fe => fe.Statement,
                _ => null,
            };
            if (body == null || !anc.Span.Contains(b.Span)) continue;
            if (declOutside == null || !body.Span.Contains(declOutside.Span)) return true;   // the variable outlives one iteration
        }
        return false;
    }

    /// <summary>True when <paramref name="s"/> (a += / -= on d) can run after the copy at <paramref name="at"/> was taken.</summary>
    private bool MayFollow(SyntaxNode at, SyntaxNode s, SyntaxNode fn, ISymbol d)
    {
        if (Nested(at, fn) || Nested(s, fn)) return true;
        var decl = d is ILocalSymbol ? d.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() : null;
        if (CommonLoop(at, s, fn, decl ?? fn)) return true;
        return s.SpanStart >= at.Span.End && !OnOtherBranch(at, s);
    }

    /// <summary>True when the use <paramref name="u"/> of a copy can run after <paramref name="s"/>.</summary>
    private bool UsedAfter(SyntaxNode u, SyntaxNode s, SyntaxNode fn, SyntaxNode? aliasDecl)
    {
        if (Nested(u, fn) || Nested(s, fn)) return true;
        if (CommonLoop(u, s, fn, aliasDecl ?? fn)) return true;
        return u.SpanStart >= s.SpanStart && !OnOtherBranch(u, s);
    }

    /// <summary>The places where the delegate variable <paramref name="d"/> is read and the copy lives on in a variable, field or other store.</summary>
    private List<(SyntaxNode At, ISymbol? Dest)> CopiesOf(ISymbol d, IEnumerable<SyntaxNode> nodes)
    {
        var result = new List<(SyntaxNode, ISymbol?)>();
        foreach (var u in nodes.OfType<IdentifierNameSyntax>())
        {
            if (u.Identifier.Text != d.Name || !SymbolEqualityComparer.Default.Equals(Model(u).GetSymbolInfo(u).Symbol, d)) continue;
            if (CopyAt(u) is { } c) result.Add(c);
        }
        return result;
    }

    /// <summary>Whether the read of a delegate variable at <paramref name="u"/> copies it: the node and the local it lands in (null: somewhere else).</summary>
    private (SyntaxNode At, ISymbol? Dest)? CopyAt(IdentifierNameSyntax u)
    {
        SyntaxNode expr = u.Parent is MemberAccessExpressionSyntax ma && ma.Name == u ? ma : u;
        while (true)
        {
            var node = ValueSite(expr, out var parent);
            switch (parent)
            {
                case ArgumentSyntax { Parent: BaseArgumentListSyntax { Parent: ObjectCreationExpressionSyntax oc } } arg
                    when arg.Expression == node && Model(oc).GetTypeInfo(oc).Type?.TypeKind == TypeKind.Delegate:
                    expr = oc;   // new Action(d) is a copy
                    continue;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } asx when asx.Left == node:
                    expr = asx;
                    continue;
                case ArgumentSyntax { Parent: TupleExpressionSyntax }:
                case InitializerExpressionSyntax:
                case ReturnStatementSyntax:
                case ArrowExpressionClauseSyntax:
                case YieldStatementSyntax:
                    return (u, null);
                case SimpleLambdaExpressionSyntax sl when sl.Body == node:
                case ParenthesizedLambdaExpressionSyntax pl when pl.Body == node:
                    return (u, null);
                case IsPatternExpressionSyntax ip when ip.Expression == node && ip.Pattern.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>().Any():
                    return (u, null);
                case SwitchStatementSyntax sw when sw.Expression == node && sw.Sections.Any(sec => sec.Labels.Any(l => l.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Any())):
                case SwitchExpressionSyntax se when se.GoverningExpression == node && se.Arms.Any(arm => arm.Pattern.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>().Any()):
                    return (u, null);
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd }:
                    return (u, Model(vd).GetDeclaredSymbol(vd) is ILocalSymbol { RefKind: RefKind.None } l && vd.Parent?.Parent is LocalDeclarationStatementSyntax ? l : null);
                case EqualsValueClauseSyntax:
                    return (u, null);
                case AssignmentExpressionSyntax a when a.Right == node && (a.IsKind(SyntaxKind.SimpleAssignmentExpression) || a.IsKind(SyntaxKind.CoalesceAssignmentExpression)):
                {
                    var left = Model(a).GetSymbolInfo(a.Left).Symbol;
                    if (left is IPropertySymbol ps && IsHandoverSetter(ps)) return null;   // the setter takes its own reference
                    if (a.Parent is not ExpressionStatementSyntax) return (u, null);   // the value of the assignment is used: not followed
                    return (u, left is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None } ? left : null);
                }
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// True when every value <paramref name="sym"/> is given is new or holds no closure (a lambda, a method group, null, a call that returns
    /// a new lambda): through =, ??=, an initializer or a parameter default. Any other way of writing it is taken as a copy that it does not
    /// own: a ref or out argument (except Heap.Free), a ref to it (ref local, ref return, ref conditional), a deconstruction, and a variable
    /// declared by a pattern or an out var.
    /// </summary>
    private bool AllValuesFresh(ISymbol sym)
    {
        foreach (var r in sym.DeclaringSyntaxReferences)
            if (r.GetSyntax() is SingleVariableDesignationSyntax) return false;   // is Action d, case Action d, out Action d, var (d, x)
        bool Is(SemanticModel model, ExpressionSyntax e) => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(Strip(e)).Symbol, sym);
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var model = Model(tree);
            foreach (var n in tree.GetRoot().DescendantNodes())
            {
                ExpressionSyntax? value = null;
                switch (n)
                {
                    case AssignmentExpressionSyntax asg when asg.IsKind(SyntaxKind.SimpleAssignmentExpression) || asg.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                        if (asg.Left is TupleExpressionSyntax or DeclarationExpressionSyntax)
                        {
                            if (asg.Left.DescendantNodes().OfType<ExpressionSyntax>().Any(x => Is(model, x))) return false;
                        }
                        else if (Is(model, asg.Left)) value = asg.Right;
                        break;
                    case ArgumentSyntax arg when arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword):
                        if (Is(model, arg.Expression) && !IsHeapFreeArgument(arg)) return false;
                        break;
                    case RefExpressionSyntax re:
                        if (Is(model, re.Expression)) return false;
                        break;
                    case VariableDeclaratorSyntax { Initializer: { } init } vd:
                        if (SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(vd), sym)) value = init.Value;
                        break;
                    case PropertyDeclarationSyntax { Initializer: { } pinit } pd:
                        if (SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(pd), sym)) value = pinit.Value;
                        break;
                    case ParameterSyntax { Default: { } def } ps:
                        if (SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(ps), sym)) value = def.Value;
                        break;
                }
                if (value != null && !NewOrEmptyValue(value)) return false;
            }
        }
        return true;
    }

    private bool NewOrEmptyValue(ExpressionSyntax e)
    {
        e = Strip(e);
        switch (e)
        {
            case LambdaExpressionSyntax:
                return true;
            case ExpressionSyntax when IsNullConstant(e):
                return true;
            case InvocationExpressionSyntax inv:
                return IsFreshCall(inv, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
            case CastExpressionSyntax c:
                return NewOrEmptyValue(c.Expression);
            case ConditionalExpressionSyntax c:
                return NewOrEmptyValue(c.WhenTrue) && NewOrEmptyValue(c.WhenFalse);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } b:
                return NewOrEmptyValue(b.Left) && NewOrEmptyValue(b.Right);
            case SwitchExpressionSyntax sw:
                return sw.Arms.All(arm => NewOrEmptyValue(arm.Expression));
            case ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 1 } oc:
                return NewOrEmptyValue(oc.ArgumentList.Arguments[0].Expression);
            default:
                // A method group (a name or member access that is a method, not a variable): its environment is null.
                return Model(e).GetSymbolInfo(e).Symbol is IMethodSymbol;
        }
    }

    /// <summary>The functions that add to and remove from a multicast delegate of type <paramref name="dt"/> (and its trampoline), written once per type.</summary>
    private (string Add, string Sub) MulticastHelpers(INamedTypeSymbol del, IMethodSymbol invoke, string dt, SyntaxNode at)
    {
        string mc = dt + "_mc", add = dt + "_add", sub = dt + "_sub";
        var key = "ES_zmc_" + dt;
        if (_spans.ContainsKey(key)) return (add, sub);
        var ps = invoke.Parameters.Select((p, i) => Decl(p.Type, $"a{i}", at)).Prepend("void *env").ToList();
        var args = string.Join(", ", invoke.Parameters.Select((_, i) => $"a{i}"));
        var comma = args.Length == 0 ? "" : ", ";
        var isVoid = invoke.ReturnType.SpecialType == SpecialType.System_Void;
        var ft = $"__typeof__((({dt} *)0)->fn)";
        var sb = new StringBuilder();
        sb.Append($"static __attribute__((unused)) {Decl(invoke.ReturnType, $"{mc}({string.Join(", ", ps)})", at)}\n{{\n");
        sb.Append("    es_mc *m = (es_mc *)env;\n    m->h.rc++;\n");
        sb.Append($"    for (int32_t i = 0; i < m->n - 1; i++) (({ft})m->items[i].fn)(m->items[i].env{comma}{args});\n");
        if (isVoid)
            sb.Append($"    (({ft})m->items[m->n - 1].fn)(m->items[m->n - 1].env{comma}{args});\n    es_frame_release(m);\n");
        else
            sb.Append($"    {Decl(invoke.ReturnType, "r", at)} = (({ft})m->items[m->n - 1].fn)(m->items[m->n - 1].env{comma}{args});\n    es_frame_release(m);\n    return r;\n");
        sb.Append("}\n");
        sb.Append($"static __attribute__((unused)) {dt} {add}({dt} cur, {dt} v, int fresh)\n{{\n");
        sb.Append($"    es_vfn f = (es_vfn)cur.fn;\n    es_dg_add((es_vfn){mc}, &f, &cur.env, (es_vfn)v.fn, v.env, fresh);\n    cur.fn = ({ft})f;\n    return cur;\n}}\n");
        sb.Append($"static __attribute__((unused)) int {dt}_eq({dt} a, {dt} b)\n{{\n    return es_dg_eq((es_vfn){mc}, (es_vfn)a.fn, a.env, (es_vfn)b.fn, b.env);\n}}\n");
        sb.Append($"static __attribute__((unused)) {dt} {sub}({dt} cur, {dt} v)\n{{\n");
        sb.Append($"    es_vfn f = (es_vfn)cur.fn;\n    es_dg_sub((es_vfn){mc}, &f, &cur.env, (es_vfn)v.fn, v.env);\n    cur.fn = ({ft})f;\n    return cur;\n}}\n");
        _spans[key] = sb.ToString();
        return (add, sub);
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
    private void AppendClosureRuntime(StringBuilder result)
    {
        foreach (var line in new[]
        {
            "/* Heap closures: reference-counted frames of captured variables, each holding a reference to the frame around it.",
            "   A frame that owns delegate variables (an [Owned] parameter, a setter's value, a local only given new closures) has a fini",
            "   that drops their references before the frame is freed. */",
            "typedef struct es_frame { int32_t rc; struct es_frame *up; void (*fini)(struct es_frame *); } es_frame;",
            RuntimeState("int32_t es_frames_live;"),
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
            "        if (f->fini != NULL) f->fini(f);",
            "        " + FreeName + "(f);",
            "        es_frames_live--;",
            "        f = up;",
            "    }",
            "}",
            "static __attribute__((unused)) void es_frame_drop(void *slot) { es_frame_release(*(void **)slot); }",
            "/* References held by closures (a stored lambda, or an event's copy of a delegate), for Heap.LiveClosures(). */",
            RuntimeState("int32_t es_closures_live;"),
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
            "    if (n == 0) { " + FreeName + "(e->items); e->items = NULL; e->cap = 0; }",
            "}",
            "static __attribute__((unused)) void es_event_add(es_event *e, void (*fn)(void), void *env)",
            "{",
            "    if (fn == NULL) return;",
            "    if (e->count == e->cap)",
            "    {",
            "        int32_t cap = e->cap == 0 ? 4 : e->cap * 2;",
            "        es_dg *items = (es_dg *)es_new((size_t)cap * sizeof(es_dg));",
            "        for (int32_t i = 0; i < e->count; i++) items[i] = e->items[i];",
            "        " + FreeName + "(e->items);",
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
        if (_usesMulticast)
            foreach (var line in new[]
        {
            "/* Multicast delegates (d += x on a delegate that is not an event): { trampoline, es_mc }, the es_mc a reference-counted frame holding",
            "   one reference per entry. It never changes once built: += and -= make a new one and drop the old. */",
            "typedef void (*es_vfn)(void);",
            "typedef struct { es_frame h; int32_t n; es_dg items[1]; } es_mc;",
            "static __attribute__((unused)) void es_mc_fini(es_frame *f)",
            "{",
            "    es_mc *m = (es_mc *)f;",
            "    for (int32_t i = 0; i < m->n; i++) es_closure_release(m->items[i].env);",
            "}",
            "static __attribute__((unused)) es_dg *es_mc_items(es_vfn tramp, es_vfn fn, void *env, es_dg *one, int32_t *n)",
            "{",
            "    if (fn == tramp) { es_mc *m = (es_mc *)env; *n = m->n; return m->items; }",
            "    one->fn = fn; one->env = env; one->dead = 0; *n = 1;",
            "    return one;",
            "}",
            "static __attribute__((unused)) es_mc *es_mc_new(int32_t n)",
            "{",
            "    es_mc *m = (es_mc *)es_frame_new(sizeof(es_mc) + (size_t)(n > 1 ? n - 1 : 0) * sizeof(es_dg), NULL);",
            "    m->h.fini = es_mc_fini;",
            "    es_closures_live++;   /* the reference of the delegate that holds it */",
            "    return m;",
            "}",
            "static __attribute__((unused)) void es_mc_push(es_mc *m, const es_dg *d)",
            "{",
            "    m->items[m->n].fn = d->fn; m->items[m->n].env = es_closure_retain(d->env); m->items[m->n].dead = 0;",
            "    m->n++;",
            "}",
            "/* cur += v: v hands over its reference when fresh, else the entries take their own. The old value's reference is dropped. */",
            "static __attribute__((unused)) void es_dg_add(es_vfn tramp, es_vfn *cfn, void **cenv, es_vfn vfn, void *venv, int fresh)",
            "{",
            "    es_dg c1, v1; int32_t nc, nv;",
            "    if (vfn == NULL) return;",
            "    if (*cfn == NULL) { if (!fresh) es_closure_retain(venv); *cfn = vfn; *cenv = venv; return; }",
            "    es_dg *ci = es_mc_items(tramp, *cfn, *cenv, &c1, &nc);",
            "    es_dg *vi = es_mc_items(tramp, vfn, venv, &v1, &nv);",
            "    es_mc *m = es_mc_new(nc + nv);",
            "    for (int32_t i = 0; i < nc; i++) es_mc_push(m, &ci[i]);",
            "    for (int32_t i = 0; i < nv; i++) es_mc_push(m, &vi[i]);",
            "    es_closure_release(*cenv);",
            "    if (fresh) es_closure_release(venv);",
            "    *cfn = tramp; *cenv = m;",
            "}",
            "static __attribute__((unused)) int es_dg_eq(es_vfn tramp, es_vfn afn, void *aenv, es_vfn bfn, void *benv)",
            "{",
            "    es_dg a1, b1; int32_t na, nb;",
            "    if (afn != bfn) return 0;",
            "    if (aenv == benv) return 1;",
            "    if (afn != tramp) return 0;",
            "    es_dg *ai = es_mc_items(tramp, afn, aenv, &a1, &na);",
            "    es_dg *bi = es_mc_items(tramp, bfn, benv, &b1, &nb);",
            "    if (na != nb) return 0;",
            "    for (int32_t i = 0; i < na; i++) if (ai[i].fn != bi[i].fn || ai[i].env != bi[i].env) return 0;",
            "    return 1;",
            "}",
            "/* cur -= v: the last entry (or run of entries) equal to v goes; no match changes nothing; nothing left is null. */",
            "static __attribute__((unused)) void es_dg_sub(es_vfn tramp, es_vfn *cfn, void **cenv, es_vfn vfn, void *venv)",
            "{",
            "    es_dg c1, v1; int32_t nc, nv, k;",
            "    if (vfn == NULL || *cfn == NULL) return;",
            "    es_dg *ci = es_mc_items(tramp, *cfn, *cenv, &c1, &nc);",
            "    es_dg *vi = es_mc_items(tramp, vfn, venv, &v1, &nv);",
            "    for (k = nc - nv; k >= 0; k--)",
            "    {",
            "        int32_t j;",
            "        for (j = 0; j < nv; j++) if (ci[k + j].fn != vi[j].fn || ci[k + j].env != vi[j].env) break;",
            "        if (j == nv) break;",
            "    }",
            "    if (k < 0) return;",
            "    int32_t rest = nc - nv;",
            "    if (rest == 0) { es_closure_release(*cenv); *cfn = NULL; *cenv = NULL; return; }",
            "    if (rest == 1)",
            "    {",
            "        const es_dg *keep = &ci[k == 0 ? nv : 0];",
            "        es_vfn kf = keep->fn;",
            "        void *ke = es_closure_retain(keep->env);",
            "        es_closure_release(*cenv);",
            "        *cfn = kf; *cenv = ke;",
            "        return;",
            "    }",
            "    es_mc *m = es_mc_new(rest);",
            "    for (int32_t i = 0; i < nc; i++) if (i < k || i >= k + nv) es_mc_push(m, &ci[i]);",
            "    es_closure_release(*cenv);",
            "    *cfn = tramp; *cenv = m;",
            "}",
        }) result.AppendLine(line);
    }
}
