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
public sealed partial class SubsetAnalyzer : DiagnosticAnalyzer
{
    public const string AllowFloatProperty = "build_property.ESharpAllowFloat";

    /// <summary>Set by the compiler, which parses E#'s library as source: the path prefixes of those files, separated by ';'.</summary>
    public const string LibrarySourcesProperty = "build_property.ESharpLibrarySources";

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
        context.RegisterSyntaxNodeAction(CheckBorrowedDelegate, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(CheckDelegateTypeArguments, SyntaxKind.GenericName, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(CheckLibraryInternal,
            SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterCompilationStartAction(Closures.Start);
    }

    private const string ArrayAdvice =
        "arrays are fixed-size locals or static fields with a constant length ('int[] a = new int[16];'); to pass one to a method, declare the parameter as Span<T> (also for class references) and hand over the array";

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
        // static void Main(string[] args) is the one array parameter: the emitter builds it from the command line.
        var isArgsEntry = c.Node is MethodDeclarationSyntax { Identifier.Text: "Main" } em && em.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is "EntryPoint" or "EntryPointAttribute" or "ESharp.EntryPoint" or "ESharp.EntryPointAttribute")
            && parameters.Parameters.Count == 1 && parameters.Parameters[0].Type is ArrayTypeSyntax { RankSpecifiers.Count: 1, ElementType: PredefinedTypeSyntax { Keyword.Text: "string" } };
        foreach (var p in parameters.Parameters)
            if (p.Type is ArrayTypeSyntax && !isArgsEntry)
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

    /// <summary>The LINQ operators E# turns into loops (over arrays and Enumerable.Range); the emitter checks how they are used.</summary>
    internal static readonly System.Collections.Generic.HashSet<string> SupportedLinq = new System.Collections.Generic.HashSet<string>
    {
        "Where", "Select", "Take", "Skip", "Count", "Sum", "Min", "Max", "Any", "All", "First", "FirstOrDefault",
        "Last", "LastOrDefault", "Contains", "Aggregate", "Range", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
    };

    private static void CheckLinqQuery(SyntaxNodeAnalysisContext c)
    {
        // from x in a [where ..] [let ..] select ..: one source, no joins, ordering, grouping or continuation.
        var q = (QueryExpressionSyntax)c.Node;
        string? unsupported = null;
        foreach (var clause in q.Body.Clauses)
            if (!(clause is WhereClauseSyntax || clause is LetClauseSyntax || clause is OrderByClauseSyntax)) unsupported = clause.Kind().ToString().Replace("Clause", "").ToLowerInvariant();
        if (q.Body.SelectOrGroup is GroupClauseSyntax) unsupported = "group";
        if (q.Body.Continuation != null) unsupported = "into";
        if (unsupported != null)
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Linq, c.Node.GetLocation(), "query clause '" + unsupported + "'"));
    }

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
    /// Classes are heap objects (allocated by <c>new</c>, freed with <c>Heap.Free</c>) held by pointer. A class may derive from
    /// one other plain class declared in the program (single inheritance, with virtual, abstract and override members, and
    /// <c>is</c>/<c>as</c> tests), or from an E# library class marked <c>[Extensible]</c> (such as <c>GtWindow</c>), and may implement
    /// interfaces. Other library classes (.NET's, or E#'s without the attribute) and generic bases are not supported.
    /// </summary>
    private static void CheckClass(SyntaxNodeAnalysisContext c)
    {
        var decl = (ClassDeclarationSyntax)c.Node;
        foreach (var m in decl.Modifiers)
            if (m.IsKind(SyntaxKind.StaticKeyword)) return;
        if (c.SemanticModel.GetDeclaredSymbol(decl, c.CancellationToken) is { BaseType: { SpecialType: not SpecialType.System_Object } baseType })
        {
            if (baseType.IsGenericType || baseType.IsStatic || baseType.TypeKind != TypeKind.Class)
            {
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, decl.Identifier.GetLocation(), decl.Identifier.Text,
                    $"it inherits from '{baseType.Name}': only a plain, non-generic class can be a base class"));
                return;
            }
            // E#'s own library is source in esharp build but a referenced assembly in the IDE: the same rule both ways, by attribute.
            if (IsESharpLibraryType(c, baseType))
            {
                if (!IsExtensible(baseType))
                {
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, decl.Identifier.GetLocation(), decl.Identifier.Text,
                        $"it inherits from '{baseType.Name}', a library class that is not marked [Extensible]: only library classes made for it (such as GtWindow) can be base classes"));
                    return;
                }
            }
            else if (!baseType.Locations.Any(l => l.IsInSource))
            {
                // An E# library package is source in esharp build (PackageChecks gives ES0117 there) and a referenced assembly in the
                // IDE and the dotnet host build: the same rule here, by the [Extensible] attribute in its metadata.
                if (EsPackageId(baseType.OriginalDefinition.ContainingAssembly) is { } package)
                {
                    if (!IsExtensible(baseType))
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Descriptors.PackageDerive, decl.Identifier.GetLocation(), decl.Identifier.Text,
                            baseType.OriginalDefinition.ToDisplayString(), package));
                        return;
                    }
                }
                else
                {
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, decl.Identifier.GetLocation(), decl.Identifier.Text,
                        $"it inherits from '{baseType.Name}': only a plain, non-generic class declared in the program, an [Extensible] E# library class or an [Extensible] class of an E# library package can be a base class"));
                    return;
                }
            }
        }
        foreach (var member in decl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop) continue;
            var dynamic = prop.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword) || m.IsKind(SyntaxKind.OverrideKeyword));
            var bodiless = prop.ExpressionBody == null && prop.AccessorList != null && prop.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null);
            if (dynamic && bodiless)
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.HeapClass, prop.Identifier.GetLocation(), decl.Identifier.Text,
                    "a virtual or override property needs accessors with bodies (an automatic property cannot be virtual)"));
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

        var linqSubset = (type == "System.Linq.Enumerable" && SupportedLinq.Contains(method.Name)) ||
                         (type == "System.MemoryExtensions" && method.Name == "Contains" && method.ReducedFrom != null && method.Parameters.Length == 1);
        if (type == "System.Linq.Queryable" || (type == "System.Linq.Enumerable" && !linqSubset))
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Linq, c.Node.GetLocation(), method.Name));
        else if (ns.StartsWith("System.Reflection") || type == "System.Type" ||
                 (type == "System.Object" && method.Name == "GetType"))
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Reflection, c.Node.GetLocation(), method.Name));

        CheckSince(c, method);

        // E#'s own library and E# library packages are source in esharp build but referenced assemblies in a host project (esharp
        // new, a game that references a package): their generic methods (Heap.Free<T>, a package's Pick<T>) are monomorphised like
        // the program's own, so they are not "library generics".
        if (method.IsGenericMethod && !linqSubset && !method.OriginalDefinition.Locations.Any(l => l.IsInSource) && !IsESharpLibrary(method) &&
            EsPackageId(method.OriginalDefinition.ContainingAssembly) == null)
            c.ReportDiagnostic(Diagnostic.Create(Descriptors.Generics, c.Node.GetLocation(), method.Name));
    }

    private static bool IsESharpLibrary(IMethodSymbol method)
    {
        var assembly = method.OriginalDefinition.ContainingAssembly?.Name;
        return assembly == "ESharp.Platform" || assembly == "ESharp.Runtime";
    }

    /// <summary>
    /// A type of E#'s own library: from the ESharp.Platform / ESharp.Runtime assemblies (IDE, host projects), or, where the compiler
    /// parses the library as source together with the program, declared in one of the library's source files. The compiler names
    /// those by path prefix in the <see cref="LibrarySourcesProperty"/> option (<esharp-lib>/Runtime/ and <esharp-lib>/Platform/, which no program path can start with); the IDE does not set it.
    /// </summary>
    private static bool IsESharpLibraryType(SyntaxNodeAnalysisContext c, INamedTypeSymbol type)
    {
        var assembly = type.OriginalDefinition.ContainingAssembly?.Name;
        if (assembly == "ESharp.Platform" || assembly == "ESharp.Runtime") return true;
        if (!c.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(LibrarySourcesProperty, out var prefixes) || string.IsNullOrEmpty(prefixes))
            return false;
        var list = prefixes.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        return type.OriginalDefinition.DeclaringSyntaxReferences.Any(r => list.Any(p => r.SyntaxTree.FilePath.StartsWith(p, System.StringComparison.Ordinal)));
    }

    /// <summary>
    /// ES0103: the compiler parses E#'s library and the program as one assembly, so C# lets the program reach the library's internal
    /// members; the dotnet host build (library as metadata) does not. Only where the library is source (the option is set) and only
    /// for files that are not library files. A member is out of reach when it, or a type containing it, is internal or private protected.
    /// </summary>
    private static void CheckLibraryInternal(SyntaxNodeAnalysisContext c)
    {
        if (!c.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(LibrarySourcesProperty, out var prefixes) || string.IsNullOrEmpty(prefixes))
            return;
        var list = prefixes.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        bool InLibrary(string path) => list.Any(p => path.StartsWith(p, System.StringComparison.Ordinal));
        if (InLibrary(c.Node.SyntaxTree.FilePath)) return;
        var info = c.SemanticModel.GetSymbolInfo(c.Node, c.CancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
        if (symbol == null) return;
        symbol = symbol.OriginalDefinition;
        if (symbol.DeclaringSyntaxReferences.IsEmpty || !symbol.DeclaringSyntaxReferences.All(r => InLibrary(r.SyntaxTree.FilePath))) return;
        for (var s = symbol; s != null && !(s is INamespaceSymbol); s = s.ContainingSymbol)
        {
            if (s is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove }) continue;
            if (s.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal)
            {
                c.ReportDiagnostic(Diagnostic.Create(Descriptors.LibraryInternal, c.Node.GetLocation(), symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                return;
            }
        }
    }

    /// <summary>
    /// The package id of a referenced assembly that is an E# library package: the <c>[assembly: ESharp.EsLibrary(id)]</c> that
    /// AmigaESharp.Sdk writes into every EsLibrary build, or, for a DLL without it (GenerateAssemblyInfo off), the assembly name of
    /// one that references ESharp.Runtime or ESharp.Platform (except ESharp.Target.Host). Null for any other assembly (.NET's own, for one).
    /// </summary>
    private static string? EsPackageId(IAssemblySymbol? assembly)
    {
        // E#'s own assemblies and its host fakes (ESharp.Target.Host, which references them) are never packages.
        if (assembly == null || assembly.Name is "ESharp.Platform" or "ESharp.Runtime" or "ESharp.Target.Host") return null;
        foreach (var a in assembly.GetAttributes())
            if (a.AttributeClass is { Name: "EsLibraryAttribute", ContainingNamespace: { Name: "ESharp", ContainingNamespace.IsGlobalNamespace: true } } &&
                a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string id && id.Length > 0)
                return id;
        foreach (var module in assembly.Modules)
            if (module.ReferencedAssemblies.Any(r => r.Name is "ESharp.Runtime" or "ESharp.Platform"))
                return assembly.Name;
        return null;
    }

    /// <summary>The library class is marked <c>[ESharp.Extensible]</c>: programs may derive from it.</summary>
    private static bool IsExtensible(INamedTypeSymbol type) =>
        type.OriginalDefinition.GetAttributes().Any(a => a.AttributeClass is { Name: "ExtensibleAttribute", ContainingNamespace: { Name: "ESharp", ContainingNamespace.IsGlobalNamespace: true } });

    private static void CheckSince(SyntaxNodeAnalysisContext c, IMethodSymbol method)
    {
        var def = method.OriginalDefinition;
        var have = LibraryVersions.For(c.Node.SyntaxTree.Options.PreprocessorSymbolNames);
        if (have == null) return;
        string? library = null;
        int? since = null;
        foreach (var a in def.GetAttributes())
        {
            if (a.AttributeClass is { Name: "SinceAttribute", ContainingNamespace: { Name: "ESharp" } } &&
                a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is int v)
                since = v;
            else if (a.AttributeClass is { Name: "AmigaLibAttribute", ContainingNamespace: { Name: "ESharp" } } &&
                     a.ConstructorArguments.Length >= 1 && a.ConstructorArguments[0].Value is string l)
                library = l;
        }
        if (since is not int need || need <= have) return;
        if (OptedIn(c, library, need)) return;
        c.ReportDiagnostic(Diagnostic.Create(Descriptors.LibraryTooOld, c.Node.GetLocation(), method.Name, need, have));
    }

    /// <summary>True if the call sits in code marked [RequiresLib] (method, type or assembly) for this library at this version or higher.</summary>
    private static bool OptedIn(SyntaxNodeAnalysisContext c, string? library, int need)
    {
        bool Matches(System.Collections.Immutable.ImmutableArray<AttributeData> attrs)
        {
            foreach (var a in attrs)
                if (a.AttributeClass is { Name: "RequiresLibAttribute", ContainingNamespace: { Name: "ESharp" } } &&
                    a.ConstructorArguments.Length == 2 && a.ConstructorArguments[0].Value is string name &&
                    a.ConstructorArguments[1].Value is int version && version >= need &&
                    (library == null || LibraryVersions.SameLibrary(name, library)))
                    return true;
            return false;
        }
        for (ISymbol? s = c.SemanticModel.GetEnclosingSymbol(c.Node.SpanStart, c.CancellationToken); s != null; s = s.ContainingSymbol)
            if (Matches(s.GetAttributes())) return true;
        return Matches(c.Compilation.Assembly.GetAttributes());
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
            // A static field is not part of a value: it is one variable of the program, so its type may be a reference type.
            if (field.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ConstKeyword))) continue;
            var typeInfo = c.SemanticModel.GetTypeInfo(field.Declaration.Type, c.CancellationToken).Type;
            if (typeInfo is { IsReferenceType: true } && typeInfo.SpecialType != SpecialType.System_String)
            {
                foreach (var v in field.Declaration.Variables)
                    c.ReportDiagnostic(Diagnostic.Create(Descriptors.ReferenceField, v.GetLocation(), v.Identifier.Text, decl.Identifier.Text));
            }
        }
    }
}
