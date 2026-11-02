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
        context.RegisterSyntaxNodeAction(CheckArrayCreation,
            SyntaxKind.ArrayCreationExpression, SyntaxKind.ImplicitArrayCreationExpression);
        context.RegisterSyntaxNodeAction(CheckArrayShape, SyntaxKind.ArrayType);
        context.RegisterSyntaxNodeAction(CheckArraySignature,
            SyntaxKind.MethodDeclaration, SyntaxKind.OperatorDeclaration);
        context.RegisterSyntaxNodeAction(CheckArrayVariable, SyntaxKind.VariableDeclarator);
        context.RegisterSyntaxNodeAction(CheckArrayAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private const string ArrayAdvice =
        "arrays are fixed-size locals or static fields with a constant length ('int[] a = new int[16];'); pass them to methods as Span<T>";

    private static void ReportArray(SyntaxNodeAnalysisContext c, Location at, string what) =>
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.FixedArrays, at, $"{what}: {ArrayAdvice}"));

    private static bool IsArrayDeclaratorInitializer(SyntaxNode creation) =>
        creation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax or FieldDeclarationSyntax } } };

    private static void CheckArrayCreation(SyntaxNodeAnalysisContext c)
    {
        if (!IsArrayDeclaratorInitializer(c.Node))
        {
            ReportArray(c, c.Node.GetLocation(), "array created outside a variable declaration (no heap arrays)");
            return;
        }
        if (c.Node is ArrayCreationExpressionSyntax { Initializer: null } ac)
        {
            var size = ac.Type.RankSpecifiers[0].Sizes[0];
            if (size is OmittedArraySizeExpressionSyntax || !c.SemanticModel.GetConstantValue(size, c.CancellationToken).HasValue)
                ReportArray(c, size.GetLocation(), "array length is not a constant");
        }
    }

    private static void CheckArrayShape(SyntaxNodeAnalysisContext c)
    {
        var t = (ArrayTypeSyntax)c.Node;
        if (t.RankSpecifiers.Count > 1 || t.RankSpecifiers[0].Rank > 1)
            ReportArray(c, t.GetLocation(), "multi-dimensional and jagged arrays are not supported");
        else if (t.ElementType is ArrayTypeSyntax)
            ReportArray(c, t.GetLocation(), "jagged arrays are not supported");
    }

    private static void CheckArraySignature(SyntaxNodeAnalysisContext c)
    {
        var (parameters, returnType) = c.Node switch
        {
            MethodDeclarationSyntax m => (m.ParameterList, m.ReturnType),
            OperatorDeclarationSyntax o => (o.ParameterList, o.ReturnType),
            _ => (null, null),
        };
        if (parameters == null) return;
        foreach (var p in parameters.Parameters)
            if (p.Type is ArrayTypeSyntax)
                ReportArray(c, p.GetLocation(), $"array parameter '{p.Identifier.Text}'");
        if (returnType is ArrayTypeSyntax)
            ReportArray(c, returnType.GetLocation(), "array return type");
    }

    private static void CheckArrayVariable(SyntaxNodeAnalysisContext c)
    {
        var v = (VariableDeclaratorSyntax)c.Node;
        if (v.Parent is not VariableDeclarationSyntax { Type: ArrayTypeSyntax or NullableTypeSyntax { ElementType: ArrayTypeSyntax } } decl) return;
        if (decl.Parent is not (LocalDeclarationStatementSyntax or FieldDeclarationSyntax)) return;
        var init = v.Initializer?.Value;
        if (init is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or InitializerExpressionSyntax) return;
        ReportArray(c, v.GetLocation(), $"array '{v.Identifier.Text}' must be initialised with 'new T[N]' or an initializer list");
    }

    private static void CheckArrayAssignment(SyntaxNodeAnalysisContext c)
    {
        var a = (AssignmentExpressionSyntax)c.Node;
        if (c.SemanticModel.GetTypeInfo(a.Left, c.CancellationToken).Type is IArrayTypeSymbol)
            ReportArray(c, a.GetLocation(), "arrays cannot be reassigned");
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

    /// <summary>
    /// Classes are heap objects (allocated by <c>new</c>, freed with <c>Heap.Free</c>) held by pointer. There is no
    /// inheritance and no virtual dispatch: a class may implement interfaces (for generic constraints) but has no base
    /// class, and no member may be abstract, virtual, override or sealed-override.
    /// </summary>
    private static void CheckClass(SyntaxNodeAnalysisContext c)
    {
        var decl = (ClassDeclarationSyntax)c.Node;
        foreach (var m in decl.Modifiers)
            if (m.IsKind(SyntaxKind.StaticKeyword)) return;
        if (c.SemanticModel.GetDeclaredSymbol(decl, c.CancellationToken) is { BaseType: { SpecialType: not SpecialType.System_Object } baseType })
        {
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, decl.Identifier.GetLocation(), decl.Identifier.Text,
                $"it inherits from '{baseType.Name}': classes cannot inherit yet"));
            return;
        }
        foreach (var m in decl.Modifiers)
            if (m.IsKind(SyntaxKind.AbstractKeyword))
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, m.GetLocation(), decl.Identifier.Text, "abstract classes are not supported"));
        foreach (var member in decl.Members)
        {
            var mods = member switch
            {
                BaseMethodDeclarationSyntax x => x.Modifiers,
                BasePropertyDeclarationSyntax x => x.Modifiers,
                _ => default,
            };
            foreach (var m in mods)
                if (m.IsKind(SyntaxKind.VirtualKeyword) || m.IsKind(SyntaxKind.AbstractKeyword) || m.IsKind(SyntaxKind.OverrideKeyword))
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, m.GetLocation(), decl.Identifier.Text,
                        $"'{m.Text}' members need dynamic dispatch, which is not supported yet"));
        }
    }

    /// <summary>
    /// Interfaces may declare plain methods. A variable of interface type holds a class object plus its function table
    /// (dynamic dispatch); generic code constrained by an interface is monomorphised instead (static dispatch). Properties,
    /// events, generic methods and default implementations in interfaces are not supported.
    /// </summary>
    private static void CheckInterface(SyntaxNodeAnalysisContext c)
    {
        var decl = (InterfaceDeclarationSyntax)c.Node;
        foreach (var member in decl.Members)
        {
            if (member is MethodDeclarationSyntax { TypeParameterList: null, Body: null, ExpressionBody: null }) continue;
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Interfaces, member.GetLocation(), decl.Identifier.Text,
                member is MethodDeclarationSyntax ? "generic methods and default implementations are not supported" : "only plain methods are supported"));
        }
        if (decl.TypeParameterList != null)
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Interfaces, decl.Identifier.GetLocation(), decl.Identifier.Text, "generic interfaces are not supported"));
    }

    private static void CheckGenericDeclaration(SyntaxNodeAnalysisContext c)
    {
        var (list, name, loc) = c.Node switch
        {
            // Generic structs and methods are monomorphised; generic classes would be heap types.
            ClassDeclarationSyntax x => (x.TypeParameterList, x.Identifier.Text, x.Identifier.GetLocation()),
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

        if (method.IsGenericMethod && !method.OriginalDefinition.Locations.Any(l => l.IsInSource))
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
