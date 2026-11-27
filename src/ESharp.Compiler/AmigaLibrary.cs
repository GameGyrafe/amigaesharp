using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ESharp.Compiler;

/// <summary>
/// An Amiga shared library built from E# (a project with <c>EsOutputType</c> <c>AmigaLibrary</c>, issue #154): its name and version,
/// from the project (<c>esharp build</c> reads them from the csproj).
/// </summary>
public sealed class AmigaLibraryOptions
{
    /// <summary>The file and node name, for example <c>amigalib.library</c>.</summary>
    public string Name { get; init; } = "";

    public int Version { get; init; } = 1;
    public int Revision { get; init; }

    /// <summary>The date of the id and $VER strings, dd.mm.yyyy.</summary>
    public string Date { get; init; } = "";

    /// <summary>The C# struct of the binding; default <c>&lt;Stem&gt;Library</c> (amigalib.library: AmigalibLibrary).</summary>
    public string? BindingName { get; init; }

    /// <summary>The name without <c>.library</c>.</summary>
    public string Stem => Name.EndsWith(".library", StringComparison.OrdinalIgnoreCase) ? Name[..^".library".Length] : Name;

    /// <summary>The stem as a C# identifier with a capital first letter (amigalib: Amigalib; my-lib: My_lib).</summary>
    public string Pascal
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var ch in Stem) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            sb[0] = char.ToUpperInvariant(sb[0]);
            return sb.ToString();
        }
    }

    /// <summary>The base name in the <c>.fd</c> file (without its underscore): AmigalibBase.</summary>
    public string BaseName => Pascal + "Base";

    public string Binding => string.IsNullOrEmpty(BindingName) ? Pascal + "Library" : BindingName!;

    /// <summary>The library's id string (lib_IdString and the RomTag's): <c>amigalib.library 1.2 (25.11.2026)</c>.</summary>
    public string IdString => $"{Name} {Version}.{Revision}" + (Date.Length > 0 ? $" ({Date})" : "");

    /// <summary>Why the name or version cannot be used, or null.</summary>
    public string? Problem()
    {
        if (!Name.EndsWith(".library", StringComparison.Ordinal) || Stem.Length == 0) return $"the library name '{Name}' must end in .library";
        if (Name.Any(c => c < 33 || c > 126 || c is '/' or ':' or '"' or '\\')) return $"the library name '{Name}' may hold only printable ASCII without spaces, '/', ':', '\\' and '\"'";
        if (Version is < 1 or > 255) return $"the library version {Version} must be 1 to 255 (the RomTag holds it in a byte)";
        if (Revision is < 0 or > 65535) return $"the library revision {Revision} must be 0 to 65535";
        if (Date.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(Date, @"^\d{1,2}\.\d{1,2}\.\d{4}$")) return $"the library date '{Date}' must be dd.mm.yyyy";
        if (!SyntaxFacts.IsValidIdentifier(Binding)) return $"the binding name '{Binding}' is not a C# identifier";
        return null;
    }
}

/// <summary>One argument of a library function: its C# name and type, and the register it travels in.</summary>
public sealed record LibraryArgument(string Name, string Type, string Register);

/// <summary>A function of the library: its name, vector offset (negative: -30, -36, ...), arguments and result type (C# text).</summary>
public sealed record LibraryFunctionInfo(string Name, int Offset, IReadOnlyList<LibraryArgument> Arguments, string ReturnType, string Method);

/// <summary>What a library build gives besides its C: the <c>.fd</c> file, the C# binding and the function table.</summary>
public sealed class AmigaLibraryOutput
{
    public AmigaLibraryOutput(string fd, string binding, IReadOnlyList<LibraryFunctionInfo> functions)
    {
        Fd = fd;
        Binding = binding;
        Functions = functions;
    }

    public string Fd { get; }

    /// <summary>The C# source an E# program compiles with to open the library and call it (see <see cref="AmigaLibraries.Binding"/>).</summary>
    public string Binding { get; }

    public IReadOnlyList<LibraryFunctionInfo> Functions { get; }
}

/// <summary>The functions of a library build, in vector order, with the registers of their arguments.</summary>
internal sealed class AmigaLibraryPlan
{
    public AmigaLibraryPlan(AmigaLibraryOptions options, IReadOnlyList<(IMethodSymbol Method, IReadOnlyList<string> Registers, int Offset)> functions)
    {
        Options = options;
        Functions = functions;
    }

    public AmigaLibraryOptions Options { get; }
    public IReadOnlyList<(IMethodSymbol Method, IReadOnlyList<string> Registers, int Offset)> Functions { get; }
}

/// <summary>
/// The rules, register assignment, <c>.fd</c> file and C# binding of an E# <c>.library</c> (issue #154). The library's C (per-opener data,
/// RomTag, open/close/expunge, register stubs) is the emitter's (<c>CEmitter.AmigaLibrary.cs</c>).
/// </summary>
public static class AmigaLibraries
{
    /// <summary>The binding struct's own members, which a library function may not be named.</summary>
    public static readonly IReadOnlyList<string> BindingMembers = new[]
    {
        "Base", "IsOpen", "Open", "Close", "OpenCount", "FlushUnused", "ExpungeWhenClosed", "LibraryName", "LibraryVersion", "LibraryRevision", "Types", "Equals", "GetHashCode", "ToString", "GetType",
    };

    /// <summary>The first function's vector offset: after Open, Close, Expunge and the reserved vector.</summary>
    public const int FirstOffset = -30;

    /// <summary>
    /// The registers of a function's arguments, in order: data (integers, bool) in d0-d7, pointers (and strings) in a0-a3. When one kind runs
    /// out, its arguments take the other kind's free registers (a pointer in d4, a number in a2), so 12 arguments fit. a4 and a5 are not used:
    /// a4 holds the opener's data inside the library and a5 may be the caller's frame pointer. Null when there are more than 12.
    /// </summary>
    public static IReadOnlyList<string>? AssignRegisters(IReadOnlyList<bool> pointers)
    {
        int numbers = pointers.Count(p => !p), addresses = pointers.Count(p => p);
        if (numbers + addresses > 12) return null;
        // The registers the other kind leaves free, lowest first.
        var spareData = new Queue<string>(Enumerable.Range(Math.Min(numbers, 8), 8 - Math.Min(numbers, 8)).Select(i => "d" + i));
        var spareAddress = new Queue<string>(Enumerable.Range(Math.Min(addresses, 4), 4 - Math.Min(addresses, 4)).Select(i => "a" + i));
        int d = 0, a = 0;
        var result = new string[pointers.Count];
        for (var i = 0; i < pointers.Count; i++)
            result[i] = pointers[i] ? (a < 4 ? "a" + a++ : spareData.Dequeue()) : (d < 8 ? "d" + d++ : spareAddress.Dequeue());
        return result;
    }

    /// <summary>
    /// Why a .library cannot be built for the target these symbols define, or null: m68k (AmigaOS 3.x), AROS (x86-64 and i386) and AmigaOS 4 build one,
    /// and so does a build for no target (the host tests compile its C).
    /// </summary>
    public static string? TargetProblem(IReadOnlyCollection<string> defines)
    {
        if (defines.Contains("SDL"))
            return "sdl has no shared libraries; a program that names the library's folder in esharp.include compiles the library's code in on sdl";
        if (defines.Contains("MORPHOS"))
            return "a MorphOS .library is not done yet (m68k, AROS x86-64, AROS i386 and AmigaOS 4 are)";
        return null;
    }

    /// <summary>True for a type that travels in an address register (a pointer or a string).</summary>
    internal static bool IsPointerLike(ITypeSymbol t) => t is IPointerTypeSymbol || t.SpecialType == SpecialType.System_String;

    private static bool IsRegisterNumber(ITypeSymbol t) => t.SpecialType is SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte
        or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_Char;

    private static bool IsMemoryNumber(ITypeSymbol t) => IsRegisterNumber(t) || t.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64;

    /// <summary>The library's [LibraryFunction] methods, checked, in vector order (by file path, then position); null when there are errors.</summary>
    internal static AmigaLibraryPlan? Plan(CSharpCompilation compilation, IReadOnlyCollection<SyntaxTree> userTrees, AmigaLibraryOptions options, List<Diagnostic> diagnostics)
    {
        var before = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        if (options.Problem() is { } problem)
            diagnostics.Add(Diagnostic.Create(Descriptors.LibraryUnsupported, Location.None, "this name or version", problem));
        var user = new HashSet<SyntaxTree>(userTrees);
        var methods = new List<IMethodSymbol>();
        foreach (var tree in userTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not IMethodSymbol m) continue;
                if (HasAttribute(m, "EntryPointAttribute"))
                    diagnostics.Add(Diagnostic.Create(Descriptors.LibraryUnsupported, decl.Identifier.GetLocation(), "an entry point ([EntryPoint])",
                        "a .library has no Main; its functions are the [LibraryFunction] methods, and an E# program opens it"));
                if (HasAttribute(m, "LibraryFunctionAttribute")) methods.Add(m);
            }
        }
        if (methods.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(Descriptors.LibraryNoFunctions, Location.None));
            return null;
        }

        var byName = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        var functions = new List<(IMethodSymbol, IReadOnlyList<string>, int)>();
        foreach (var m in methods)
        {
            var at = m.Locations[0];
            var display = m.ContainingType.Name + "." + m.Name;
            if (!m.IsStatic || m.DeclaredAccessibility != Accessibility.Public)
                diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionForm, at, display, "it must be public static (the binding calls it by name on the host and on sdl)"));
            if (m.IsGenericMethod || m.ContainingType.IsGenericType)
                diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionForm, at, display, "a generic method or a method of a generic type has no single C function to export"));
            if (m.IsExtern || m.MethodKind != MethodKind.Ordinary)
                diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionForm, at, display, "only an ordinary method with a body can be exported"));
            if (!m.ReturnsVoid) CheckType(m.ReturnType, "the result", display, at, user, diagnostics);
            foreach (var p in m.Parameters)
            {
                if (p.RefKind != RefKind.None)
                    diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionType, at, display, $"parameter '{p.Name}'", p.RefKind.ToString().ToLowerInvariant() + " " + p.Type.ToDisplayString()));
                else if (p.IsParams)
                    diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionType, at, display, $"parameter '{p.Name}'", "params " + p.Type.ToDisplayString()));
                else CheckType(p.Type, $"parameter '{p.Name}'", display, at, user, diagnostics);
            }
            if (BindingMembers.Contains(m.Name))
                diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionForm, at, display, $"'{m.Name}' is a member of the binding struct already ({string.Join(", ", BindingMembers)}); rename the method"));
            if (byName.TryGetValue(m.Name, out var other))
                diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionName, at, other.ContainingType.Name + "." + other.Name, display, m.Name));
            else byName[m.Name] = m;
            var registers = AssignRegisters(m.Parameters.Select(p => IsPointerLike(p.Type)).ToList());
            if (registers == null)
            {
                diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionRegisters, at, display, m.Parameters.Length));
                continue;
            }
            functions.Add((m, registers, FirstOffset - 6 * functions.Count));
        }
        return diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) > before ? null : new AmigaLibraryPlan(options, functions);
    }

    private static void CheckType(ITypeSymbol t, string what, string method, Location at, HashSet<SyntaxTree> user, List<Diagnostic> diagnostics)
    {
        if (IsRegisterNumber(t) || t.SpecialType == SpecialType.System_String) return;
        if (t is IPointerTypeSymbol p)
        {
            var problem = PointeeProblem(p.PointedAtType, user, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
            if (problem != null) diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionForm, at, method, $"{what} ({t.ToDisplayString()}) {problem}"));
            return;
        }
        diagnostics.Add(Diagnostic.Create(Descriptors.LibraryFunctionType, at, method, what, t.ToDisplayString()));
    }

    /// <summary>Why a pointer to <paramref name="t"/> cannot cross the library boundary, or null.</summary>
    private static string? PointeeProblem(ITypeSymbol t, HashSet<SyntaxTree> user, HashSet<ITypeSymbol> seen)
    {
        if (t.SpecialType == SpecialType.System_Void || IsMemoryNumber(t)) return null;
        if (t is IPointerTypeSymbol inner) return PointeeProblem(inner.PointedAtType, user, seen);
        if (t is not INamedTypeSymbol { TypeKind: TypeKind.Struct } s || s.IsGenericType)
            return $"points to '{t.ToDisplayString()}'; a pointer may point to numbers, bool, void, pointers and structs only";
        if (!s.DeclaringSyntaxReferences.Any(r => user.Contains(r.SyntaxTree))) return null;   // a platform struct: the program has it too
        if (!seen.Add(s)) return null;
        foreach (var member in s.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: true }:
                    return $"points to the struct '{s.Name}', which has an auto-property; a struct that crosses the library boundary holds only fields (the binding copies them for the program)";
                case IFieldSymbol { IsStatic: false } f when f.IsFixedSizeBuffer:
                    return $"points to the struct '{s.Name}', whose field '{f.Name}' is a fixed buffer; use separate fields or a pointer";
                case IFieldSymbol { IsStatic: false } f:
                    if (IsMemoryNumber(f.Type) || f.Type is IPointerTypeSymbol && PointeeProblem(((IPointerTypeSymbol)f.Type).PointedAtType, user, seen) == null) break;
                    if (f.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } && PointeeProblem(f.Type, user, seen) is null) break;
                    return $"points to the struct '{s.Name}', whose field '{f.Name}' has type '{f.Type.ToDisplayString()}'; a struct that crosses the library boundary holds only numbers, bool, pointers and such structs";
            }
        }
        return null;
    }

    /// <summary>The library's structs (declared in its own sources) that its functions point to, with the structs they hold, in a stable order.</summary>
    private static List<INamedTypeSymbol> BoundaryStructs(AmigaLibraryPlan plan, HashSet<SyntaxTree> user)
    {
        var result = new List<INamedTypeSymbol>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        void Visit(ITypeSymbol t)
        {
            while (t is IPointerTypeSymbol p) t = p.PointedAtType;
            if (t is not INamedTypeSymbol { TypeKind: TypeKind.Struct } s || !s.DeclaringSyntaxReferences.Any(r => user.Contains(r.SyntaxTree)) || !seen.Add(s)) return;
            foreach (var f in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic)) Visit(f.Type);
            result.Add(s);   // after the structs it holds
        }
        foreach (var (m, _, _) in plan.Functions)
        {
            Visit(m.ReturnType);
            foreach (var p in m.Parameters) Visit(p.Type);
        }
        return result;
    }

    /// <summary>The <c>.fd</c> file: base name, bias 30, each function with its argument names and registers.</summary>
    public static string Fd(AmigaLibraryOptions options, IReadOnlyList<LibraryFunctionInfo> functions)
    {
        var sb = new StringBuilder();
        sb.Append($"* \"{options.Name}\" {options.Version}.{options.Revision}: generated by esharp from the library's E# sources\n");
        sb.Append($"##base _{options.BaseName}\n");
        sb.Append($"##bias {-FirstOffset}\n");
        sb.Append("##public\n");
        foreach (var f in functions)
            sb.Append($"{f.Name}({string.Join(",", f.Arguments.Select(a => a.Name))})({string.Join(",", f.Arguments.Select(a => a.Register))})\n");
        sb.Append("##end\n");
        return sb.ToString();
    }

    internal static IReadOnlyList<LibraryFunctionInfo> Infos(AmigaLibraryPlan plan) =>
        plan.Functions.Select(f => new LibraryFunctionInfo(f.Method.Name, f.Offset,
            f.Method.Parameters.Select((p, i) => new LibraryArgument(p.Name, TypeText(p.Type), f.Registers[i])).ToList(),
            f.Method.ReturnsVoid ? "void" : TypeText(f.Method.ReturnType), Qualified(f.Method.ContainingType) + "." + f.Method.Name)).ToList();

    private static string TypeText(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Qualified(INamedTypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Ident(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static bool HasAttribute(ISymbol s, string name) => s.GetAttributes().Any(a => a.AttributeClass?.Name == name);

    /// <summary>
    /// The C# binding (one file): a struct named <see cref="AmigaLibraryOptions.Binding"/> in the namespace of the first library function's class,
    /// holding one opener's library base, with Open, Close, OpenCount and one method per library function. On the Amiga targets the methods call
    /// the vectors ([LibraryCall], with this opener's base); on the host and sdl they call the library's own C# methods, compiled into the program
    /// (the host keeps each opener's statics apart, see <c>SharedLibrary</c>). The library's structs that its functions point to are copied
    /// (fields only) for the Amiga build of the program, which does not compile the library's sources.
    /// </summary>
    internal static string Binding(AmigaLibraryPlan plan, IReadOnlyCollection<SyntaxTree> userTrees)
    {
        var o = plan.Options;
        var user = new HashSet<SyntaxTree>(userTrees);
        var first = plan.Functions[0].Method.ContainingType;
        var ns = first.ContainingNamespace.IsGlobalNamespace ? null : first.ContainingNamespace.ToDisplayString();
        var b = o.Binding;
        var vectors = b + "Vectors";
        var sb = new StringBuilder();
        void L(string s = "") => sb.Append(s).Append('\n');
        L("// <auto-generated/>");
        L($"// The E# binding of {o.Name} {o.Version}.{o.Revision}, generated by esharp from the library's sources (EsOutputType AmigaLibrary). Do not edit.");
        L("using ESharp;");
        L("using ESharp.Platform;");
        L();

        // Structs that cross the boundary: the Amiga build of the program does not compile the library, so it gets their fields here.
        var structs = BoundaryStructs(plan, user);
        if (structs.Count > 0)
        {
            L("#if AMIGA");
            foreach (var group in structs.GroupBy(s => s.ContainingNamespace.IsGlobalNamespace ? "" : s.ContainingNamespace.ToDisplayString()))
            {
                if (group.Key.Length > 0) { L($"namespace {group.Key}"); L("{"); }
                foreach (var s in group)
                {
                    if (s.ContainingType != null)
                    {
                        L($"    // {s.ToDisplayString()} is nested; the copy is a top-level struct of the same name");
                    }
                    L($"    /// <summary>The fields of the library's {s.Name} (the same layout as in the library).</summary>");
                    L($"    public unsafe struct {s.Name}");
                    L("    {");
                    foreach (var f in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
                        L($"        public {TypeText(f.Type)} {Ident(f.Name)};");
                    L("    }");
                }
                if (group.Key.Length > 0) L("}");
            }
            L("#endif");
            L();
        }

        if (ns != null) { L($"namespace {ns}"); L("{"); }
        var types = userTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>())
            .Where(d => d.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax)
            .Select(d => (d.SyntaxTree, d))
            .ToList();
        var typeNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, d) in types)
        {
            var nsName = d.Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString());
            var full = string.Join(".", nsName.Append(d.Identifier.Text));
            if (d is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax { TypeParameterList: not null }) continue;   // generic: no statics of its own to keep apart
            typeNames.Add(full);
        }
        L($"    /// <summary>");
        L($"    /// One opener of {o.Name} (version {o.Version}.{o.Revision}): <c>Open</c> gives this opener's library base, and each opener has its own copy");
        L($"    /// of the library's data. The methods call the library's functions through this opener's base.");
        L($"    /// </summary>");
        L($"    [LibraryBinding(\"{o.Name}\", \"{string.Join(";", typeNames)}\")]   // sdl: these types' static data is kept per opener");
        L($"    public unsafe struct {b}");
        L("    {");
        L($"        public const string LibraryName = \"{o.Name}\";");
        L($"        public const int LibraryVersion = {o.Version};");
        L($"        public const int LibraryRevision = {o.Revision};");
        L($"        private const string Types = \"{string.Join(";", typeNames)}\";   // the host keeps their static fields apart per opener (sdl: [LibraryBinding])");
        L();
        L("        /// <summary>This opener's library base, or null when it is not open.</summary>");
        L("        public void* Base;");
        L();
        L("        public bool IsOpen => Base != null;");
        L();
        L("        /// <summary>Opens the library (exec OpenLibrary on the Amiga): an opener whose Base is null when it is missing or older than <paramref name=\"version\"/>.</summary>");
        L($"        public static {b} Open(int version)");
        L("        {");
        L($"            {b} library;");
        L("            library.Base = SharedLibrary.Open(LibraryName, version, LibraryVersion, Types);");
        L("            return library;");
        L("        }");
        L();
        L("        /// <summary>Closes this opener (its copy of the library's data goes too).</summary>");
        L("        public void Close()");
        L("        {");
        L("            SharedLibrary.Close(Base);");
        L("            Base = null;");
        L("        }");
        L();
        L("        /// <summary>How many openers the library has now, or -1 when it is not in memory.</summary>");
        L("        public static int OpenCount() { return SharedLibrary.OpenCount(LibraryName); }");
        L();
        L("        /// <summary>Asks exec to expunge the libraries nobody has open (this one too, when it has no openers).</summary>");
        L("        public static void FlushUnused() { SharedLibrary.FlushUnused(); }");
        L();
        L("        /// <summary>While the library is open: it goes when its last opener closes (exec RemLibrary). False when it is not open.</summary>");
        L("        public static bool ExpungeWhenClosed() { return SharedLibrary.ExpungeWhenClosed(LibraryName); }");
        foreach (var (m, regs, offset) in plan.Functions)
        {
            var ret = m.ReturnsVoid ? "void" : TypeText(m.ReturnType);
            var pars = string.Join(", ", m.Parameters.Select(p => $"{TypeText(p.Type)} {Ident(p.Name)}"));
            var args = string.Join(", ", m.Parameters.Select(p => Ident(p.Name)));
            L();
            L($"        /// <summary>{m.ContainingType.Name}.{m.Name} of the library (vector {offset}" + (regs.Count > 0 ? "; " + string.Join(", ", m.Parameters.Select((p, i) => $"{p.Name} in {regs[i]}")) : "") + ").</summary>");
            L($"        public {ret} {Ident(m.Name)}({pars})");
            L("        {");
            L("#if AMIGA");
            L($"            {(m.ReturnsVoid ? "" : "return ")}{vectors}.{Ident(m.Name)}(Base{(args.Length > 0 ? ", " + args : "")});");
            L("#else");
            L("            SharedLibrary.Enter(Base);");
            if (m.ReturnsVoid)
            {
                L($"            {Qualified(m.ContainingType)}.{Ident(m.Name)}({args});");
                L("            SharedLibrary.Leave(Base);");
            }
            else
            {
                L($"            {ret} es_result = {Qualified(m.ContainingType)}.{Ident(m.Name)}({args});");
                L("            SharedLibrary.Leave(Base);");
                L("            return es_result;");
            }
            L("#endif");
            L("        }");
        }
        L("    }");
        L();
        L("#if AMIGA");
        L($"    /// <summary>The vectors of {o.Name}; the first argument is the opener's library base.</summary>");
        L($"    internal static unsafe class {vectors}");
        L("    {");
        foreach (var (m, regs, offset) in plan.Functions)
        {
            var ret = m.ReturnsVoid ? "void" : TypeText(m.ReturnType);
            var pars = string.Join("", m.Parameters.Select(p => $", {TypeText(p.Type)} {Ident(p.Name)}"));
            L($"        [LibraryCall({offset.ToString(CultureInfo.InvariantCulture)}, \"{string.Join(",", regs)}\")]");
            L($"        public static extern {ret} {Ident(m.Name)}(void* libraryBase{pars});");
        }
        L("    }");
        L("#endif");
        if (ns != null) L("}");
        return sb.ToString();
    }
}
