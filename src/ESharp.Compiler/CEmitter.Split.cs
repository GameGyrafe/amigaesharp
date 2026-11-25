using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ESharp.Compiler;

/// <summary>
/// Which code a C file defines when a program and its E# library packages are built as separate objects (issue #154, precompiled
/// package binaries). Without a plan (<see cref="Active"/> false) the C file is the whole program, as always.
/// <list type="bullet">
/// <item>Library mode (<see cref="LibraryPrefix"/>): the C of one package for <c>library.a</c>. It defines the package's functions,
/// static fields, class tables and field initializers (and the generic copies the package itself makes), with external linkage,
/// and an init function (<see cref="InitName"/>) that runs its static initializers and static constructors. Everything else
/// (the platform layer, other packages, the E# runtime's state) is declared only: the program defines it.</item>
/// <item>Program mode (<see cref="Binaries"/>): the program's C when some packages are linked from their binaries. Their code is
/// declared, not defined, except generic copies over types the binary does not have, which the program emits itself (the binary
/// lists the C names of the copies it holds). main calls each binary's init function after the platform's initializers.</item>
/// </list>
/// Both sides compute the same reachable set for the package's code (all of it is a root, as program code is), so whatever the
/// binary declares the program defines.
/// </summary>
internal sealed class SplitPlan
{
    /// <summary>Library mode: the path prefix of the package's trees (<see cref="EsPackage.PathPrefix"/>).</summary>
    public string? LibraryPrefix { get; init; }

    /// <summary>Library mode: the name of the init function.</summary>
    public string InitName { get; init; } = "";

    /// <summary>Program mode: the binaries linked, by the path prefix of their package's trees.</summary>
    public IReadOnlyDictionary<string, PackageBinary> Binaries { get; init; } = new Dictionary<string, PackageBinary>();

    /// <summary>Program mode: the binaries in the order main calls their init functions.</summary>
    public IReadOnlyList<PackageBinary> InitOrder { get; init; } = Array.Empty<PackageBinary>();

    public IReadOnlyCollection<string> MadeTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> MadeMethods { get; init; } = Array.Empty<string>();

    public bool Library => LibraryPrefix != null;
    public bool Active => Library || Binaries.Count > 0;
}

internal sealed partial class CEmitter
{
    /// <summary>Separate objects for the program and precompiled packages (see <see cref="SplitPlan"/>).</summary>
    public SplitPlan Split { get; init; } = new();

    /// <summary>Library mode, after <see cref="Emit"/>: what the binary's manifest records.</summary>
    public LibraryBuildInfo? LibraryInfo { get; private set; }

    private static string? PathOf(ISymbol s) =>
        s.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath;

    /// <summary>The C name of the generic copy that <paramref name="s"/> belongs to (the innermost one), or null.</summary>
    private string? InstanceKey(ISymbol s)
    {
        for (var c = s.OriginalDefinition; c != null && c is not INamespaceSymbol; c = c.ContainingSymbol)
        {
            if (c is IMethodSymbol { MethodKind: MethodKind.Ordinary } m && IsCopy(m)) return FuncName(m);
            if (c is INamedTypeSymbol t && IsCopy(t)) return TypeName(t);
        }
        return null;
    }

    /// <summary>A generic copy the monomorphiser made (matched by its qualified name, not its short one).</summary>
    private bool IsCopy(ISymbol s) =>
        (s is INamedTypeSymbol ? Split.MadeTypes : Split.MadeMethods).Contains(Monomorphizer.CopyName(s.OriginalDefinition.ContainingSymbol, s.Name));

    private PackageBinary? BinaryOf(string? path)
    {
        if (path == null) return null;
        foreach (var (prefix, binary) in Split.Binaries)
            if (path.StartsWith(prefix, StringComparison.Ordinal)) return binary;
        return null;
    }

    /// <summary>
    /// True when <paramref name="s"/> (a function, static field or property, event, or the class whose table and field initializer are
    /// meant) is defined in another object of the link: this C file only declares it.
    /// </summary>
    private bool IsExternal(ISymbol s)
    {
        if (!Split.Active) return false;
        var path = PathOf(s);
        if (Split.Library) return path == null || !path.StartsWith(Split.LibraryPrefix!, StringComparison.Ordinal);
        if (BinaryOf(path) is not { } binary) return false;
        var key = InstanceKey(s);
        return key == null || binary.Generics.Contains(key);
    }

    /// <summary>A class whose table a precompiled binary defines (or, in library mode, this one): every slot's implementation is reached.</summary>
    private bool IsSplitOwnedClass(INamedTypeSymbol c)
    {
        if (!Split.Active) return false;
        var path = PathOf(c);
        return Split.Library ? path != null && path.StartsWith(Split.LibraryPrefix!, StringComparison.Ordinal) : BinaryOf(path) != null;
    }

    /// <summary>The storage class of a definition this file makes: <c>static</c> in a whole-program C file, none (external linkage) in a split one.</summary>
    private string Storage => Split.Active ? "" : "static ";

    /// <summary>Runtime pieces the package's own code needs from the program (library mode records them, program mode turns them on).</summary>
    private List<string> RuntimeUses()
    {
        var uses = new List<string>();
        if (_usesAlloc || _usesClosures) uses.Add("alloc");
        if (_usesClosures) uses.Add("closures");
        if (_usesFmt) uses.Add("fmt");
        if (UsesArgs) uses.Add("args");
        return uses;
    }

    private void UseRuntime(IEnumerable<string> uses)
    {
        foreach (var u in uses)
            switch (u)
            {
                case "alloc": _usesAlloc = true; break;
                case "closures": _usesClosures = true; break;
                case "fmt": _usesFmt = true; break;
                case "args": _natives.Add("es_args_count"); break;
            }
    }

    private readonly SortedDictionary<string, string> _baseLibs = new(StringComparer.Ordinal);   // base name -> library, for the manifest

    /// <summary>
    /// A variable of the E# runtime's state (the counters of Heap.LiveClosures, the exit hook of a fatal error, the text ring of string
    /// formatting, the command line of Args): static in a whole-program C file; in a split build it lives once, in the program (external
    /// linkage there, declared extern in a package's C), so the program and every binary share it.
    /// </summary>
    private string RuntimeState(string declaration) => !Split.Active ? "static " + declaration : Split.Library ? "extern " + declaration : declaration;

    /// <summary>A runtime helper without state that every whole-program C file calls: static in each C file, marked unused in a split one (a file may not call it).</summary>
    private string SplitHelper => Split.Active ? "static __attribute__((unused)) " : "static ";

    /// <summary>Library mode: the init function (static initializers that are not C constants, then static constructors).</summary>
    private void EmitLibraryInit()
    {
        Line($"void {Split.InitName}(void)");
        Line("{");
        _indent++;
        EmitDeferredStatics();
        foreach (var cctor in _staticConstructors) Line(FuncName(cctor) + "();");
        _indent--;
        Line("}");
        Line("");
    }

    /// <summary>Library mode: the C names of the generic copies this package's binary holds, for its manifest.</summary>
    private List<string> LibraryInstances(IEnumerable<INamedTypeSymbol> types)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in types)
        {
            if (IsExternal(t)) continue;
            if (IsCopy(t)) names.Add(TypeName(t));
            foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary && !m.IsGenericMethod && IsCopy(m)) names.Add(FuncName(m));
        }
        return names.ToList();
    }
}
