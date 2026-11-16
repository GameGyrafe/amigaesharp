using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ESharp.Analyzers;

/// <summary>
/// Rules for closures without a garbage collector (issue #41). A capturing lambda that is only passed as an argument lives on the
/// stack of the method that made it, so a delegate parameter is borrowed: the callee may call it and pass it on, but keeping it
/// (ES0012) would leave a dangling pointer once the caller returns, unless the parameter is marked [Owned]. A capturing lambda
/// that is stored is a heap closure that its owner frees with Heap.Free; ES0013 warns where a stored closure is plainly never freed.
/// </summary>
public sealed partial class SubsetAnalyzer
{
    private static SyntaxNode ValueSite(SyntaxNode node, out SyntaxNode? parent)
    {
        parent = node.Parent;
        while (parent is ParenthesizedExpressionSyntax or CastExpressionSyntax
               || parent is PostfixUnaryExpressionSyntax pu && pu.IsKind(SyntaxKind.SuppressNullableWarningExpression)
               || parent is ConditionalExpressionSyntax c && c.Condition != node)
        {
            node = parent;
            parent = parent.Parent;
        }
        return node;
    }

    private static bool HasOwned(IParameterSymbol? p) =>
        p != null && p.GetAttributes().Any(a => a.AttributeClass is { Name: "OwnedAttribute", ContainingNamespace.Name: "ESharp" });

    private static IParameterSymbol? ParameterOf(SemanticModel model, ArgumentSyntax a)
    {
        if (a.Parent is not BaseArgumentListSyntax { Parent: { } call } list) return null;
        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol m) return null;
        if (a.NameColon != null) return m.Parameters.FirstOrDefault(p => p.Name == a.NameColon.Name.Identifier.Text);
        var index = list.Arguments.IndexOf(a);
        return index < m.Parameters.Length ? m.Parameters[index] : m.Parameters.LastOrDefault(p => p.IsParams);
    }

    private static bool IsHeapFree(SemanticModel model, ArgumentSyntax a) =>
        a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) && a.Parent?.Parent is InvocationExpressionSyntax inv &&
        model.GetSymbolInfo(inv).Symbol is IMethodSymbol { Name: "Free", ContainingType: { Name: "Heap", ContainingNamespace: { Name: "Platform", ContainingNamespace.Name: "ESharp" } } };

    /// <summary>How the value at <paramref name="node"/> is kept beyond the call (null if it is not): the words for the message.</summary>
    private static string? KeptHow(SemanticModel model, SyntaxNode node)
    {
        node = ValueSite(node, out var p);
        switch (p)
        {
            case AssignmentExpressionSyntax a when a.Right == node:
                if (a.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    return model.GetSymbolInfo(a.Left).Symbol is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None }
                        ? null : "stored in a field, property, static or array element";
                if (a.IsKind(SyntaxKind.AddAssignmentExpression) && model.GetSymbolInfo(a.Left).Symbol is IEventSymbol) return "added to an event";
                return null;
            case ReturnStatementSyntax:
            case ArrowExpressionClauseSyntax:
                return "returned";
            case InitializerExpressionSyntax:
                return "stored in an array";
            case ArgumentSyntax arg:
                if (IsHeapFree(model, arg)) return "freed (its caller owns it)";
                return HasOwned(ParameterOf(model, arg)) ? "passed to an [Owned] parameter" : null;
            default:
                return null;
        }
    }

    private static void CheckBorrowedDelegate(SyntaxNodeAnalysisContext c)
    {
        var id = (IdentifierNameSyntax)c.Node;
        if (c.SemanticModel.GetSymbolInfo(id, c.CancellationToken).Symbol is not IParameterSymbol { RefKind: RefKind.None } p) return;
        if (p.Type.TypeKind != TypeKind.Delegate || HasOwned(p)) return;
        // 'value' in a property setter is what the caller stored: it is treated like a field assignment at the call site.
        if (p.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove }) return;
        var how = KeptHow(c.SemanticModel, id);
        if (how != null)
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.BorrowedDelegate, id.GetLocation(), p.Name, how));
    }

    /// <summary>True when the lambda uses a local, a parameter or 'this' from outside itself (so it needs a closure).</summary>
    private static bool Captures(SemanticModel model, AnonymousFunctionExpressionSyntax lam)
    {
        foreach (var n in lam.DescendantNodes())
        {
            if (n is ThisExpressionSyntax or BaseExpressionSyntax) return true;
            if (n is not IdentifierNameSyntax id) continue;
            if (n.Parent is MemberAccessExpressionSyntax dot && dot.Name == n) continue;
            if (n.Parent is NameColonSyntax) continue;
            switch (model.GetSymbolInfo(id).Symbol)
            {
                case ILocalSymbol or IParameterSymbol { IsThis: false }:
                    var sym = model.GetSymbolInfo(id).Symbol!;
                    if (!sym.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == lam.SyntaxTree && lam.Span.Contains(r.Span))) return true;
                    break;
                case IFieldSymbol { IsStatic: false, IsConst: false }:
                case IPropertySymbol { IsStatic: false }:
                case IEventSymbol { IsStatic: false }:
                case IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary }:
                    return true;
            }
        }
        return false;
    }

    private static bool IsCapturingLambda(SemanticModel model, ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax pe) e = pe.Expression;
        return e is AnonymousFunctionExpressionSyntax lam && Captures(model, lam);
    }

    /// <summary>Collects the fields and events that receive capturing lambdas and those freed with Heap.Free; reports the rest at the end.</summary>
    private sealed class Closures
    {
        private readonly ConcurrentDictionary<ISymbol, Location> _received = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<ISymbol, bool> _freed = new(SymbolEqualityComparer.Default);

        public static void Start(CompilationStartAnalysisContext start)
        {
            var state = new Closures();
            start.RegisterSyntaxNodeAction(state.Assignment,
                SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression);
            start.RegisterSyntaxNodeAction(state.Argument, SyntaxKind.Argument);
            start.RegisterCompilationEndAction(state.End);
        }

        private void Assignment(SyntaxNodeAnalysisContext c)
        {
            var a = (AssignmentExpressionSyntax)c.Node;
            var target = c.SemanticModel.GetSymbolInfo(a.Left, c.CancellationToken).Symbol;
            var isEvent = target is IEventSymbol;
            if (a.IsKind(SyntaxKind.AddAssignmentExpression) && !isEvent) return;
            if (target is not (IFieldSymbol or IEventSymbol) && !(target is IPropertySymbol prop && IsAutoProperty(prop))) return;
            if (IsCapturingLambda(c.SemanticModel, a.Right))
            {
                _received.TryAdd(target, a.GetLocation());
                return;
            }
            if (isEvent) CheckLocalHandler(c, a);
        }

        private static bool IsAutoProperty(IPropertySymbol p) =>
            p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } list }
            && list.Accessors.All(a => a.Body == null && a.ExpressionBody == null);

        private void Argument(SyntaxNodeAnalysisContext c)
        {
            var arg = (ArgumentSyntax)c.Node;
            if (!IsHeapFree(c.SemanticModel, arg)) return;
            if (c.SemanticModel.GetSymbolInfo(arg.Expression, c.CancellationToken).Symbol is { } freed) _freed.TryAdd(freed, true);
        }

        /// <summary>E += h, where the local h holds a capturing lambda: the event takes its own reference, so h's must be freed too.</summary>
        private static void CheckLocalHandler(SyntaxNodeAnalysisContext c, AssignmentExpressionSyntax add)
        {
            var right = add.Right;
            while (right is ParenthesizedExpressionSyntax pe) right = pe.Expression;
            if (right is not IdentifierNameSyntax id || c.SemanticModel.GetSymbolInfo(id, c.CancellationToken).Symbol is not ILocalSymbol local) return;
            var body = add.Ancestors().FirstOrDefault(n => n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
            if (body == null) return;
            var capturing = false;
            foreach (var n in body.DescendantNodes())
            {
                if (n is VariableDeclaratorSyntax vd && vd.Initializer != null && SymbolEqualityComparer.Default.Equals(c.SemanticModel.GetDeclaredSymbol(vd), local))
                    capturing |= IsCapturingLambda(c.SemanticModel, vd.Initializer.Value);
                if (n is not IdentifierNameSyntax use || use.Identifier.Text != local.Name || !SymbolEqualityComparer.Default.Equals(c.SemanticModel.GetSymbolInfo(use).Symbol, local)) continue;
                var site = ValueSite(use, out var parent);
                if (parent is AssignmentExpressionSyntax asg && asg.Right == site && asg.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    if (KeptHow(c.SemanticModel, use) != null) return;   // stored elsewhere: that owner frees it
                }
                else if (parent is AssignmentExpressionSyntax set && set.Left == site && set.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    capturing |= IsCapturingLambda(c.SemanticModel, set.Right);
                else if (parent is ArgumentSyntax arg && (IsHeapFree(c.SemanticModel, arg) || HasOwned(ParameterOf(c.SemanticModel, arg)))) return;
                else if (parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax) return;
            }
            if (capturing)
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.ClosureNeverFreed, add.GetLocation(),
                    $"The event takes its own reference to the closure in '{local.Name}', and '{local.Name}' is never freed: call Heap.Free(ref {local.Name}) when it is no longer needed, or add the lambda directly (E += () => ...)"));
        }

        private void End(CompilationAnalysisContext c)
        {
            foreach (var pair in _received)
            {
                if (_freed.ContainsKey(pair.Key)) continue;
                var what = pair.Key is IEventSymbol ? "event" : "delegate";
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.ClosureNeverFreed, pair.Value,
                    $"The {what} '{pair.Key.Name}' is given a capturing lambda (a heap closure) but is never freed: call Heap.Free(ref {pair.Key.Name}) when it is no longer needed (for an event, inside its class)"));
            }
        }
    }
}
