using System;
using System.Collections.Generic;
using System.Linq;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// The checks for E# library packages (issue #31) beyond the subset rules, which run on package sources as on the program's:
/// the manifest (target list, licence, E# version, problems found when the package was read) and the sources (platform-level
/// features, declared in the package or called from it, that the manifest does not list, an entry point, a type declared twice, internal members used from outside, a class derived
/// from a package class not marked [Extensible]).
/// </summary>
internal static class PackageChecks
{
    /// <summary>Checks on the manifests alone, before any source is looked at.</summary>
    public static void Manifests(CompileOptions options, List<Diagnostic> diagnostics)
    {
        foreach (var p in options.Packages)
        {
            foreach (var problem in p.Problems)
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageInvalid, Location.None, p.Id, p.Version, problem));
            var m = p.Manifest;
            if (m == null) continue;
            if (options.TargetName != null && m.Targets.Count > 0 && !m.Targets.Contains(options.TargetName, StringComparer.OrdinalIgnoreCase))
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageTarget, Location.None, p.Id, p.Version, options.TargetName, string.Join(", ", m.Targets)));
            if (!options.AllowCopyleft && Licenses.IsCopyleft(p.License))
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageCopyleft, Location.None, p.Id, p.Version, p.License));
            if (options.SdkVersion is { } have && SdkVersions.NeedsNewer(m.MinSdk, have))
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageSdk, Location.None, p.Id, p.Version, m.MinSdk, have));
        }
        PackageNatives.Check(options, diagnostics);
    }

    /// <summary>Checks on the parsed sources of the program and the packages (<paramref name="packageOf"/>: each package tree's package).</summary>
    public static void Sources(CSharpCompilation compilation, IReadOnlyCollection<SyntaxTree> programTrees,
        IReadOnlyDictionary<SyntaxTree, EsPackage> packageOf, List<Diagnostic> diagnostics)
    {
        if (packageOf.Count == 0) return;
        string Origin(SyntaxTree tree) => packageOf.TryGetValue(tree, out var p) ? $"package '{p.Id}'" : "the program";
        EsPackage? PackageOf(ISymbol symbol)
        {
            var refs = symbol.OriginalDefinition.DeclaringSyntaxReferences;
            if (refs.IsEmpty) return null;
            return packageOf.TryGetValue(refs[0].SyntaxTree, out var p) && refs.All(r => packageOf.TryGetValue(r.SyntaxTree, out var q) && q == p) ? p : null;
        }
        var allTrees = programTrees.Concat(packageOf.Keys.OrderBy(t => t.FilePath, StringComparer.Ordinal)).ToList();

        // Platform-level features declared in the package: an [AmigaLib] or [Native] extern, an [InlineAsm] body, an [Interrupt] server or
        // a [HookEntry] method (the system calls those with a target-specific convention, from another task or interrupt).
        foreach (var (tree, package) in packageOf.OrderBy(k => k.Key.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                foreach (var attr in method.AttributeLists.SelectMany(l => l.Attributes))
                {
                    var name = model.GetSymbolInfo(attr).Symbol?.ContainingType?.Name ?? attr.Name.ToString().Split('.').Last();
                    var feature = name switch
                    {
                        "AmigaLibAttribute" or "AmigaLib" => "AmigaLib",
                        "NativeAttribute" or "Native" => "Native",
                        "InlineAsmAttribute" or "InlineAsm" => "InlineAsm",
                        "InterruptAttribute" or "Interrupt" => "Interrupt",
                        "HookEntryAttribute" or "HookEntry" => "HookEntry",
                        _ => null,
                    };
                    if (feature != null && package.Manifest != null && !package.Manifest.Declares(feature))
                        diagnostics.Add(Diagnostic.Create(Descriptors.PackageFeature, attr.GetLocation(), package.Id, feature, method.Identifier.Text));
                }
        }

        // The same features used rather than declared: a call to (or a method group of) an [AmigaLib] or [Native] extern declared
        // elsewhere, such as the platform's raw bindings (Exec.Forbid, Asl.AllocAslRequest, Dos.PutStr on the Amiga targets) or
        // the C runtime's es_std_* functions. The platform API built on them (Gfx, Video, Files, ...) is not raw and needs nothing.
        // Each such method once per package, at its first use. What is raw depends on the target being built (Dos.PutStr is a
        // binding on the Amiga targets and a plain method on sdl), so the check is per target, as the build is.
        var reported = new HashSet<(string Package, string Method)>();
        foreach (var (tree, package) in packageOf.OrderBy(k => k.Key.FilePath, StringComparer.Ordinal))
        {
            if (package.Manifest == null) continue;
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (node.Parent is MethodDeclarationSyntax) continue;
                var info = model.GetSymbolInfo(node);
                if ((info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null)) is not IMethodSymbol called) continue;
                called = called.ReducedFrom ?? called;
                called = called.OriginalDefinition;
                if (!called.IsExtern || PackageOf(called) == package) continue;   // its own externs: the declaration check above
                var names = called.GetAttributes().Select(a => a.AttributeClass?.Name).ToList();
                var feature = names.Contains("AmigaLibAttribute") ? "AmigaLib" : names.Contains("NativeAttribute") ? "Native" : null;
                if (feature == null || package.Manifest.Declares(feature) || !reported.Add((package.Id, called.ToDisplayString()))) continue;
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageFeature, node.GetLocation(), package.Id, feature,
                    called.ContainingType.Name + "." + called.Name));
            }
        }

        // An entry point belongs to the program.
        foreach (var entry in CEmitter.FindEntryPoints(compilation, new HashSet<SyntaxTree>(packageOf.Keys)))
            if (PackageOf(entry) is { } p)
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageEntryPoint, entry.Locations[0], p.Id, entry.ToDisplayString()));

        // The same type (namespace, name and arity) declared in two places: C# would merge partial declarations silently.
        // Generated asset classes (Assets.g.cs from an assets.json, the program's or a package's) are named in the message, since the
        // fix is in that assets.json ("namespace", "class").
        var declared = new Dictionary<string, List<(string Origin, Location At)>>(StringComparer.Ordinal);
        bool IsAssets(Location at) => at.SourceTree is { } t && (packageOf.TryGetValue(t, out var p)
            ? p.Manifest?.Assets is { } a && t.FilePath == p.PathPrefix + a.Source
            : string.Equals(System.IO.Path.GetFileName(t.FilePath), "Assets.g.cs", StringComparison.OrdinalIgnoreCase));
        foreach (var tree in allTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                         .Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type) continue;
                var key = (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." : "") + type.MetadataName;
                if (!declared.TryGetValue(key, out var list)) declared[key] = list = new List<(string, Location)>();
                list.Add((Origin(tree), decl is BaseTypeDeclarationSyntax b ? b.Identifier.GetLocation() : ((DelegateDeclarationSyntax)decl).Identifier.GetLocation()));
            }
        }
        foreach (var (key, list) in declared.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var origins = list.Select(l => l.Origin).Distinct().ToList();
            if (origins.Count < 2) continue;
            string Label(string origin) => list.Any(l => l.Origin == origin && IsAssets(l.At)) ? origin + " (its assets: the \"namespace\" and \"class\" of its assets.json)" : origin;
            foreach (var other in origins.Skip(1))
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageNameClash, list.First(l => l.Origin == other).At,
                    key.Replace('`', '\''), Label(origins[0]), Label(other)));
        }

        foreach (var tree in allTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            packageOf.TryGetValue(tree, out var here);
            var origin = Origin(tree);

            // A class of the program (or of another package) derived from a package's class that is not marked [Extensible]. An
            // [Extensible] one is compiled with the program like any other class (vtables, overrides, constructor chaining); the
            // analyzer gives the same ES0117 in the IDE, where the package is a referenced assembly (SubsetAnalyzer.CheckClass).
            foreach (var cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                if (model.GetDeclaredSymbol(cls) is { BaseType: { } baseType } type && PackageOf(baseType) is { } owner && owner != here &&
                    !baseType.OriginalDefinition.GetAttributes().Any(a => a.AttributeClass is { Name: "ExtensibleAttribute", ContainingNamespace: { Name: "ESharp", ContainingNamespace.IsGlobalNamespace: true } }))
                    diagnostics.Add(Diagnostic.Create(Descriptors.PackageDerive, cls.Identifier.GetLocation(), type.Name, baseType.ToDisplayString(), owner.Id));

            // Internal members of a package, used from elsewhere: one assembly here, two in the dotnet host build.
            foreach (var node in tree.GetRoot().DescendantNodes().Where(n => n is SimpleNameSyntax or BaseObjectCreationExpressionSyntax))
            {
                var info = model.GetSymbolInfo(node);
                var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
                if (symbol == null || symbol is INamespaceSymbol) continue;
                symbol = symbol.OriginalDefinition;
                if (PackageOf(symbol) is not { } owner || owner == here) continue;
                if (!IsInternal(symbol)) continue;
                diagnostics.Add(Diagnostic.Create(Descriptors.PackageInternal, node.GetLocation(),
                    symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), owner.Id, origin));
            }
        }
    }

    /// <summary>The symbol, or a type that contains it, is internal (or private protected): out of reach from another assembly.</summary>
    private static bool IsInternal(ISymbol symbol)
    {
        for (var s = symbol; s != null && s is not INamespaceSymbol; s = s.ContainingSymbol)
        {
            if (s is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove }) continue;
            if (s.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal) return true;
        }
        return false;
    }
}
