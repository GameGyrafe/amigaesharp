using System;
using System.Collections.Generic;
using System.Linq;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// Monomorphisation as a source-to-source pass. Every use of a generic struct (<c>Stack&lt;int&gt;</c>)
/// or generic static method (<c>Max&lt;Fix&gt;(a, b)</c>, also with inferred arguments) is renamed to a
/// concrete name (<c>Stack__int</c>) and a copy of the generic declaration with the type parameters
/// replaced is added next to the original. The copies may contain further generic uses, so the pass
/// repeats until nothing is left. The generic originals stay in the tree (the emitter skips them), and
/// every copy starts with a <c>#line</c> directive so diagnostics and the C <c>#line</c> output point
/// at the original source.
/// </summary>
internal static class Monomorphizer
{
    private const int MaxRounds = 16;

    public sealed record Result(CSharpCompilation Compilation, HashSet<SyntaxTree> User, List<Diagnostic> Errors);

    private sealed class Use
    {
        public SimpleNameSyntax Node = null!;
        public string NewName = "";
    }

    private sealed class Instance
    {
        public SyntaxNode Def = null!;
        public SemanticModel Model = null!;
        public IReadOnlyList<ITypeParameterSymbol> Params = null!;
        public IReadOnlyList<ITypeSymbol> Args = null!;
        public string NewName = "";
    }

    public static Result Expand(CSharpCompilation compilation, HashSet<SyntaxTree> user, CSharpParseOptions parse)
    {
        var made = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<Diagnostic>();

        for (var round = 0; round < MaxRounds; round++)
        {
            var uses = new Dictionary<SyntaxTree, List<Use>>();
            var instances = new Dictionary<SyntaxTree, List<Instance>>();
            var any = false;

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    if (name is IdentifierNameSyntax { IsVar: true } || InGeneric(name)) continue;
                    var symbol = model.GetSymbolInfo(name).Symbol;
                    string? newName = null;
                    SyntaxNode? def = null;
                    IReadOnlyList<ITypeParameterSymbol>? tparams = null;
                    IReadOnlyList<ITypeSymbol>? targs = null;
                    string key = "";

                    if (name is GenericNameSyntax && symbol is INamedTypeSymbol { IsGenericType: true } t &&
                        !(t.ContainingType?.IsGenericType ?? false) && IsSource(t.OriginalDefinition) && Concrete(t.TypeArguments))
                    {
                        def = t.OriginalDefinition.DeclaringSyntaxReferences[0].GetSyntax();
                        tparams = t.OriginalDefinition.TypeParameters;
                        targs = t.TypeArguments;
                        newName = Mangle(t.Name, t.TypeArguments);
                        key = "T:" + t.OriginalDefinition.ToDisplayString() + "|" + newName;
                    }
                    // Heap.Free<T> is an emitter intrinsic (free + null): it is not copied per type.
                    else if (symbol is IMethodSymbol { IsGenericMethod: true } m && !m.ContainingType.IsGenericType &&
                             !(m.ContainingType.Name == "Heap" && m.ContainingNamespace?.ToDisplayString() == "ESharp.Platform") &&
                             IsSource(m.OriginalDefinition) && Concrete(m.TypeArguments))
                    {
                        def = m.OriginalDefinition.DeclaringSyntaxReferences[0].GetSyntax();
                        tparams = m.OriginalDefinition.TypeParameters;
                        targs = m.TypeArguments;
                        newName = Mangle(m.Name, m.TypeArguments);
                        key = "M:" + m.OriginalDefinition.ToDisplayString() + "|" + newName;
                    }
                    if (newName == null || def == null) continue;

                    any = true;
                    if (!uses.TryGetValue(tree, out var list)) uses[tree] = list = new List<Use>();
                    list.Add(new Use { Node = name, NewName = newName });

                    if (made.Add(key))
                    {
                        var defTree = def.SyntaxTree;
                        if (!instances.TryGetValue(defTree, out var il)) instances[defTree] = il = new List<Instance>();
                        il.Add(new Instance
                        {
                            Def = def, Model = compilation.GetSemanticModel(defTree),
                            Params = tparams!, Args = targs!, NewName = newName,
                        });
                    }
                }
            }

            if (!any) return new Result(compilation, user, errors);

            // Build the new tree for every tree that has uses to rewrite or declarations to copy.
            var touched = uses.Keys.Concat(instances.Keys).Distinct().ToList();
            var newUser = new HashSet<SyntaxTree>(user);
            foreach (var tree in touched)
            {
                var text = Rewrite(tree,
                    uses.TryGetValue(tree, out var u) ? u : new List<Use>(),
                    instances.TryGetValue(tree, out var i) ? i : new List<Instance>());
                var fresh = CSharpSyntaxTree.ParseText(text, parse, path: tree.FilePath);
                compilation = compilation.ReplaceSyntaxTree(tree, fresh);
                if (newUser.Remove(tree)) newUser.Add(fresh);
            }
            user = newUser;
        }

        errors.Add(Diagnostic.Create(Descriptors.GenericDepth, Location.None, "(unknown)"));
        return new Result(compilation, user, errors);
    }

    // ------------------------------------------------------------------ analysis helpers

    private static bool InGeneric(SyntaxNode n) =>
        n.Ancestors().Any(a => a is TypeDeclarationSyntax { TypeParameterList: not null } or
            MethodDeclarationSyntax { TypeParameterList: not null });

    private static bool IsSource(ISymbol s) => s.Locations.Any(l => l.IsInSource);

    private static bool Concrete(IEnumerable<ITypeSymbol> args) => args.All(a => !HasTypeParameter(a));

    private static bool HasTypeParameter(ITypeSymbol t) => t switch
    {
        ITypeParameterSymbol => true,
        IPointerTypeSymbol p => HasTypeParameter(p.PointedAtType),
        IArrayTypeSymbol a => HasTypeParameter(a.ElementType),
        INamedTypeSymbol n => n.TypeArguments.Any(HasTypeParameter),
        _ => false,
    };

    private static string Mangle(string name, IEnumerable<ITypeSymbol> args) =>
        name + "__" + string.Join("_", args.Select(MangleType));

    private static string MangleType(ITypeSymbol t) => t switch
    {
        IPointerTypeSymbol p => MangleType(p.PointedAtType) + "Ptr",
        IArrayTypeSymbol a => MangleType(a.ElementType) + "Arr",
        INamedTypeSymbol n when n.SpecialType != SpecialType.None => n.ToDisplayString().Replace('.', '_'),
        INamedTypeSymbol n => (n.ContainingType != null ? MangleType(n.ContainingType) + "_" : "") + n.Name +
                              (n.TypeArguments.Length > 0 ? "__" + string.Join("_", n.TypeArguments.Select(MangleType)) : ""),
        _ => "t",
    };

    // ------------------------------------------------------------------ rewriting

    private static string Rewrite(SyntaxTree tree, List<Use> uses, List<Instance> instances)
    {
        var root = tree.GetRoot();

        // Copies are built from the untouched tree, before any edit.
        var clonesByParent = new Dictionary<SyntaxNode, List<MemberDeclarationSyntax>>();
        foreach (var inst in instances)
        {
            var parent = inst.Def.Parent!;
            if (!clonesByParent.TryGetValue(parent, out var l)) clonesByParent[parent] = l = new List<MemberDeclarationSyntax>();
            l.Add(Clone(inst));
        }
        var restoreLine = clonesByParent.Keys.ToDictionary(p => p, p => CloseLine(p, tree));

        var tracked = root.TrackNodes(uses.Select(u => (SyntaxNode)u.Node).Concat(clonesByParent.Keys));
        var byCurrent = uses.ToDictionary(u => (SyntaxNode)tracked.GetCurrentNode(u.Node)!, u => u.NewName);
        var edited = tracked.ReplaceNodes(byCurrent.Keys,
            (orig, _) => SyntaxFactory.IdentifierName(byCurrent[orig]).WithTriviaFrom(orig));

        foreach (var parent in clonesByParent.Keys)
        {
            var current = edited.GetCurrentNode(parent)!;
            var withClones = AddMembers(current, clonesByParent[parent], restoreLine[parent], tree.FilePath, edited, parent);
            edited = edited.ReplaceNode(current, withClones);
        }
        return edited.ToFullString();
    }

    private static int CloseLine(SyntaxNode parent, SyntaxTree tree)
    {
        var token = parent switch
        {
            BaseTypeDeclarationSyntax t => t.CloseBraceToken,
            BaseNamespaceDeclarationSyntax { } ns when ns is NamespaceDeclarationSyntax b => b.CloseBraceToken,
            _ => ((CompilationUnitSyntax)tree.GetRoot()).EndOfFileToken,
        };
        if (parent is FileScopedNamespaceDeclarationSyntax)
            token = ((CompilationUnitSyntax)tree.GetRoot()).EndOfFileToken;
        return token.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1;
    }

    private static SyntaxNode AddMembers(SyntaxNode parent, List<MemberDeclarationSyntax> clones, int closeLine, string path,
        SyntaxNode root, SyntaxNode original)
    {
        var restore = $"#line {closeLine} \"{path}\"\n";
        switch (parent)
        {
            case ClassDeclarationSyntax c:
                return c.AddMembers(clones.ToArray()).WithCloseBraceToken(WithDirective(c.CloseBraceToken, restore));
            case StructDeclarationSyntax s:
                return s.AddMembers(clones.ToArray()).WithCloseBraceToken(WithDirective(s.CloseBraceToken, restore));
            case NamespaceDeclarationSyntax n:
                return n.AddMembers(clones.ToArray()).WithCloseBraceToken(WithDirective(n.CloseBraceToken, restore));
            case CompilationUnitSyntax cu:
                return cu.AddMembers(clones.ToArray()).WithEndOfFileToken(WithDirective(cu.EndOfFileToken, restore));
            case FileScopedNamespaceDeclarationSyntax fs:
                // The end of file token belongs to the compilation unit; copies go at the end of the namespace.
                return fs.AddMembers(clones.ToArray());
            default:
                throw new InvalidOperationException("unsupported container for generic instantiation: " + parent.Kind());
        }
    }

    private static SyntaxToken WithDirective(SyntaxToken token, string directive)
    {
        var old = token.LeadingTrivia.ToFullString();
        var nl = old.LastIndexOf('\n');
        var head = old.Substring(0, nl + 1);
        var tail = old.Substring(nl + 1);
        return token.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(head + directive + tail));
    }

    private static MemberDeclarationSyntax Clone(Instance inst)
    {
        var def = inst.Def;
        var subst = new Dictionary<ITypeParameterSymbol, string>(SymbolEqualityComparer.Default);
        for (var k = 0; k < inst.Params.Count; k++)
            subst[inst.Params[k]] = inst.Args[k].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var targets = def.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(id => id.Parent is not TypeParameterConstraintClauseSyntax)
            .Where(id => inst.Model.GetSymbolInfo(id).Symbol is ITypeParameterSymbol tp && subst.ContainsKey(tp))
            .ToList();
        var replaced = def.ReplaceNodes(targets, (orig, _) =>
        {
            var tp = (ITypeParameterSymbol)inst.Model.GetSymbolInfo(orig).Symbol!;
            return SyntaxFactory.ParseTypeName(subst[tp]).WithTriviaFrom(orig);
        });

        var startLine = def.GetFirstToken().GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1;
        var path = def.GetFirstToken().GetLocation().GetMappedLineSpan().Path;
        var id = SyntaxFactory.Identifier(inst.NewName);

        MemberDeclarationSyntax result;
        switch (replaced)
        {
            case StructDeclarationSyntax s:
                var renamed = s.WithIdentifier(id.WithTriviaFrom(s.Identifier)).WithTypeParameterList(null)
                    .WithConstraintClauses(default);
                var ctors = renamed.Members.OfType<ConstructorDeclarationSyntax>().ToList();
                renamed = renamed.ReplaceNodes(ctors, (o, _) => o.WithIdentifier(id.WithTriviaFrom(o.Identifier)));
                result = renamed;
                break;
            case MethodDeclarationSyntax m:
                result = m.WithIdentifier(id.WithTriviaFrom(m.Identifier)).WithTypeParameterList(null)
                    .WithConstraintClauses(default);
                break;
            default:
                throw new InvalidOperationException("cannot instantiate " + def.Kind());
        }

        var leading = SyntaxFactory.ParseLeadingTrivia($"#line {startLine} \"{path}\"\n");
        return result.WithLeadingTrivia(leading).WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
    }
}
