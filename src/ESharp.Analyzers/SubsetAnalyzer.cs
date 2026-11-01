using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ESharp.Analyzers;

/// <summary>
/// Flags C# that falls outside the E# subset. Runs in the IDE as you type and
/// again inside the compiler, so the compiler never sees out-of-subset code.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubsetAnalyzer : DiagnosticAnalyzer
{
    public const string AllowFloatProperty = "build_property.ESharpAllowFloat";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Descriptors.SubsetRules;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(CheckException,
            SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression, SyntaxKind.TryStatement);
        context.RegisterSyntaxNodeAction(CheckTypeOf, SyntaxKind.TypeOfExpression);
        context.RegisterSyntaxNodeAction(CheckLinqQuery,
            SyntaxKind.QueryExpression);
        context.RegisterSyntaxNodeAction(CheckAsync,
            SyntaxKind.AwaitExpression);
        context.RegisterSyntaxNodeAction(CheckAsyncModifier,
            SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement,
            SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
        context.RegisterSyntaxNodeAction(CheckClass, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(CheckInterface, SyntaxKind.InterfaceDeclaration);
        context.RegisterSyntaxNodeAction(CheckGenericDeclaration,
            SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(CheckInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(CheckTypeSyntax,
            SyntaxKind.PredefinedType, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(CheckLiteral, SyntaxKind.NumericLiteralExpression);
        context.RegisterSyntaxNodeAction(CheckStructFields, SyntaxKind.StructDeclaration);
    }

    private static bool AllowFloat(SyntaxNodeAnalysisContext c)
    {
        var options = c.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        return options.TryGetValue(AllowFloatProperty, out var v) &&
               string.Equals(v, "true", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckException(SyntaxNodeAnalysisContext c)
    {
        var text = c.Node switch
        {
            TryStatementSyntax => "try",
            _ => "throw",
        };
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.Exceptions, c.Node.GetLocation(), text));
    }

    private static void CheckTypeOf(SyntaxNodeAnalysisContext c) =>
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.Reflection, c.Node.GetLocation(), "typeof"));

    private static void CheckLinqQuery(SyntaxNodeAnalysisContext c) =>
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.Linq, c.Node.GetLocation(), "query expression"));

    private static void CheckAsync(SyntaxNodeAnalysisContext c) =>
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.AsyncAwait, c.Node.GetLocation(), "await"));

    private static void CheckAsyncModifier(SyntaxNodeAnalysisContext c)
    {
        var modifiers = c.Node switch
        {
            MethodDeclarationSyntax m => m.Modifiers,
            LocalFunctionStatementSyntax l => l.Modifiers,
            LambdaExpressionSyntax l => l.Modifiers,
            AnonymousMethodExpressionSyntax a => a.Modifiers,
            _ => default,
        };
        foreach (var m in modifiers)
        {
            if (m.IsKind(SyntaxKind.AsyncKeyword))
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.AsyncAwait, m.GetLocation(), "async"));
        }
    }

    private static void CheckClass(SyntaxNodeAnalysisContext c)
    {
        var decl = (ClassDeclarationSyntax)c.Node;
        foreach (var m in decl.Modifiers)
            if (m.IsKind(SyntaxKind.StaticKeyword)) return;
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, decl.Identifier.GetLocation(), decl.Identifier.Text));
    }

    private static void CheckInterface(SyntaxNodeAnalysisContext c)
    {
        var decl = (InterfaceDeclarationSyntax)c.Node;
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.Interfaces, decl.Identifier.GetLocation(), decl.Identifier.Text));
    }

    private static void CheckGenericDeclaration(SyntaxNodeAnalysisContext c)
    {
        var (list, name, loc) = c.Node switch
        {
            ClassDeclarationSyntax x => (x.TypeParameterList, x.Identifier.Text, x.Identifier.GetLocation()),
            StructDeclarationSyntax x => (x.TypeParameterList, x.Identifier.Text, x.Identifier.GetLocation()),
            MethodDeclarationSyntax x => (x.TypeParameterList, x.Identifier.Text, x.Identifier.GetLocation()),
            _ => (null, "", Location.None),
        };
        if (list != null)
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Generics, loc, name));
    }

    private static void CheckInvocation(SyntaxNodeAnalysisContext c)
    {
        if (c.SemanticModel.GetSymbolInfo(c.Node, c.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
        var type = method.ContainingType?.ToDisplayString() ?? "";

        if (type == "System.Linq.Enumerable" || type == "System.Linq.Queryable")
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Linq, c.Node.GetLocation(), method.Name));
        else if (ns.StartsWith("System.Reflection") || type == "System.Type" ||
                 (type == "System.Object" && method.Name == "GetType"))
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Reflection, c.Node.GetLocation(), method.Name));

        if (method.IsGenericMethod)
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Generics, c.Node.GetLocation(), method.Name));
    }

    private static void CheckTypeSyntax(SyntaxNodeAnalysisContext c)
    {
        // Only inspect nodes that are in type position.
        var typeInfo = c.SemanticModel.GetTypeInfo(c.Node, c.CancellationToken);
        var symbol = c.SemanticModel.GetSymbolInfo(c.Node, c.CancellationToken).Symbol as ITypeSymbol;
        var type = symbol ?? (c.Node is PredefinedTypeSyntax ? typeInfo.Type : null);
        if (type == null) return;

        if (type.TypeKind == TypeKind.Dynamic)
        {
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Dynamic, c.Node.GetLocation()));
            return;
        }

        if (c.Node is IdentifierNameSyntax id && id.IsVar) return;

        switch (type.SpecialType)
        {
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                if (!AllowFloat(c))
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.FloatingPoint, c.Node.GetLocation(), type.ToDisplayString()));
                break;
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                if (c.Node is PredefinedTypeSyntax)
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.SlowArithmetic, c.Node.GetLocation(), type.ToDisplayString()));
                break;
        }
    }

    private static void CheckLiteral(SyntaxNodeAnalysisContext c)
    {
        var literal = (LiteralExpressionSyntax)c.Node;
        if (literal.Token.Value is float or double or decimal && !AllowFloat(c))
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.FloatingPoint, literal.GetLocation(), literal.Token.Text));
    }

    private static void CheckStructFields(SyntaxNodeAnalysisContext c)
    {
        var decl = (StructDeclarationSyntax)c.Node;
        foreach (var field in decl.Members.OfType<FieldDeclarationSyntax>())
        {
            var typeInfo = c.SemanticModel.GetTypeInfo(field.Declaration.Type, c.CancellationToken).Type;
            if (typeInfo is { IsReferenceType: true } && typeInfo.SpecialType != SpecialType.System_String)
            {
                foreach (var v in field.Declaration.Variables)
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.ReferenceField, v.GetLocation(), v.Identifier.Text, decl.Identifier.Text));
            }
        }
    }
}
