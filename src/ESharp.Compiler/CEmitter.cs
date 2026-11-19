using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ESharp.Compiler;

/// <summary>
/// Lowers the E# subset to readable, stable C. Output is deterministic (declaration order),
/// and every function and statement carries a #line directive back to the .cs file.
/// </summary>
internal sealed partial class CEmitter
{
    private readonly CSharpCompilation _compilation;
    private readonly HashSet<SyntaxTree> _user;
    private readonly List<Diagnostic> _diagnostics;
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = new();

    private readonly StringBuilder _out = new();
    private int _indent;
    private string? _lastLineFile;
    private int _lastLine = -1;
    private readonly SortedSet<string> _headers = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _spans = new(StringComparer.Ordinal);
    private bool _usesLen;
    private readonly SortedDictionary<string, string> _bases = new(StringComparer.Ordinal);   // base name -> C type, for [LibraryBase] classes
    private bool _usesStrlen;
    private bool _usesStreq;

    /// <summary>How <c>this</c> is spelled inside the method being emitted.</summary>
    private enum ThisKind { None, Pointer, Value }
    private ThisKind _this = ThisKind.None;
    private const string ThisName = "_this";
    private int _temp;

    private readonly bool _bounds;
    private bool _usesBounds;

    public CEmitter(CSharpCompilation compilation, HashSet<SyntaxTree> user, List<Diagnostic> diagnostics, bool boundsChecks = false)
    {
        _bounds = boundsChecks;
        _compilation = compilation;
        _user = user;
        _diagnostics = diagnostics;
    }

    // ------------------------------------------------------------------ entry point lookup

    public static IEnumerable<IMethodSymbol> FindEntryPoints(CSharpCompilation compilation, HashSet<SyntaxTree> user)
    {
        foreach (var tree in compilation.SyntaxTrees.Where(user.Contains))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var m in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(m) is IMethodSymbol sym && HasAttribute(sym, "EntryPointAttribute"))
                    yield return sym;
            }
        }
    }

    /// <summary>The entry point takes <c>string[] args</c>: C has no array parameter, so the function declares its own array from the command line.</summary>
    public static bool IsArgsEntry(IMethodSymbol m) =>
        HasAttribute(m, "EntryPointAttribute") && m.Parameters.Length == 1 && m.Parameters[0] is { RefKind: RefKind.None, Type: IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String, Rank: 1 } };

    /// <summary>A non-static class: a heap object that variables hold by pointer.</summary>
    private static bool IsHeapClass(ITypeSymbol t) => t is INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false };

    /// <summary>An interface declared in E# source: a value of this type is a fat pointer (object + table of functions).</summary>
    private static bool IsInterfaceType(ITypeSymbol? t) =>
        t is INamedTypeSymbol { TypeKind: TypeKind.Interface, IsGenericType: false } i && i.Locations.Any(l => l.IsInSource);

    private static string SlotName(IMethodSymbol im)
    {
        var overloads = im.ContainingType.GetMembers(im.Name).OfType<IMethodSymbol>().Count();
        return overloads > 1 ? im.Name + "_" + string.Join("_", im.Parameters.Select(p => Mangle(p.Type))) : im.Name;
    }

    private static string VtableName(INamedTypeSymbol iface, INamedTypeSymbol cls) => $"{TypeName(iface)}_vt_{QualifiedName(cls)}";

    // ---- class hierarchies
    // A derived class holds its base class object as its first member ('es_base'), so a pointer to the derived object is also a pointer to
    // the base object. The root of a hierarchy that has virtual members or subclasses starts with 'vt', a pointer to the class's table of
    // virtual functions; each class has one const table (struct X_cvt, whose first member is the base class's table, so a derived table can be
    // read as a base table) and a type id (the table's address) that is, as, and is-patterns walk down the base chain.

    private readonly HashSet<INamedTypeSymbol> _polyRoots = new(SymbolEqualityComparer.Default);

    private static INamedTypeSymbol? BaseClass(INamedTypeSymbol c) =>
        c.BaseType is { SpecialType: not SpecialType.System_Object } b && IsHeapClass(b) ? b : null;

    private static INamedTypeSymbol RootClass(INamedTypeSymbol c)
    {
        while (BaseClass(c) is { } b) c = b;
        return c;
    }

    private static int Depth(INamedTypeSymbol c)
    {
        var n = 0;
        while (BaseClass(c) is { } b) { c = b; n++; }
        return n;
    }

    private static bool IsDynamic(IMethodSymbol m) => m.IsVirtual || m.IsAbstract || m.IsOverride;

    /// <summary>Classes whose objects carry a virtual function table: every class of a hierarchy that has a subclass or a virtual member.</summary>
    private bool IsPoly(ITypeSymbol? t) => t is INamedTypeSymbol c && IsHeapClass(c) && _polyRoots.Contains(RootClass(c));

    /// <summary>Part of a hierarchy: needs pointer casts when it is used as its base class.</summary>
    private bool IsHier(ITypeSymbol? t) => t is INamedTypeSymbol c && IsHeapClass(c) && (BaseClass(c) != null || _polyRoots.Contains(c));

    private static bool IsBaseOrSame(INamedTypeSymbol derived, INamedTypeSymbol target)
    {
        for (INamedTypeSymbol? c = derived; c != null; c = BaseClass(c))
            if (SymbolEqualityComparer.Default.Equals(c, target)) return true;
        return false;
    }

    /// <summary>Member path from an object of class <paramref name="from"/> to a member declared in its base class <paramref name="declaring"/>: es_base.es_base.name.</summary>
    private static string MemberPath(ITypeSymbol? from, INamedTypeSymbol? declaring, string name)
    {
        if (from is not INamedTypeSymbol c || declaring == null || !IsHeapClass(c) || !IsBaseOrSame(c, declaring)) return name;
        var sb = new StringBuilder();
        for (var t = c; t != null && !SymbolEqualityComparer.Default.Equals(t, declaring); t = BaseClass(t)) sb.Append("es_base.");
        return sb + name;
    }

    private static string VtPath(INamedTypeSymbol c) => MemberPath(c, RootClass(c), "vt");

    private static string CvtName(INamedTypeSymbol c) => TypeName(c) + "_cvt";

    private static string TypeIdOf(INamedTypeSymbol c) => $"((const struct ES_Type *)&{TypeName(c)}_vt)";

    private static IMethodSymbol RootDef(IMethodSymbol m)
    {
        while (m.OverriddenMethod != null) m = m.OverriddenMethod;
        return m;
    }

    private static string VSlot(IMethodSymbol def) => $"v_{QualifiedName(def.ContainingType)}_{SlotName(def)}";

    /// <summary>The virtual members a class introduces (not the ones it overrides), in declaration order, accessors of virtual properties included.</summary>
    private static List<IMethodSymbol> OwnSlots(INamedTypeSymbol c) =>
        c.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet && !m.IsGenericMethod
                        && (m.IsVirtual || m.IsAbstract) && !m.IsOverride).ToList();

    private static IEnumerable<IMethodSymbol> AllSlots(INamedTypeSymbol c)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? t = c; t != null; t = BaseClass(t)) chain.Insert(0, t);
        return chain.SelectMany(OwnSlots);
    }

    /// <summary>The method that runs when a virtual member is called on an object of class <paramref name="c"/>: the most derived override, or null if abstract.</summary>
    private static IMethodSymbol? FinalImpl(INamedTypeSymbol c, IMethodSymbol def)
    {
        for (INamedTypeSymbol? t = c; t != null; t = BaseClass(t))
        {
            foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
                if (m.IsOverride && SymbolEqualityComparer.Default.Equals(RootDef(m), def)) return m;
            if (SymbolEqualityComparer.Default.Equals(t, def.ContainingType)) break;
        }
        return def.IsAbstract ? null : def;
    }

    /// <summary>A C pointer at run time: raw pointers and class references.</summary>
    private static bool IsRef(ITypeSymbol? t) => t is IPointerTypeSymbol || t != null && IsHeapClass(t);

    private static AttributeData? GetAttribute(ISymbol s, string name) =>
        s.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == name);

    private static bool HasAttribute(ISymbol s, string name) => GetAttribute(s, name) != null;

    /// <summary>Records the library base of a [LibraryBase] class (and its proto header); returns the C variable, or null.</summary>
    private string? UseBase(INamedTypeSymbol? type)
    {
        if (type == null || GetAttribute(type, "LibraryBaseAttribute") is not { } a) return null;
        string lib = (string)a.ConstructorArguments[0].Value!, name = (string)a.ConstructorArguments[1].Value!, ctype = (string)a.ConstructorArguments[2].Value!;
        _bases[name] = ctype;
        // muimaster.library: no header (the MUI SDK's are not used); the emitter defines its calls itself (MuiMasterCalls).
        if (lib == "muimaster") _usesMuiMaster = true;
        else _headers.Add($"proto/{lib}.h");
        return "es_" + name;
    }

    // ------------------------------------------------------------------ driver

    public string Emit()
    {
        var types = CollectTypes();
        // Records get members made by the C# compiler (Equals, ==, ToString, copy) that E# has no bodies for.
        foreach (var record in types.Where(t => t.IsRecord))
            Unsupported(record.DeclaringSyntaxReferences.First().GetSyntax(), $"record '{record.Name}'; write a class or struct (with your own == if needed)");
        types = types.Where(t => !t.IsRecord).ToList();
        // C has no namespaces: the namespace is part of a type's C name, so same-named types in different namespaces are apart.
        // Joining with '_' can still make two different types meet (namespace A_B type C and namespace A type B_C), which is reported.
        foreach (var group in types.GroupBy(TypeName).Where(g => g.Count() > 1))
            foreach (var dup in group.Skip(1))
                Unsupported(dup.DeclaringSyntaxReferences.First().GetSyntax(),
                    $"C name '{group.Key}' of '{dup.ToDisplayString()}' is already used by '{group.First().ToDisplayString()}'; rename one of them");
        CheckMemberNameCollisions(types);
        var structs = SortStructs(types.OfType<INamedTypeSymbol>().Where(t => t.TypeKind == TypeKind.Struct));
        var heapClasses = types.Where(IsHeapClass).Select(t => (INamedTypeSymbol)t).OrderBy(Depth).ToList();
        _heapClasses = heapClasses;
        foreach (var c in heapClasses)
            if (BaseClass(c) != null || c.GetMembers().OfType<IMethodSymbol>().Any(IsDynamic)) _polyRoots.Add(RootClass(c));
        var enums = types.Where(t => t.TypeKind == TypeKind.Enum).ToList();
        var statics = types.Where(t => t.TypeKind == TypeKind.Class).ToList();

        var reachable = ReachableMethods(types);
        var usedStatics = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var u in _usedSymbols) usedStatics.Add(u);
        var body = new StringBuilder();
        foreach (var e in enums) EmitEnum(e);
        var interfaces = CollectInterfaces();
        foreach (var i in interfaces)
        {
            // An interface value is the object and the table of its functions; the table is defined once all types are known.
            Line($"struct {TypeName(i)}_vtbl;");
            Line($"typedef struct {{ void *obj; const struct {TypeName(i)}_vtbl *vt; }} {TypeName(i)};");
        }
        foreach (var c in heapClasses) Line($"typedef struct {TypeName(c)} {TypeName(c)};");   // forward declarations: classes point to each other
        if (heapClasses.Any(IsPoly))
        {
            Line("struct ES_Type { const struct ES_Type *base; };");
            // Type ids are table addresses: an object is an instance of a class when that class's id is on its chain of base classes.
            Line("static __attribute__((unused)) bool es_is(const struct ES_Type *t, const struct ES_Type *want) { for (; t != NULL; t = t->base) if (t == want) return true; return false; }");
            foreach (var c in heapClasses.Where(c => IsPoly(c) && BaseClass(c) == null)) Line($"struct {CvtName(c)};");
        }
        foreach (var s in structs) EmitStruct(s);
        var typeText = _out.ToString();
        _out.Clear();
        // Class structs come after the span, tuple and delegate typedefs: a class may hold a delegate field or an event
        // (those typedefs only need the structs and the forward declarations above, classes being pointers).
        foreach (var c in heapClasses) EmitClass(c);
        var classText = _out.ToString();
        _out.Clear();
        // The tables of virtual and interface functions come after the span, tuple and delegate typedefs, which their parameters may
        // use (a method taking a ReadOnlySpan of a struct); those typedefs only need the structs and the forward declarations above.
        foreach (var c in heapClasses.Where(IsPoly)) EmitCvt(c);
        foreach (var c in heapClasses.Where(IsPoly))
        {
            Line($"static __attribute__((unused)) const struct {CvtName(c)} {TypeName(c)}_vt;");
            _usesAlloc = true;
            Line($"static __attribute__((unused)) {TypeName(c)} *{TypeName(c)}_alloc(void) {{ {TypeName(c)} *o = ES_NEW({TypeName(c)}); o->{VtPath(c)} = (const struct {CvtName(RootClass(c))} *)&{TypeName(c)}_vt; return o; }}");
        }
        foreach (var i in interfaces) EmitInterface(i);
        var interfaceText = _out.ToString();
        _out.Clear();

        var fields = statics.SelectMany(t => t.GetMembers().OfType<IFieldSymbol>()).Where(f => f.IsStatic && !f.IsConst && f.AssociatedSymbol == null && usedStatics.Contains(f)).ToList();
        foreach (var f in fields) EmitStaticField(f);
        var staticProps = types.SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
            .Where(p => p.IsStatic && IsAutoProperty(p) && usedStatics.Contains(p)).ToList();
        foreach (var p in staticProps) EmitStaticProperty(p);
        var staticEvents = statics.SelectMany(t => t.GetMembers().OfType<IEventSymbol>()).Where(e => e.IsStatic && IsFieldLikeEvent(e)).ToList();
        foreach (var ev in staticEvents)
        {
            _usesClosures = true;
            Line($"static es_event {StaticEventName(ev)};");
        }
        if (fields.Count + staticProps.Count + staticEvents.Count > 0) Line("");

        var methods = types.SelectMany(t => t.GetMembers().OfType<IMethodSymbol>())
            .Where(m => !m.IsGenericMethod)
            .Where(m => reachable.Contains(m))
            .Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.Constructor
                        or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.StaticConstructor
                        && !m.IsExtern && DeclaresBody(m))
            .ToList();

        methods.AddRange(reachable.Where(m => m.MethodKind == MethodKind.LocalFunction));
        methods.AddRange(reachable.Where(m => m.MethodKind == MethodKind.AnonymousFunction)
            .GroupBy(m => FuncName(m)).Select(g => g.First()));
        _staticConstructors.AddRange(methods.Where(m => m.MethodKind == MethodKind.StaticConstructor));
        foreach (var m in methods)
        {
            Line(Prototype(m) + ";");
            if (m.MethodKind == MethodKind.Constructor && IsHeapClass(m.ContainingType)) Line(InitPrototype(m) + ";");
        }
        foreach (var c in _fieldInitClasses) Line($"static void {FieldInitName(c)}({TypeName(c)} *{ThisName});");
        if (methods.Count > 0 || _fieldInitClasses.Count > 0) Line("");
        CheckBodilessAsm(reachable);
        EmitInterruptDeclarations(methods);
        EmitHookDeclarations(methods);
        foreach (var c in heapClasses.Where(IsPoly)) EmitVtInstance(c, reachable);
        foreach (var c in heapClasses)
            foreach (var i in interfaces.Where(i => c.AllInterfaces.Contains(i, SymbolEqualityComparer.Default)))
                EmitVtable(i, c);

        foreach (var c in _fieldInitClasses) EmitFieldInit(c);
        foreach (var m in methods) EmitMethod(m);

        EmitMain();
        EmitInterruptEntries();
        EmitHookEntries();

        body.Append(typeText);
        foreach (var span in _spans.Values) body.Append(span);
        if (_spans.Count > 0) body.Append('\n');
        body.Append(classText);
        body.Append(interfaceText);
        body.Append(_out);
        _out.Clear();

        var result = new StringBuilder();
        result.AppendLine("/* Generated by the E# compiler. Do not edit. */");
        result.AppendLine("#include <stdbool.h>");
        result.AppendLine("#include <stddef.h>");
        result.AppendLine("#include <stdint.h>");
        // [LibraryBase] libraries may be absent (libnix refuses to start a program whose startup list names a missing
        // library) or are devices, so their base is renamed to a variable of ours that SetBase fills in; nothing refers to
        // the usual name, so neither libnix nor AROS's autoinit tries to open it.
        foreach (var (name, type) in _bases) { result.AppendLine($"#define {name} es_{name}"); result.AppendLine($"extern {type} *es_{name};"); }
        foreach (var h in _headers) result.AppendLine($"#include <{h}>");
        if (UsesHookEntries)
        {
            // [HookEntry] gates on MorphOS: struct EmulLibEntry, TRAP_LIB and the 68k registers (REG_A0, ...) of the emulation handle.
            result.AppendLine("#if defined(__MORPHOS__)");
            result.AppendLine("#include <emul/emulregs.h>");
            result.AppendLine("#endif");
        }
        if (_headers.Contains("proto/dos.h"))
        {
            // AmigaOS 4 renamed some dos.library calls (Flush is FFlush, DeleteFile is Delete); its SDK maps the classic names here.
            result.AppendLine("#if defined(__amigaos4__)");
            result.AppendLine("#include <dos/obsolete.h>");
            result.AppendLine("#endif");
        }
        foreach (var (name, type) in _bases) result.AppendLine($"{type} *es_{name};");
        // libauto (-lauto) opens the interface of the usual libraries at start-up on AmigaOS 4, but it has none for
        // cybergraphics.library, which may be absent anyway: SetBase gets "main" version 1 itself and drops it again.
        if (_bases.ContainsKey("CyberGfxBase"))
        {
            result.AppendLine("#if defined(__amigaos4__)");
            result.AppendLine("struct CyberGfxIFace *ICyberGfx;");
            result.AppendLine("static void es_setbase_CyberGfxBase(struct Library *lib)");
            result.AppendLine("{");
            result.AppendLine("    if (ICyberGfx != NULL) { DropInterface((struct Interface *)ICyberGfx); ICyberGfx = NULL; }");
            result.AppendLine("    if (lib != NULL && (ICyberGfx = (struct CyberGfxIFace *)GetInterface(lib, \"main\", 1, NULL)) == NULL) lib = NULL;");
            result.AppendLine("    es_CyberGfxBase = lib;");
            result.AppendLine("}");
            result.AppendLine("#define ES_SETBASE_CyberGfxBase(l) es_setbase_CyberGfxBase((struct Library *)(l))");
            result.AppendLine("#else");
            result.AppendLine("#define ES_SETBASE_CyberGfxBase(l) (es_CyberGfxBase = (__typeof__(es_CyberGfxBase))(l))");
            result.AppendLine("#endif");
        }
        if (_usesMuiMaster) result.Append(MuiMasterCalls);
        result.AppendLine();
        if (_usesBounds)
        {
            result.AppendLine("#include <stdio.h>");
            result.AppendLine("#include <stdlib.h>");
            AppendFatalHelper(result);
            result.AppendLine("static inline int32_t es_idx(int32_t i, int32_t n, const char *file, int line)");
            result.AppendLine("{");
            result.AppendLine("    if ((uint32_t)i >= (uint32_t)n)");
            result.AppendLine("    {");
            result.AppendLine("#if defined(__AROS__)");
            result.AppendLine("        es_errstr(file); es_errstr(\"(\"); es_errint(line); es_errstr(\"): index \"); es_errint(i);");
            result.AppendLine("        es_errstr(\" out of range 0..\"); es_errint((long)n - 1); es_errstr(\"\\n\");");
            result.AppendLine("#else");
            result.AppendLine("        fprintf(stderr, \"%s(%d): index %ld out of range 0..%ld\\n\", file, line, (long)i, (long)n - 1);");
            result.AppendLine("#endif");
            result.AppendLine("        es_fatal();");
            result.AppendLine("    }");
            result.AppendLine("    return i;");
            result.AppendLine("}");
            result.AppendLine("#define ES_IDX(i, n) es_idx((i), (n), __FILE__, __LINE__)");
            result.AppendLine();
        }
        if (_usesStrSearch)
        {
            // string.IndexOf/LastIndexOf/Contains/StartsWith/EndsWith: ordinal, byte by byte, no exceptions (null counts as ""; a null
            // value finds nothing; a startIndex past the end gives -1, a negative one is 0; the empty string is found at startIndex).
            foreach (var line in new[]
            {
                "static __attribute__((unused)) int32_t es_str_len(const char *s) { int32_t n = 0; if (s == NULL) return 0; while (s[n]) n++; return n; }",
                "static __attribute__((unused)) int32_t es_str_find(const char *s, const char *v, int32_t start)",
                "{",
                "    int32_t len = es_str_len(s), vl = es_str_len(v);",
                "    if (v == NULL || start > len) return -1;",
                "    if (start < 0) start = 0;",
                "    for (int32_t i = start; i + vl <= len; i++)",
                "    {",
                "        int32_t k = 0;",
                "        while (k < vl && s[i + k] == v[k]) k++;",
                "        if (k == vl) return i;",
                "    }",
                "    return -1;",
                "}",
                "static __attribute__((unused)) int32_t es_str_findc(const char *s, int32_t c, int32_t start)",
                "{",
                "    int32_t len = es_str_len(s);",
                "    if (start > len) return -1;",
                "    if (start < 0) start = 0;",
                "    for (int32_t i = start; i < len; i++) if ((unsigned char)s[i] == (c & 255)) return i;",
                "    return -1;",
                "}",
                "static __attribute__((unused)) int32_t es_str_rfind(const char *s, const char *v)",
                "{",
                "    int32_t len = es_str_len(s), vl = es_str_len(v);",
                "    if (v == NULL) return -1;",
                "    for (int32_t i = len - vl; i >= 0; i--)",
                "    {",
                "        int32_t k = 0;",
                "        while (k < vl && s[i + k] == v[k]) k++;",
                "        if (k == vl) return i;",
                "    }",
                "    return -1;",
                "}",
                "static __attribute__((unused)) int32_t es_str_rfindc(const char *s, int32_t c)",
                "{",
                "    for (int32_t i = es_str_len(s) - 1; i >= 0; i--) if ((unsigned char)s[i] == (c & 255)) return i;",
                "    return -1;",
                "}",
                "static __attribute__((unused)) int es_str_starts(const char *s, const char *v)",
                "{",
                "    int32_t vl = es_str_len(v);",
                "    if (v == NULL || vl > es_str_len(s)) return 0;",
                "    for (int32_t k = 0; k < vl; k++) if (s[k] != v[k]) return 0;",
                "    return 1;",
                "}",
                "static __attribute__((unused)) int es_str_ends(const char *s, const char *v)",
                "{",
                "    int32_t vl = es_str_len(v), len = es_str_len(s);",
                "    if (v == NULL || vl > len) return 0;",
                "    for (int32_t k = 0; k < vl; k++) if (s[len - vl + k] != v[k]) return 0;",
                "    return 1;",
                "}",
                "static __attribute__((unused)) int es_str_endsc(const char *s, int32_t c)",
                "{",
                "    int32_t len = es_str_len(s);",
                "    return len > 0 && (unsigned char)s[len - 1] == (c & 255);",
                "}",
            })
                result.AppendLine(line);
            result.AppendLine();
        }
        if (_usesFmt)
        {
            foreach (var line in new[]
            {
                "static char es_fmt_buf[8][128];",
                "static int es_fmt_index;",
                "static char *es_fmt_begin(void) { char *b = es_fmt_buf[es_fmt_index]; es_fmt_index = (es_fmt_index + 1) & 7; return b; }",
                // s.Substring(start[, count]) copies into the same ring as interpolation; out-of-range arguments are clamped (no exceptions).
                "static __attribute__((unused)) const char *es_substr(const char *s, int32_t start, int32_t count)",
                "{",
                "    char *b = es_fmt_begin();",
                "    int32_t len = 0, n = 0;",
                "    if (s == NULL) s = \"\";",
                "    while (s[len]) len++;",
                "    if (start < 0) start = 0;",
                "    if (start > len) start = len;",
                "    if (count < 0 || count > len - start) count = len - start;",
                "    while (n < count && n < 127) { b[n] = s[start + n]; n++; }",
                "    b[n] = 0;",
                "    return b;",
                "}",
                "static __attribute__((unused)) int es_put_pad(char *b, int n, const char *s, int len, int width)",
                "{",
                "    int pad = (width < 0 ? -width : width) - len;",
                "    if (width > 0) while (pad-- > 0 && n < 127) b[n++] = ' ';",
                "    for (int i = 0; i < len && n < 127; i++) b[n++] = s[i];",
                "    if (width < 0) while (pad-- > 0 && n < 127) b[n++] = ' ';",
                "    return n;",
                "}",
                "static __attribute__((unused)) int es_put_str(char *b, int n, const char *s, int width)",
                "{",
                "    int len = 0;",
                "    if (s == NULL) s = \"\";",
                "    while (s[len]) len++;",
                "    return es_put_pad(b, n, s, len, width);",
                "}",
                "static __attribute__((unused)) int es_put_chr(char *b, int n, int c, int width)",
                "{",
                "    char t = (char)c;",
                "    return es_put_pad(b, n, &t, 1, width);",
                "}",
                "static __attribute__((unused)) int es_put_u(char *b, int n, uint32_t v, int width, int digits, int hex)",
                "{",
                "    char t[40];",
                "    int len = 0;",
                "    uint32_t base = hex ? 16u : 10u;",
                "    do { uint32_t d = v % base; t[len++] = (char)(d < 10 ? '0' + d : (hex == 2 ? 'a' : 'A') + d - 10); v /= base; } while (v != 0 && len < 39);",
                "    while (len < digits && len < 39) t[len++] = '0';",
                "    char r[40];",
                "    for (int i = 0; i < len; i++) r[i] = t[len - 1 - i];",
                "    return es_put_pad(b, n, r, len, width);",
                "}",
                "static __attribute__((unused)) int es_put_u64(char *b, int n, uint64_t v, int width, int digits, int hex)",
                "{",
                "    char t[72];",
                "    int len = 0;",
                "    uint64_t base = hex ? 16u : 10u;",
                "    do { uint32_t d = (uint32_t)(v % base); t[len++] = (char)(d < 10 ? '0' + d : (hex == 2 ? 'a' : 'A') + d - 10); v /= base; } while (v != 0 && len < 70);",
                "    while (len < digits && len < 70) t[len++] = '0';",
                "    char r[72];",
                "    for (int i = 0; i < len; i++) r[i] = t[len - 1 - i];",
                "    return es_put_pad(b, n, r, len, width);",
                "}",
                "static __attribute__((unused)) int es_put_i64(char *b, int n, int64_t v, int width, int digits, int hex)",
                "{",
                "    if (v >= 0 || hex) return es_put_u64(b, n, (uint64_t)v, width, digits, hex);",
                "    char t[72];",
                "    uint64_t m = 0u - (uint64_t)v;",
                "    int len = 0;",
                "    do { t[len++] = (char)('0' + (uint32_t)(m % 10u)); m /= 10u; } while (m != 0 && len < 70);",
                "    while (len < digits && len < 70) t[len++] = '0';",
                "    char r[72];",
                "    r[0] = '-';",
                "    for (int i = 0; i < len; i++) r[i + 1] = t[len - 1 - i];",
                "    return es_put_pad(b, n, r, len + 1, width);",
                "}",
                "static __attribute__((unused)) int es_put_i(char *b, int n, int32_t v, int width, int digits, int hex)",
                "{",
                "    if (v >= 0 || hex) return es_put_u(b, n, (uint32_t)v, width, digits, hex);",
                "    char t[40];",
                "    uint32_t m = 0u - (uint32_t)v;",
                "    int len = 0;",
                "    do { t[len++] = (char)('0' + m % 10u); m /= 10u; } while (m != 0 && len < 38);",
                "    while (len < digits && len < 38) t[len++] = '0';",
                "    char r[40];",
                "    r[0] = '-';",
                "    for (int i = 0; i < len; i++) r[i + 1] = t[len - 1 - i];",
                "    return es_put_pad(b, n, r, len + 1, width);",
                "}",
            }) result.AppendLine(line);
        }
        if (_usesClosures) _usesAlloc = true;
        if (_usesAlloc)
        {
            result.AppendLine("#include <stdio.h>");
            result.AppendLine("#include <stdlib.h>");
            if (!_usesBounds) AppendFatalHelper(result);
            result.AppendLine("static void *es_new(size_t n)");
            result.AppendLine("{");
            result.AppendLine("    void *p = calloc(1, n);");
            result.AppendLine("    if (p == NULL) { es_errstr(\"out of memory\\n\"); es_fatal(); }");
            result.AppendLine("    return p;");
            result.AppendLine("}");
            result.AppendLine("#define ES_NEW(T) ((T *)es_new(sizeof(T)))");
            if (_usesClosures) AppendClosureRuntime(result);
        }
        if (_usesStrlen)
        {
            // Our own, not libc's: a call to strlen is an undefined symbol that the AROS loader resolved wrongly, and it keeps the output libc-free.
            result.AppendLine("static inline int32_t es_strlen(const char *s)");
            result.AppendLine("{");
            result.AppendLine("    const char *p = s;");
            result.AppendLine("    while (*p != 0) p++;");
            result.AppendLine("    return (int32_t)(p - s);");
            result.AppendLine("}");
        }
        if (_usesStreq)
        {
            // String equality as in C#: by the characters, and null equals only null. Our own loop, not libc's strcmp (see es_strlen).
            result.AppendLine("static inline int es_streq(const char *a, const char *b)");
            result.AppendLine("{");
            result.AppendLine("    if (a == b) return 1;");
            result.AppendLine("    if (a == 0 || b == 0) return 0;");
            result.AppendLine("    while (*a != 0 && *a == *b) { a++; b++; }");
            result.AppendLine("    return *a == *b;");
            result.AppendLine("}");
        }
        if (UsesArgs)
        {
            // Args: main keeps argc/argv; argument 0 is the program name, so Args counts from 1.
            // A Workbench start has argc 0 and argv pointing at the WBStartup message (the C startup code of every Amiga target does this):
            // the program icon's tool types then stand in for the command line (issue #84). "KEY=VALUE" and "SWITCH" tool types are the
            // words Args.Value/Has already look for; a tool type in parentheses is switched off in the icon and is skipped.
            result.AppendLine("static int es_argc;");
            result.AppendLine("static char **es_argv;");
            result.AppendLine("#define ES_WB_MAX 64");
            result.AppendLine("static char *es_wb_args[ES_WB_MAX + 2];");
            result.AppendLine("static char es_wb_buf[2048];");
            // Workbench's own tool types (they steer how Workbench starts the program) are not program arguments.
            result.AppendLine("static int es_tt_is_workbench(const char *s)");
            result.AppendLine("{");
            result.AppendLine("    static const char *const names[] = { \"TOOLPRI\", \"STARTPRI\", \"DONOTWAIT\", \"TOOLWINDOW\", NULL };");
            result.AppendLine("    for (int k = 0; names[k] != NULL; k++)");
            result.AppendLine("    {");
            result.AppendLine("        const char *n = names[k]; const char *p = s;");
            result.AppendLine("        while (*n != 0 && *p != 0 && (*p == *n || *p == *n + 32)) { p++; n++; }");
            result.AppendLine("        if (*n == 0 && (*p == 0 || *p == '=')) return 1;");
            result.AppendLine("    }");
            result.AppendLine("    return 0;");
            result.AppendLine("}");
            result.AppendLine("static int es_tt_is_newicons_marker(const char *s)");
            result.AppendLine("{");
            result.AppendLine("    const char *m = \"*** DON'T EDIT THE FOLLOWING LINES!! ***\";");
            result.AppendLine("    while (*m != 0 && *s == *m) { s++; m++; }");
            result.AppendLine("    return *m == 0 && *s == 0;");
            result.AppendLine("}");
            // The words are copied into a static buffer (quotes around a value removed, as a Shell does), so the DiskObject can be freed at once.
            result.AppendLine("static int es_wb_n; static unsigned es_wb_used;");
            result.AppendLine("static int es_wb_add(const char *src)   /* 0 when full */");
            result.AppendLine("{");
            result.AppendLine("    if (es_wb_n >= ES_WB_MAX + 1) return 0;   /* more tool types than that are dropped */");
            result.AppendLine("    unsigned len = 0, eq = 0;");
            result.AppendLine("    while (src[len] != 0) { if (src[len] == '=' && eq == 0) eq = len + 1; len++; }");
            result.AppendLine("    unsigned from = 0, to = len;   /* NAME=\"two words\" loses its quotes, as in a Shell line */");
            result.AppendLine("    if (eq != 0 && len - eq >= 2 && src[eq] == '\"' && src[len - 1] == '\"') { from = eq + 1; to = len - 1; }");
            result.AppendLine("    unsigned out = from != 0 ? eq + (to - from) : len;");
            result.AppendLine("    if (es_wb_used + out + 1 > sizeof es_wb_buf) return 0;");
            result.AppendLine("    char *dst = es_wb_buf + es_wb_used; unsigned o = 0;");
            result.AppendLine("    if (from != 0) { for (unsigned i = 0; i < eq; i++) dst[o++] = src[i]; for (unsigned i = from; i < to; i++) dst[o++] = src[i]; }");
            result.AppendLine("    else for (unsigned i = 0; i < len; i++) dst[o++] = src[i];");
            result.AppendLine("    dst[o++] = 0;");
            result.AppendLine("    es_wb_args[es_wb_n++] = dst; es_wb_used += o;");
            result.AppendLine("    return 1;");
            result.AppendLine("}");
            result.AppendLine("static __attribute__((unused)) void es_args_from_tooltypes(const char *name, char *const *tt)");
            result.AppendLine("{");
            result.AppendLine("    es_wb_n = 0; es_wb_used = 0;");
            result.AppendLine("    es_wb_add(name != NULL ? name : \"\");   /* argument 0 is the program name */");
            // A NewIcons image (issue #101) is a marker tool type and then IM1=/IM2= lines of picture data: none of it is an argument.
            result.AppendLine("    int ni = 0;   /* after the NewIcons marker */");
            result.AppendLine("    for (; tt != NULL && *tt != NULL; tt++)");
            result.AppendLine("    {");
            result.AppendLine("        const char *s = *tt;");
            result.AppendLine("        if (s[0] == '*' && es_tt_is_newicons_marker(s)) { ni = 1; continue; }");
            result.AppendLine("        if (ni && s[0] == 'I' && s[1] == 'M' && (s[2] == '1' || s[2] == '2') && s[3] == '=') continue;");
            result.AppendLine("        if (s[0] != 0 && s[0] != '(' && !es_tt_is_workbench(s) && !es_wb_add(s)) break;");
            result.AppendLine("    }");
            result.AppendLine("    es_wb_args[es_wb_n] = NULL;");
            result.AppendLine("    es_argc = es_wb_n;");
            result.AppendLine("    es_argv = es_wb_args;");
            result.AppendLine("}");
            result.AppendLine("#if defined(__AMIGA__) || defined(__amigaos__) || defined(__amigaos4__) || defined(__MORPHOS__) || defined(__AROS__)");
            // icon.library is on the Workbench disk (not in ROM) on a classic Amiga, but Workbench has it open when it starts a program, so
            // it is opened only here, in a Workbench start. The usual base names are renamed so the C startup code does not open it itself.
            result.AppendLine("#define IconBase es_IconBase");
            result.AppendLine("#define IIcon es_IIcon");
            result.AppendLine("#include <proto/exec.h>");
            result.AppendLine("#include <proto/dos.h>");
            result.AppendLine("#include <proto/icon.h>");
            result.AppendLine("#include <workbench/startup.h>");
            result.AppendLine("#include <workbench/workbench.h>");
            result.AppendLine("struct Library *es_IconBase;");
            result.AppendLine("#if defined(__amigaos4__)");
            result.AppendLine("struct IconIFace *es_IIcon;");
            result.AppendLine("#endif");
            result.AppendLine("static void es_args_from_workbench(struct WBStartup *wb)");
            result.AppendLine("{");
            result.AppendLine("    if (wb == NULL || wb->sm_NumArgs < 1 || wb->sm_ArgList == NULL || wb->sm_ArgList[0].wa_Name == NULL) return;");
            result.AppendLine("    es_IconBase = OpenLibrary((CONST_STRPTR)\"icon.library\", 39);");
            result.AppendLine("    if (es_IconBase == NULL) return;");
            result.AppendLine("#if defined(__amigaos4__)");
            result.AppendLine("    es_IIcon = (struct IconIFace *)GetInterface(es_IconBase, \"main\", 1, NULL);");
            result.AppendLine("    if (es_IIcon != NULL)");
            result.AppendLine("#endif");
            result.AppendLine("    {");
            result.AppendLine("        BPTR old = CurrentDir(wb->sm_ArgList[0].wa_Lock);");
            result.AppendLine("        struct DiskObject *dob = GetDiskObject((CONST_STRPTR)wb->sm_ArgList[0].wa_Name);");
            result.AppendLine("        CurrentDir(old);");
            result.AppendLine("        if (dob != NULL) { es_args_from_tooltypes((const char *)wb->sm_ArgList[0].wa_Name, (char *const *)dob->do_ToolTypes); FreeDiskObject(dob); }");
            result.AppendLine("    }");
            result.AppendLine("#if defined(__amigaos4__)");
            result.AppendLine("    if (es_IIcon != NULL) { DropInterface((struct Interface *)es_IIcon); es_IIcon = NULL; }");
            result.AppendLine("#endif");
            result.AppendLine("    CloseLibrary(es_IconBase);");
            result.AppendLine("    es_IconBase = NULL;");
            result.AppendLine("}");
            result.AppendLine("#endif");
            result.AppendLine("static __attribute__((unused)) int32_t es_args_count(void) { return es_argc > 1 ? (int32_t)es_argc - 1 : 0; }");
            result.AppendLine("static __attribute__((unused)) const char *es_args_get(int32_t i) { return i >= 0 && i < es_args_count() ? es_argv[i + 1] : NULL; }");
        }
        if (_natives.Contains("es_fixdiv"))
        {
            result.AppendLine("static inline int32_t es_fixdiv(int32_t a, int32_t b)");
            result.AppendLine("{");
            result.AppendLine("#if defined(__mc68020__) || defined(__mc68030__) || defined(__mc68040__) || defined(__mc68060__)");
            result.AppendLine("    if (b == 0) return 0;");
            result.AppendLine("    uint32_t ua = a < 0 ? 0u - (uint32_t)a : (uint32_t)a, ub = b < 0 ? 0u - (uint32_t)b : (uint32_t)b;");
            result.AppendLine("    if (ua < (ub > 0x1FFFFu ? 0xFFFFFFFFu : ub << 15))   /* the quotient fits in 32 bits, so divs.l cannot overflow */");
            result.AppendLine("    {");
            result.AppendLine("        int32_t q = (int32_t)((uint32_t)a << 16), r = a >> 16;   /* the dividend a << 16 as r:q */");
            result.AppendLine("        __asm__(\"divs.l %2,%1:%0\" : \"+d\"(q), \"+d\"(r) : \"d\"(b) : \"cc\");");
            result.AppendLine("        return q;");
            result.AppendLine("    }");
            result.AppendLine("#endif");
            result.AppendLine("    return (int32_t)(((int64_t)a << 16) / b);");
            result.AppendLine("}");
            result.AppendLine();
        }
        if (_natives.Contains("es_rawputchar") || _natives.Contains("es_rawioinit"))
        {
            // exec's raw serial output: RawIOInit (LVO -504) and RawPutChar (LVO -516) are in no header of the m68k NDK, so call the vectors.
            result.AppendLine("#if defined(__AROS__)");
            result.AppendLine("#include <proto/exec.h>");
            result.AppendLine("static inline void es_rawioinit(void) { }");
            result.AppendLine("static inline void es_rawputchar(int32_t c) { RawPutChar((UBYTE)c); }");
            result.AppendLine("#elif defined(__mc68000__)");
            result.AppendLine("static inline void es_rawioinit(void)");
            result.AppendLine("{");
            result.AppendLine("    register void *a6 __asm(\"a6\") = *(void **)4;   /* ExecBase */");
            result.AppendLine("    register int32_t d0 __asm(\"d0\");");
            result.AppendLine("    __asm volatile(\"jsr -504(%%a6)\" : \"=r\"(d0), \"+r\"(a6) : : \"d1\", \"a0\", \"a1\", \"memory\", \"cc\");");
            result.AppendLine("}");
            result.AppendLine("static inline void es_rawputchar(int32_t c)");
            result.AppendLine("{");
            result.AppendLine("    register void *a6 __asm(\"a6\") = *(void **)4;   /* ExecBase */");
            result.AppendLine("    register int32_t d0 __asm(\"d0\") = c;");
            result.AppendLine("    __asm volatile(\"jsr -516(%%a6)\" : \"+r\"(d0), \"+r\"(a6) : : \"d1\", \"a0\", \"a1\", \"memory\", \"cc\");");
            result.AppendLine("}");
            result.AppendLine("#elif defined(__amigaos4__)");
            result.AppendLine("/* AmigaOS 4 has no RawPutChar in its exec interface: DebugPrintF goes to the kernel debug output (serial with the kernel option \"serial\"). */");
            result.AppendLine("#include <proto/exec.h>");
            result.AppendLine("static inline void es_rawioinit(void) { }");
            result.AppendLine("static inline void es_rawputchar(int32_t c) { char s[2]; s[0] = (char)c; s[1] = 0; (IExec->DebugPrintF)(\"%s\", s); }   /* parenthesised: not the __USE_INLINE__ macro */");
            result.AppendLine("#else");
            result.AppendLine("static inline void es_rawioinit(void) { }");
            result.AppendLine("static inline void es_rawputchar(int32_t c) { (void)c; }   /* no serial trace on this target yet */");
            result.AppendLine("#endif");
            result.AppendLine();
        }
        if (_natives.Contains("es_domethod") || _natives.Contains("es_boopsi_prepare"))
        {
            // es_boopsi_prepare runs at the first BoopsiObject.Create (on the program's own task), so the AROS probe below never
            // runs for the first time inside a hook on another task.
            // DoMethodA is not a library vector: amiga.lib (m68k), a macro (AROS, MorphOS) or the intuition interface (AmigaOS 4).
            result.AppendLine("#include <proto/intuition.h>");
            result.AppendLine("#if defined(__amigaos4__)");
            result.AppendLine("static inline __attribute__((unused)) uintptr_t es_domethod(void *obj, void *msg) { return (uintptr_t)IDoMethodA((Object *)obj, (Msg)msg); }");
            result.AppendLine("static inline __attribute__((unused)) void es_boopsi_prepare(void) { }");
            result.AppendLine("#elif defined(__AROS__)");
            // The AROS SDK's DoMethodA macro reads the class pointer just before the object through the SDK's struct _Object, but
            // newer AROS builds align that struct (SMP), so the class pointer may sit further back. Find out once where it is with an
            // object of a class of our own (its class pointer is known), then call the class's dispatcher as DoMethodA does.
            result.AppendLine("#include <intuition/classes.h>");
            result.AppendLine("#include <utility/hooks.h>");
            result.AppendLine("#include <proto/dos.h>");
            result.AppendLine("static __attribute__((unused)) IPTR es_probe_dispatch(struct Hook *h, Object *o, Msg msg) { return CALLHOOKPKT((struct Hook *)((Class *)h)->cl_Super, o, msg); }");
            result.AppendLine("static int es_oclass_offset;   /* 0: not found out yet, -1: not found (no DoMethod then), else the offset */");
            result.AppendLine("static __attribute__((unused)) int es_oclass_probe(void)");
            result.AppendLine("{");
            result.AppendLine("    Class *cl = MakeClass(NULL, (ClassID)\"rootclass\", NULL, 0, 0);");
            result.AppendLine("    if (cl == NULL) return 0;   /* out of memory: try again next time */");
            result.AppendLine("    cl->cl_Dispatcher.h_Entry = (HOOKFUNC)es_probe_dispatch;");
            result.AppendLine("    Object *o = NewObjectA(cl, NULL, NULL);");
            result.AppendLine("    int found = 0;");
            result.AppendLine("    if (o != NULL)");
            result.AppendLine("    {");
            result.AppendLine("        found = -1;");
            result.AppendLine("        for (int k = 1; k <= 4 && found < 0; k++)");
            result.AppendLine("            if (*(Class **)((char *)o - k * (int)sizeof(void *)) == cl) found = k * (int)sizeof(void *);");
            result.AppendLine("        DisposeObject(o);");
            result.AppendLine("    }");
            result.AppendLine("    FreeClass(cl);");
            result.AppendLine("    if (found < 0) PutStr((CONST_STRPTR)\"E#: DoMethod cannot find the class of a BOOPSI object on this AROS (unknown object layout); methods return 0\\n\");");
            result.AppendLine("    return found;");
            result.AppendLine("}");
            result.AppendLine("static inline __attribute__((unused)) void es_boopsi_prepare(void) { if (es_oclass_offset == 0) es_oclass_offset = es_oclass_probe(); }");
            result.AppendLine("static __attribute__((unused)) uintptr_t es_domethod(void *obj, void *msg)");
            result.AppendLine("{");
            result.AppendLine("    if (obj == NULL) return 0;");
            result.AppendLine("    if (es_oclass_offset == 0) es_oclass_offset = es_oclass_probe();");
            result.AppendLine("    if (es_oclass_offset <= 0) return 0;");
            result.AppendLine("    Class *c = *(Class **)((char *)obj - es_oclass_offset);");
            result.AppendLine("    return (uintptr_t)CALLHOOKPKT((struct Hook *)c, obj, msg);");
            result.AppendLine("}");
            result.AppendLine("#else");
            result.AppendLine("#include <clib/alib_protos.h>");
            result.AppendLine("static inline __attribute__((unused)) uintptr_t es_domethod(void *obj, void *msg) { return (uintptr_t)DoMethodA((Object *)obj, (Msg)msg); }");
            result.AppendLine("static inline __attribute__((unused)) void es_boopsi_prepare(void) { }");
            result.AppendLine("#endif");
            result.AppendLine();
        }
        // Non-Amiga targets: [Native] calls named es_std_* are standard C (stdio, stdlib, time), es_sdl_* need SDL 3. The C runtime is
        // pasted in whole (its functions are static and marked unused), so a program that only prints needs no SDL at all.
        var usesSdl = _natives.Any(n => n.StartsWith("es_sdl_", StringComparison.Ordinal));
        if (usesSdl || _natives.Any(n => n.StartsWith("es_std_", StringComparison.Ordinal)))
        {
            result.Append(LibrarySources.CRuntime("es_std.h"));
            if (usesSdl) result.Append(LibrarySources.CRuntime("es_sdl.h"));
            result.AppendLine();
        }
        if (_usesLen)
        {
            result.AppendLine("#define ES_LEN(a) ((int32_t)(sizeof(a) / sizeof((a)[0])))");
            result.AppendLine();
        }
        result.Append(body);
        return result.ToString();
    }

    private readonly HashSet<string> _natives = new();

    private bool _usesMuiMaster;

    /// <summary>
    /// The muimaster.library calls of ESharp.Platform's MuiMaster class, by vector (bias 30, the AROS fd), with no MUI header: the
    /// 68k registers on m68k, the 68k jump table through EmulateTags on AmigaOS 4, the emulation registers on MorphOS, AROS's own
    /// library call macros on AROS. Another compiler (the tests' host gcc) gets them from es_muimaster_shim.h.
    /// </summary>
    internal const string MuiMasterCalls = """
/* muimaster.library by vector (bias 30): no MUI header is used. */
#if defined(__amigaos4__)
#include <proto/exec.h>
#include <exec/emulation.h>
#define ES_MUI_CALL(lvo, ...) (IExec->EmulateTags)((APTR)((uint8_t *)es_MUIMasterBase - (lvo)), ET_RegisterA6, es_MUIMasterBase, __VA_ARGS__, TAG_DONE)
static __attribute__((unused)) void *MUI_NewObjectA(const void *cls, void *tags) { return (void *)ES_MUI_CALL(30, ET_RegisterA0, cls, ET_RegisterA1, tags); }
static __attribute__((unused)) void MUI_DisposeObject(void *obj) { ES_MUI_CALL(36, ET_RegisterA0, obj); }
static __attribute__((unused)) int32_t MUI_RequestA(void *app, void *win, uint32_t flags, const void *title, const void *gadgets, const void *format, void *parms)
{ return (int32_t)ES_MUI_CALL(42, ET_RegisterD0, app, ET_RegisterD1, win, ET_RegisterD2, flags, ET_RegisterA0, title, ET_RegisterA1, gadgets, ET_RegisterA2, format, ET_RegisterA3, parms); }
static __attribute__((unused)) int32_t MUI_Error(void) { return (int32_t)(IExec->EmulateTags)((APTR)((uint8_t *)es_MUIMasterBase - 66), ET_RegisterA6, es_MUIMasterBase, TAG_DONE); }
static __attribute__((unused)) void *MUI_MakeObjectA(int32_t type, uintptr_t *parms) { return (void *)ES_MUI_CALL(120, ET_RegisterD0, type, ET_RegisterA0, parms); }
#elif defined(__MORPHOS__)
#include <emul/emulinterface.h>
#include <emul/emulregs.h>
#define ES_MUI_CALL(lvo) ((REG_A6 = (ULONG)es_MUIMasterBase), (*MyEmulHandle->EmulCallDirectOS)(-(lvo)))
static __attribute__((unused)) void *MUI_NewObjectA(const void *cls, void *tags) { REG_A0 = (ULONG)cls; REG_A1 = (ULONG)tags; return (void *)ES_MUI_CALL(30); }
static __attribute__((unused)) void MUI_DisposeObject(void *obj) { REG_A0 = (ULONG)obj; ES_MUI_CALL(36); }
static __attribute__((unused)) int32_t MUI_RequestA(void *app, void *win, uint32_t flags, const void *title, const void *gadgets, const void *format, void *parms)
{ REG_D0 = (ULONG)app; REG_D1 = (ULONG)win; REG_D2 = flags; REG_A0 = (ULONG)title; REG_A1 = (ULONG)gadgets; REG_A2 = (ULONG)format; REG_A3 = (ULONG)parms; return (int32_t)ES_MUI_CALL(42); }
static __attribute__((unused)) int32_t MUI_Error(void) { return (int32_t)ES_MUI_CALL(66); }
static __attribute__((unused)) void *MUI_MakeObjectA(int32_t type, uintptr_t *parms) { REG_D0 = (ULONG)type; REG_A0 = (ULONG)parms; return (void *)ES_MUI_CALL(120); }
#elif defined(__AROS__)
#include <aros/libcall.h>
static __attribute__((unused)) void *MUI_NewObjectA(const void *cls, void *tags)
{ return (void *)AROS_LC2(APTR, MUI_NewObjectA, AROS_LCA(const void *, cls, A0), AROS_LCA(void *, tags, A1), struct Library *, es_MUIMasterBase, 5, MUIMaster); }
static __attribute__((unused)) void MUI_DisposeObject(void *obj) { AROS_LC1NR(void, MUI_DisposeObject, AROS_LCA(void *, obj, A0), struct Library *, es_MUIMasterBase, 6, MUIMaster); }
static __attribute__((unused)) int32_t MUI_RequestA(void *app, void *win, uint32_t flags, const void *title, const void *gadgets, const void *format, void *parms)
{
    return (int32_t)AROS_LC7(LONG, MUI_RequestA, AROS_LCA(void *, app, D0), AROS_LCA(void *, win, D1), AROS_LCA(ULONG, flags, D2), AROS_LCA(const void *, title, A0),
        AROS_LCA(const void *, gadgets, A1), AROS_LCA(const void *, format, A2), AROS_LCA(void *, parms, A3), struct Library *, es_MUIMasterBase, 7, MUIMaster);
}
static __attribute__((unused)) int32_t MUI_Error(void) { return (int32_t)AROS_LC0(LONG, MUI_Error, struct Library *, es_MUIMasterBase, 11, MUIMaster); }
static __attribute__((unused)) void *MUI_MakeObjectA(int32_t type, uintptr_t *parms)
{ return (void *)AROS_LC2(APTR, MUI_MakeObjectA, AROS_LCA(LONG, type, D0), AROS_LCA(IPTR *, (IPTR *)parms, A0), struct Library *, es_MUIMasterBase, 20, MUIMaster); }
#elif defined(__mc68000__)
#define ES_MUI_CLOBBER "d1", "memory", "cc"
static __attribute__((unused)) void *MUI_NewObjectA(const void *cls, void *tags)
{
    register void *a6 __asm("a6") = es_MUIMasterBase; register const void *a0 __asm("a0") = cls; register void *a1 __asm("a1") = tags; register void *d0 __asm("d0");
    __asm volatile("jsr -30(%%a6)" : "=r"(d0), "+r"(a0), "+r"(a1) : "r"(a6) : ES_MUI_CLOBBER);
    return d0;
}
static __attribute__((unused)) void MUI_DisposeObject(void *obj)
{
    register void *a6 __asm("a6") = es_MUIMasterBase; register void *a0 __asm("a0") = obj; register void *a1 __asm("a1"); register void *d0 __asm("d0");
    __asm volatile("jsr -36(%%a6)" : "=r"(d0), "+r"(a0), "=r"(a1) : "r"(a6) : ES_MUI_CLOBBER);
}
static __attribute__((unused)) int32_t MUI_RequestA(void *app, void *win, uint32_t flags, const void *title, const void *gadgets, const void *format, void *parms)
{
    register void *a6 __asm("a6") = es_MUIMasterBase; register void *d0 __asm("d0") = app; register void *d1 __asm("d1") = win; register uint32_t d2 __asm("d2") = flags;
    register const void *a0 __asm("a0") = title; register const void *a1 __asm("a1") = gadgets; register const void *a2 __asm("a2") = format; register void *a3 __asm("a3") = parms;
    __asm volatile("jsr -42(%%a6)" : "+r"(d0), "+r"(d1), "+r"(a0), "+r"(a1) : "r"(a6), "r"(d2), "r"(a2), "r"(a3) : "memory", "cc");
    return (int32_t)(intptr_t)d0;
}
static __attribute__((unused)) int32_t MUI_Error(void)
{
    register void *a6 __asm("a6") = es_MUIMasterBase; register int32_t d0 __asm("d0"); register void *a0 __asm("a0"); register void *a1 __asm("a1");
    __asm volatile("jsr -66(%%a6)" : "=r"(d0), "=r"(a0), "=r"(a1) : "r"(a6) : ES_MUI_CLOBBER);
    return d0;
}
static __attribute__((unused)) void *MUI_MakeObjectA(int32_t type, uintptr_t *parms)
{
    register void *a6 __asm("a6") = es_MUIMasterBase; register int32_t d0 __asm("d0") = type; register uintptr_t *a0 __asm("a0") = parms; register void *a1 __asm("a1");
    __asm volatile("jsr -120(%%a6)" : "+r"(d0), "+r"(a0), "=r"(a1) : "r"(a6) : ES_MUI_CLOBBER);
    return (void *)d0;
}
#else
#include <es_muimaster_shim.h>
#endif

""";

    private bool UsesArgs => _natives.Contains("es_args_count") || _natives.Contains("es_args_get");

    private bool IsDefined(string symbol) =>
        _compilation.SyntaxTrees.FirstOrDefault(_user.Contains)?.Options.PreprocessorSymbolNames.Contains(symbol) ?? false;

    private static bool DeclaresBody(IMethodSymbol m)
    {
        return m.DeclaringSyntaxReferences.Any(r => BodyOf(r.GetSyntax()) is (not null, _) or (_, not null));
    }

    /// <summary>The block or expression body of a method, operator, constructor or property accessor.</summary>
    private static (BlockSyntax? Block, ArrowExpressionClauseSyntax? Arrow) BodyOf(SyntaxNode n) => n switch
    {
        BaseMethodDeclarationSyntax b => (b.Body, b.ExpressionBody),
        LocalFunctionStatementSyntax lf => (lf.Body, lf.ExpressionBody),
        LambdaExpressionSyntax lam => (lam.Block, null),
        AccessorDeclarationSyntax a => (a.Body, a.ExpressionBody),
        ArrowExpressionClauseSyntax x => (null, x),
        PropertyDeclarationSyntax p => (null, p.ExpressionBody),
        IndexerDeclarationSyntax i => (null, i.ExpressionBody),
        _ => (null, null),
    };

    /// <summary>An auto-property ({ get; set; }): stored in a field of the same name.</summary>
    private static bool IsAutoProperty(IPropertySymbol p) =>
        !p.IsIndexer && !p.IsAbstract && !p.IsVirtual && !p.IsOverride && p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax
        {
            ExpressionBody: null, AccessorList: { } list
        } && list.Accessors.All(a => a.Body == null && a.ExpressionBody == null);

    private SemanticModel Model(SyntaxTree tree)
    {
        if (!_models.TryGetValue(tree, out var m))
            _models[tree] = m = _compilation.GetSemanticModel(tree);
        return m;
    }

    private SemanticModel Model(SyntaxNode node) => Model(node.SyntaxTree);

    /// <summary>
    /// s.IndexOf/LastIndexOf/Contains/StartsWith/EndsWith with a char or string value, an optional startIndex (IndexOf only) and an optional
    /// StringComparison.Ordinal (also together with the startIndex). Other overloads (culture or ignore-case comparisons, count, LastIndexOf with a
    /// start) are ES0099. The search is ordinal on every target. On the .NET host, the overloads WITHOUT a StringComparison (IndexOf, LastIndexOf,
    /// StartsWith, EndsWith with a string) use the current culture and can differ from the C helpers (for example a leading soft hyphen is ignored
    /// by culture-sensitive StartsWith), so pass StringComparison.Ordinal in code that is run with esharp test.
    /// </summary>
    private string StringSearch(InvocationExpressionSyntax inv, IMethodSymbol m, MemberAccessExpressionSyntax access)
    {
        var args = inv.ArgumentList.Arguments;
        var ps = m.Parameters;
        var form = $"string.{m.Name}({string.Join(", ", ps.Select(p => p.Type.ToDisplayString()))})";
        if (ps.Length == 0 || ps[0].Type.SpecialType is not (SpecialType.System_Char or SpecialType.System_String))
            return Unsupported(inv, form);
        var isChar = ps[0].Type.SpecialType == SpecialType.System_Char;
        var hasStart = false;
        for (var i = 1; i < ps.Length; i++)
        {
            if (ps[i].Type.SpecialType == SpecialType.System_Int32 && i == 1 && m.Name == "IndexOf") { hasStart = true; continue; }
            if (ps[i].Type.Name == "StringComparison" && i == ps.Length - 1)
            {
                var cmp = Model(args[i].Expression).GetConstantValue(args[i].Expression);
                if (cmp.HasValue && cmp.Value is int v && v == 4) continue;   // StringComparison.Ordinal
                return Unsupported(inv, $"{form} with a comparison other than the constant StringComparison.Ordinal");
            }
            return Unsupported(inv, form);
        }
        _usesStrSearch = true;
        var s = Expr(access.Expression, top: true);
        var value = Expr(args[0].Expression, top: true);
        var start = hasStart ? Expr(args[1].Expression, top: true) : "0";
        switch (m.Name)
        {
            case "IndexOf": return isChar ? $"es_str_findc({s}, {value}, {start})" : $"es_str_find({s}, {value}, {start})";
            case "LastIndexOf": return isChar ? $"es_str_rfindc({s}, {value})" : $"es_str_rfind({s}, {value})";
            case "Contains": return isChar ? $"(es_str_findc({s}, {value}, 0) >= 0)" : $"(es_str_find({s}, {value}, 0) >= 0)";
            case "StartsWith": return isChar ? $"(es_str_findc({s}, {value}, 0) == 0)" : $"es_str_starts({s}, {value})";
            default: return isChar ? $"es_str_endsc({s}, {value})" : $"es_str_ends({s}, {value})";
        }
    }

    private string Unsupported(SyntaxNode node, string what)
    {
        _diagnostics.Add(Diagnostic.Create(Descriptors.Unsupported, node.GetLocation(), what));
        return "0";
    }

    // ------------------------------------------------------------------ type collection

    private List<INamedTypeSymbol> CollectTypes()
    {
        var result = new List<INamedTypeSymbol>();
        foreach (var tree in _compilation.SyntaxTrees)
        {
            var model = Model(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol sym) continue;
                if (sym.TypeKind == TypeKind.Class && IsAttributeType(sym)) continue;
                if (IsGenericDefinition(sym)) continue;
                if (sym.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum)
                    result.Add(sym);
            }
        }
        return result;
    }

    /// <summary>Generic originals are only templates; the monomorphiser added concrete copies.</summary>
    private static bool IsGenericDefinition(INamedTypeSymbol t)
    {
        for (var c = t; c != null; c = c.ContainingType)
            if (c.IsGenericType) return true;
        return false;
    }

    /// <summary>
    /// The methods to emit: all of the user's own code, plus the library methods it can reach (from
    /// bodies and static field initializers). Library code that is never used (a whole screen API in
    /// a console program) is not emitted, so it neither bloats the executable nor needs its headers.
    /// </summary>
    private readonly List<IMethodSymbol> _staticConstructors = new();
    private bool _thisIsClass;
    private INamedTypeSymbol? _thisType;
    private bool _usesAlloc;
    private bool _usesFmt;
    private bool _usesStrSearch;
    private int _objectTemps;
    private readonly HashSet<ISymbol> _usedSymbols = new(SymbolEqualityComparer.Default);

    private readonly List<IMethodSymbol> _atExit = new();

    private HashSet<IMethodSymbol> ReachableMethods(List<INamedTypeSymbol> types)
    {
        var reachable = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var work = new Stack<SyntaxNode>();

        void Add(IMethodSymbol? m)
        {
            if (m == null || !reachable.Add(m.OriginalDefinition)) return;
            foreach (var r in m.OriginalDefinition.DeclaringSyntaxReferences) work.Push(r.GetSyntax());
            if (m.MethodKind == MethodKind.Constructor && !m.IsStatic && IsHeapClass(m.ContainingType)) AddChainedConstructor(m.OriginalDefinition);
        }

        // A class constructor runs another one that no expression names: the one its ': base(...)' / ': this(...)' calls, or, without
        // one, the nearest explicit parameterless base constructor. Within the program all of them are emitted anyway; a class of the
        // program that derives from an [Extensible] library class (class MyWindow : GtWindow) needs the library's constructor too.
        void AddChainedConstructor(IMethodSymbol ctor)
        {
            var init = ctor.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<ConstructorDeclarationSyntax>().FirstOrDefault()?.Initializer;
            if (init != null && Model(init).GetSymbolInfo(init).Symbol is IMethodSymbol { IsImplicitlyDeclared: false } chained) { Add(chained); return; }
            if (init != null && init.IsKind(SyntaxKind.ThisConstructorInitializer)) return;
            for (var b = BaseClass(ctor.ContainingType); b != null; b = BaseClass(b))
            {
                // Base field initializers run too (ImplicitInits): their expressions are walked like those of any used field.
                foreach (var (member, _, _) in FieldInits(b)) _usedSymbols.Add(member);
                var explicitCtors = b.InstanceConstructors.Where(k => !k.IsImplicitlyDeclared).ToList();
                if (explicitCtors.Count == 0) continue;
                var none = explicitCtors.FirstOrDefault(k => k.Parameters.Length == 0);
                // C# also binds an implicit base() to a constructor whose parameters are all optional; the emitter does not (ImplicitInits).
                if (none == null)
                    Unsupported((SyntaxNode?)init ?? ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? ctor.ContainingType.DeclaringSyntaxReferences.First().GetSyntax(),
                        $"implicit base constructor of '{b.Name}' with optional parameters; call it with ': base(...)' and the arguments");
                else Add(none);
                break;
            }
        }

        foreach (var entry in FindEntryPoints(_compilation, _user)) Add(entry);
        // [AtExit] methods are reached when anything else of their class is (EmitMain calls them).
        var atExit = types.SelectMany(t => t.GetMembers().OfType<IMethodSymbol>())
            .Where(m => m.IsStatic && m.Parameters.Length == 0 && HasAttribute(m, "AtExitAttribute")).ToList();
        // Everything the user wrote is emitted (and checked), used or not; only library code is pruned.
        foreach (var t in types.Where(t => t.DeclaringSyntaxReferences.Any(r => _user.Contains(r.SyntaxTree))))
            foreach (var member in t.GetMembers())
                switch (member)
                {
                    case IMethodSymbol m: Add(m); break;
                    case IFieldSymbol or IPropertySymbol: _usedSymbols.Add(member); break;
                }
        var walkedFields = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        while (true)
        {
            if (work.Count == 0)
            {
                // A static field is only emitted, and its initializer only scanned, once something uses it.
                var more = false;
                foreach (var f in _usedSymbols.ToList())
                {
                    // Instance field and auto-property initializers too: they run in the class's field-init function and may hold lambdas.
                    if (!walkedFields.Add(f)) continue;
                    foreach (var r in f.DeclaringSyntaxReferences)
                    {
                        var decl = r.GetSyntax();
                        if (decl is VariableDeclaratorSyntax { Initializer: { } init }) { work.Push(init); more = true; }
                        else if (decl is PropertyDeclarationSyntax { Initializer: { } pinit }) { work.Push(pinit); more = true; }
                    }
                }
                foreach (var m in atExit)
                    if (!reachable.Contains(m.OriginalDefinition) && ClassUsed(m)) { Add(m); more = true; }
                // An override in a library class runs when the base member is called on an object of that class, which no expression
                // names (BoopsiObject.Create calls the virtual CreateAll, a MUI wrapper overrides it): reach the overrides of reached
                // members in the classes that are constructed (a constructor of the class, or of a class derived from it, is reached).
                foreach (var t in types)
                {
                    if (t.TypeKind != TypeKind.Class || !t.InstanceConstructors.Any(k => reachable.Contains(k.OriginalDefinition))) continue;
                    foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (!m.IsOverride || reachable.Contains(m.OriginalDefinition)) continue;
                        for (var o = m.OverriddenMethod; o != null; o = o.OverriddenMethod)
                            if (reachable.Contains(o.OriginalDefinition)) { Add(m); more = true; break; }
                    }
                }
                if (!more) break;
            }
            if (work.Count == 0) continue;
            var node = work.Pop();
            var model = Model(node);
            // Calls to [Conditional] methods that are compiled out reach nothing: not the method, not their arguments.
            var compiledOut = node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Where(inv => IsCompiledOutCall(inv)).Select(inv => inv.Span).ToList();
            foreach (var n in node.DescendantNodesAndSelf())
            {
                if (compiledOut.Count > 0 && compiledOut.Any(sp => sp.Contains(n.Span))) continue;
                if (n is ForEachStatementSyntax loop)
                {
                    // foreach over the GetEnumerator pattern calls these without naming them.
                    var fi = model.GetForEachStatementInfo(loop);
                    Add(fi.GetEnumeratorMethod);
                    Add(fi.MoveNextMethod);
                    if (fi.CurrentProperty is { } current) { Add(current.GetMethod); _usedSymbols.Add(current); }
                }
                if (n is PatternSyntax pat)
                    foreach (var pm in PatternMembers(model, pat))   // Deconstruct, Length/Count and indexers that patterns call
                        switch (pm)
                        {
                            case IMethodSymbol dm: Add(dm); break;
                            case IPropertySymbol pp: Add(pp.GetMethod); _usedSymbols.Add(pp); break;
                        }
                if (n is not ExpressionSyntax e) continue;
                var info = model.GetSymbolInfo(e);
                switch (info.Symbol)
                {
                    case IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } when IsInlinedLinqLambda(e): break;   // pasted into the loop, no function needed
                    case IMethodSymbol m: Add(m); break;
                    case IPropertySymbol p: Add(p.GetMethod); Add(p.SetMethod); _usedSymbols.Add(p); break;
                    case IFieldSymbol f: _usedSymbols.Add(f); break;
                }
                var conversion = model.GetConversion(e);
                if (conversion.IsUserDefined) Add(conversion.MethodSymbol);
            }
        }
        // Main calls each [AtExit] method of a class the program uses (for the user's own classes: every one, as they are all emitted).
        foreach (var m in atExit)
            if (reachable.Contains(m.OriginalDefinition) && ClassUsed(m)) _atExit.Add(m.OriginalDefinition);
        return reachable;

        bool ClassUsed(IMethodSymbol m) =>
            reachable.Any(r => SymbolEqualityComparer.Default.Equals(r.ContainingType, m.OriginalDefinition.ContainingType) && !HasAttribute(r, "AtExitAttribute"));
    }

    private static bool IsAttributeType(INamedTypeSymbol t)
    {
        for (var b = t.BaseType; b != null; b = b.BaseType)
            if (b.ToDisplayString() == "System.Attribute") return true;
        return false;
    }

    private static List<INamedTypeSymbol> SortStructs(IEnumerable<INamedTypeSymbol> structs)
    {
        var all = structs.ToList();
        var done = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var sorted = new List<INamedTypeSymbol>();

        void Visit(INamedTypeSymbol s)
        {
            if (!done.Add(s)) return;
            foreach (var f in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst && f.AssociatedSymbol == null))
                if (f.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested && all.Contains(nested, SymbolEqualityComparer.Default))
                    Visit(nested);
            foreach (var pr in s.GetMembers().OfType<IPropertySymbol>().Where(pr => !pr.IsStatic && IsAutoProperty(pr)))
                if (pr.Type is INamedTypeSymbol { TypeKind: TypeKind.Struct } nested && all.Contains(nested, SymbolEqualityComparer.Default))
                    Visit(nested);
            sorted.Add(s);
        }
        // (auto-property types count as fields below)

        foreach (var s in all) Visit(s);
        return sorted;
    }

    // ------------------------------------------------------------------ naming

    /// <summary>
    /// Prefix of the C name of every E# type (class, struct, interface, enum; user code and the embedded library alike).
    /// The system headers use bare names such as <c>struct Window</c>, <c>struct Node</c> or <c>struct List</c>, so an E#
    /// class of the same name would clash with them. No identifier in the AmigaOS 3 NDK, libnix or AROS headers starts with
    /// <c>Es_</c>, and the emitter's own helpers use <c>es_</c> (library bases, runtime functions) and <c>ES_</c> (spans,
    /// tuples, delegates, type ids), which C keeps apart because it is case sensitive.
    /// </summary>
    internal const string TypePrefix = "Es_";

    /// <summary>
    /// C name of an E# type: <see cref="TypePrefix"/>, then the namespace and the type (with any containing types), joined with
    /// '_': <c>Cards.Board</c> is <c>Es_Cards_Board</c>, <c>ESharp.Platform.Dos</c> is <c>Es_ESharp_Platform_Dos</c> and a type
    /// in the global namespace is <c>Es_Board</c>. Functions and static fields of a type start with this name too.
    /// </summary>
    private static string TypeName(INamedTypeSymbol t) => TypePrefix + QualifiedName(t);

    /// <summary>Namespace and type, joined with '_', without the prefix: the part of a mangled name that stands for a type.</summary>
    private static string QualifiedName(INamedTypeSymbol t)
    {
        var parts = new List<string>();
        for (INamedTypeSymbol? c = t; c != null; c = c.ContainingType) parts.Insert(0, c.Name);
        for (var ns = t.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace) parts.Insert(0, ns.Name);
        return string.Join("_", parts);
    }

    /// <summary>A type inside a longer C name (overloads, span and tuple typedefs); namespaced, so same-named types stay apart.</summary>
    private static string Mangle(ITypeSymbol t) => t switch
    {
        // string? and string are one C type: the nullable annotation is not part of the name (a '?' is no C identifier).
        INamedTypeSymbol n => n.SpecialType != SpecialType.None ? n.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString() : QualifiedName(n),
        IPointerTypeSymbol p => Mangle(p.PointedAtType) + "Ptr",
        IArrayTypeSymbol a => Mangle(a.ElementType) + "Arr",
        _ => "t",
    };

    private static string FuncName(IMethodSymbol m)
    {
        if (HasAttribute(m, "AmigaLibAttribute") || HasAttribute(m, "NativeAttribute")) return m.Name;
        if (m.MethodKind == MethodKind.LocalFunction && m.ContainingSymbol is IMethodSymbol outer)
            return FuncName(outer) + "_" + m.Name;
        if (m.MethodKind == MethodKind.AnonymousFunction)
        {
            var at = m.DeclaringSyntaxReferences.First();
            uint h = 17;
            foreach (var ch in at.SyntaxTree.FilePath) h = h * 31 + ch;
            return $"ES_lambda_{h:x}_{at.Span.Start}";
        }
        if (m.MethodKind == MethodKind.StaticConstructor) return $"{TypeName(m.ContainingType)}_cctor";
        if (m.MethodKind == MethodKind.Constructor)
        {
            var ctors = m.ContainingType.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).Count();
            var ctorName = $"{TypeName(m.ContainingType)}_new";
            return ctors > 1 ? ctorName + "_" + string.Join("_", m.Parameters.Select(p => Mangle(p.Type))) : ctorName;
        }
        var name = $"{TypeName(m.ContainingType)}_{m.Name}";
        var overloads = m.ContainingType.GetMembers(m.Name).OfType<IMethodSymbol>().Count();
        if (overloads > 1)
            name += "_" + string.Join("_", m.Parameters.Select(p => Mangle(p.Type)));
        if (m.MethodKind == MethodKind.Conversion)
            name += "_" + Mangle(m.ReturnType);
        return name;
    }

    private static string FieldName(IFieldSymbol f) =>
        f.IsStatic || f.ContainingType.TypeKind == TypeKind.Enum
            ? $"{TypeName(f.ContainingType)}_{f.Name}"
            : f.Name;

    // ------------------------------------------------------------------ types

    private string CType(ITypeSymbol t, SyntaxNode at)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_Void: return "void";
            case SpecialType.System_Boolean: return "bool";
            case SpecialType.System_Byte: return "uint8_t";
            case SpecialType.System_SByte: return "int8_t";
            case SpecialType.System_Int16: return "int16_t";
            case SpecialType.System_UInt16: return "uint16_t";
            case SpecialType.System_Int32: return "int32_t";
            case SpecialType.System_UInt32: return "uint32_t";
            case SpecialType.System_Int64: return "int64_t";
            case SpecialType.System_UInt64: return "uint64_t";
            case SpecialType.System_IntPtr: return "intptr_t";     // nint and nuint: pointer-sized (an OS handle or tag value)
            case SpecialType.System_UIntPtr: return "uintptr_t";
            case SpecialType.System_Char: return "uint16_t";
            case SpecialType.System_Single: return "float";
            case SpecialType.System_Double: return "double";
            case SpecialType.System_String: return "const char *";
        }

        switch (t)
        {
            case IFunctionPointerTypeSymbol:
                return "void *";   // only ever the address of an [Interrupt] server, handed to exec
            case IPointerTypeSymbol p:
                var inner = CType(p.PointedAtType, at);
                return inner.EndsWith("*") ? inner + "*" : inner + " *";
            case INamedTypeSymbol { TypeKind: not TypeKind.Delegate } holder when HoldsDelegate(holder):
                // A span, tuple or generic struct (Vec<Action>) of delegates: its typedef would come before the delegate's, and
                // copies of it would share closures with no owner. Not supported yet.
                ReportOnce(at, $"'{holder.ToDisplayString()}': a span, tuple or generic struct that holds delegates (use an array of delegates, or a class with delegate fields)");
                return "int32_t";
            case INamedTypeSymbol { Name: "Span" or "ReadOnlySpan", TypeArguments.Length: 1 } sp when
                sp.ContainingNamespace?.ToDisplayString() == "System":
                return SpanType(sp, at);
            case INamedTypeSymbol { IsTupleType: true } tuple:
                return TupleType(tuple, at);
            case INamedTypeSymbol { TypeKind: TypeKind.Delegate } del:
                return DelegateType(del, at);
            case INamedTypeSymbol cls when IsHeapClass(cls):
                return TypeName(cls) + " *";
            case INamedTypeSymbol iface when IsInterfaceType(iface):
                return TypeName(iface);
            case INamedTypeSymbol { TypeKind: TypeKind.Enum or TypeKind.Struct } n:
                return TypeName(n);
            case IArrayTypeSymbol:
                Unsupported(at, "array type here: arrays are fixed-size locals or static fields; pass them as Span<T>");
                return "int32_t";
        }

        Unsupported(at, $"type {t.ToDisplayString()}");
        return "int32_t";
    }

    /// <summary>C struct for Span&lt;T&gt; / ReadOnlySpan&lt;T&gt;: a pointer and an element count.</summary>
    private string SpanType(INamedTypeSymbol span, SyntaxNode at)
    {
        var elem = span.TypeArguments[0];
        var readOnly = span.Name == "ReadOnlySpan";
        var name = (readOnly ? "ES_ROSpan_" : "ES_Span_") + Mangle(elem);
        if (!_spans.ContainsKey(name))
        {
            var ptr = CType(elem, at);
            // 'T const *' (not 'const T *') so that a string element ('const char *') gives 'const char * const *', not a doubled const.
            _spans[name] = $"typedef struct {{ {ptr}{(readOnly ? " const" : "")} *ptr; int32_t length; }} {name};\n";
        }
        return name;
    }

    /// <summary>
    /// A delegate type (Func, Action, Predicate, a user's <c>delegate</c>) is a function pointer plus an environment pointer.
    /// The function takes the environment first; static methods and lambdas that capture nothing have a null environment,
    /// a lambda that captures locals points at a struct of pointers to them (see <see cref="Lambda"/>).
    /// </summary>
    private string DelegateType(INamedTypeSymbol del, SyntaxNode at)
    {
        var invoke = del.DelegateInvokeMethod!;
        if (invoke.ReturnsByRef || invoke.ReturnsByRefReadonly) Unsupported(at, "delegate type that returns by ref");
        var ps =invoke.Parameters.Select(p => p.RefKind == RefKind.None ? CType(p.Type, at) : CType(p.Type, at) + " *").Prepend("void *").ToList();
        var signature = $"typedef struct {{ {CType(invoke.ReturnType, at)} (*fn)({string.Join(", ", ps)}); void *env; }} @NAME@;\n";
        if (!_delegateNames.TryGetValue(signature, out var name))
        {
            name = "ES_Wfn_" + _delegateNames.Count;   // sorts after the span and tuple typedefs it may mention
            _delegateNames[signature] = name;
            _spans[name] = signature.Replace("@NAME@", name);
        }
        return name;
    }

    private readonly Dictionary<string, string> _delegateNames = new();

    private static bool HoldsDelegate(INamedTypeSymbol t) =>
        t.IsTupleType ? t.TupleElements.Any(e => e.Type.TypeKind == TypeKind.Delegate)
        : t.TypeKind == TypeKind.Struct && t.TypeArguments.Any(a => a.TypeKind == TypeKind.Delegate);

    /// <summary>C struct for a value tuple: fields Item1, Item2, ...</summary>
    private string TupleType(INamedTypeSymbol tuple, SyntaxNode at)
    {
        var elements = tuple.TupleElements;
        var name = "ES_Tuple_" + string.Join("_", elements.Select(e => Mangle(e.Type).Replace('.', '_')));
        if (!_spans.ContainsKey(name))
        {
            _spans[name] = ""; // reserve first: an element type may refer back
            var fields = string.Join(" ", elements.Select((e, i) => $"{CType(e.Type, at)} Item{i + 1};"));
            _spans[name] = $"typedef struct {{ {fields} }} {name};\n";
        }
        return name;
    }

    private static string FieldCName(IFieldSymbol f) => f.ContainingType.IsTupleType ? (f.CorrespondingTupleField ?? f).Name : f.Name;

    private static bool IsSpan(ITypeSymbol? t) =>
        t is INamedTypeSymbol { Name: "Span" or "ReadOnlySpan", TypeArguments.Length: 1 } n &&
        n.ContainingNamespace?.ToDisplayString() == "System";

    private static bool IsSimple(ExpressionSyntax e)
    {
        for (var x = e; ; )
            switch (x)
            {
                case ParenthesizedExpressionSyntax p: x = p.Expression; continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } nf: x = nf.Operand; continue;
                case IdentifierNameSyntax: return true;
                case MemberAccessExpressionSyntax ma: x = ma.Expression; continue;
                default: return false;
            }
    }

    /// <summary>The expression without enclosing parentheses and null-forgiving operators (<c>((x)!)</c> is <c>x</c>).</summary>
    private static ExpressionSyntax Strip(ExpressionSyntax e)
    {
        while (true)
        {
            if (e is ParenthesizedExpressionSyntax p) e = p.Expression;
            else if (e is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } nf) e = nf.Operand;
            else return e;
        }
    }

    /// <summary>Only side-effect-free expressions may be written twice in the output.</summary>
    private bool RequireSimple(ExpressionSyntax e, string what)
    {
        for (var x = e; ; )
        {
            switch (x)
            {
                case ParenthesizedExpressionSyntax p: x = p.Expression; continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } nf: x = nf.Operand; continue;
                case IdentifierNameSyntax: return true;
                case MemberAccessExpressionSyntax ma: x = ma.Expression; continue;
                default:
                    Unsupported(e, $"{what} on a complex expression; assign it to a local first");
                    return false;
            }
        }
    }

    /// <summary>
    /// A C array declaration for a fixed-size E# array: <c>T name[N] = {...}</c>. Arrays live in
    /// the frame or in static storage; there is no heap array.
    /// </summary>
    private string ArrayDeclarator(IArrayTypeSymbol t, string name, ExpressionSyntax? init, SyntaxNode at, bool withItems = true)
    {
        if (t.Rank != 1)
        {
            Unsupported(at, "multi-dimensional array");
            return "int32_t " + name + "[1]";
        }

        InitializerExpressionSyntax? items = null;
        ExpressionSyntax? sizeExpr = null;
        switch (init)
        {
            case ArrayCreationExpressionSyntax ac:
                items = ac.Initializer;
                sizeExpr = ac.Type.RankSpecifiers[0].Sizes[0] is OmittedArraySizeExpressionSyntax ? null : ac.Type.RankSpecifiers[0].Sizes[0];
                break;
            case ImplicitArrayCreationExpressionSyntax iac:
                items = iac.Initializer;
                break;
            case InitializerExpressionSyntax ie:
                items = ie;
                break;
            default:
                Unsupported(init ?? at, "array without a creation expression (use 'new T[N]' or an initializer list)");
                break;
        }

        long length = items?.Expressions.Count ?? 0;
        if (sizeExpr != null)
        {
            var c = Model(sizeExpr).GetConstantValue(sizeExpr);
            if (c.HasValue && c.Value is int n && n > 0) length = n;
            else Unsupported(sizeExpr, "array length that is not a positive constant");
        }
        else if (items == null) Unsupported(init ?? at, "array length");

        var elem = CType(t.ElementType, at);
        var text = (elem.EndsWith("*") ? elem : elem + " ") + $"{name}[{length}]";
        if (!withItems) return text;
        if (items == null) return text + " = {0}";
        var elements = items.Expressions.Select(e => Expr(e, top: true)).ToList();
        if (elements.Count == 0) elements.Add("0");
        return text + " = { " + string.Join(", ", elements) + " }";
    }

    private string Decl(ITypeSymbol t, string name, SyntaxNode at)
    {
        var type = CType(t, at);
        return type.EndsWith("*") ? type + name : type + " " + name;
    }

    // ------------------------------------------------------------------ declarations

    private void EmitEnum(INamedTypeSymbol e)
    {
        var decl = e.DeclaringSyntaxReferences.First().GetSyntax();
        var under = CType(e.EnumUnderlyingType!, decl);
        Line($"typedef {under} {TypeName(e)};");
        Line("enum");
        Line("{");
        _indent++;
        foreach (var m in e.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst))
            Line($"{FieldName(m)} = {Convert.ToString(m.ConstantValue, CultureInfo.InvariantCulture)},");
        _indent--;
        Line("};");
        Line("");
    }

    private void EmitStruct(INamedTypeSymbol s)
    {
        var decl = (StructDeclarationSyntax)s.DeclaringSyntaxReferences.First().GetSyntax();
        foreach (var member in decl.Members)
        {
            var isStatic = member.ChildTokens().Any(t => t.IsKind(SyntaxKind.StaticKeyword));
            var supported = member is FieldDeclarationSyntax or EnumDeclarationSyntax or StructDeclarationSyntax
                or ClassDeclarationSyntax or InterfaceDeclarationSyntax
                or MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax
                or PropertyDeclarationSyntax or IndexerDeclarationSyntax
                || member is ConstructorDeclarationSyntax { Initializer: null or { RawKind: (int)SyntaxKind.ThisConstructorInitializer } };
            if (!supported)
                Unsupported(member, member is ConstructorDeclarationSyntax ? "constructor initializer other than this(...)" : $"{member.Kind()} in struct");
        }

        var name = TypeName(s);
        Line($"typedef struct {name}");
        Line("{");
        _indent++;
        foreach (var f in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst && f.AssociatedSymbol == null))
            if (f.RefKind != RefKind.None) Unsupported(f.DeclaringSyntaxReferences.First().GetSyntax(), "ref field");
            else Line(f.IsFixedSizeBuffer
                ? Decl(((IPointerTypeSymbol)f.Type).PointedAtType, f.Name, decl) + $"[{f.FixedSize}];"
                : Decl(f.Type, f.Name, decl) + ";");
        foreach (var p in s.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && IsAutoProperty(p)))
        {
            if (p.DeclaringSyntaxReferences.First().GetSyntax() is PropertyDeclarationSyntax { Initializer: not null } pd)
                Unsupported(pd, "initializer on an instance property");
            Line(Decl(p.Type, p.Name, decl) + ";");
        }
        if (!s.GetMembers().Any(m => m is IFieldSymbol { IsStatic: false, IsConst: false, AssociatedSymbol: null } || m is IPropertySymbol { IsStatic: false } pp && IsAutoProperty(pp)))
            Line("char ES_empty;");   // C has no empty structs
        _indent--;
        Line($"}} {name};");
        Line("");
    }

    private List<INamedTypeSymbol> CollectInterfaces()
    {
        var result = new List<INamedTypeSymbol>();
        foreach (var tree in _compilation.SyntaxTrees)
        {
            if (!_user.Contains(tree)) continue;
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
                if (Model(tree).GetDeclaredSymbol(decl) is INamedTypeSymbol sym && IsInterfaceType(sym)) result.Add(sym);
        }
        return result;
    }

    private string SlotParams(IMethodSymbol m, SyntaxNode at, bool named)
    {
        var parts = new List<string> { "void *self" };
        if (m.ReturnsByRef || m.ReturnsByRefReadonly) Unsupported(at, "ref return from an interface method");
        for (var k = 0; k < m.Parameters.Length; k++)
        {
            // ref, out and in parameters are pointers, as in an ordinary function: the slot passes the caller's address on.
            var byRef = m.Parameters[k].RefKind != RefKind.None;
            parts.Add(named ? Decl(m.Parameters[k].Type, (byRef ? "*a" : "a") + k, at)
                : byRef ? Decl(m.Parameters[k].Type, "*", at) : CType(m.Parameters[k].Type, at));
        }
        return string.Join(", ", parts);
    }

    /// <summary>The table of virtual functions of a class: the base class's table, then one slot for each virtual member the class introduces.</summary>
    private void EmitCvt(INamedTypeSymbol c)
    {
        var decl = c.DeclaringSyntaxReferences.First().GetSyntax();
        Line($"struct {CvtName(c)}");
        Line("{");
        _indent++;
        if (BaseClass(c) is { } b) Line($"struct {CvtName(b)} es_base;");
        else Line("struct ES_Type ti;");
        foreach (var m in OwnSlots(c))
            Line(Decl(m.ReturnType, $"(*{VSlot(m)})({SlotParams(m, decl, false)})", decl) + ";");
        _indent--;
        Line("};");
        Line("");
    }

    /// <summary>The one table of a class (with a small function per slot that casts the object back) and its type id link to the base class.</summary>
    private void EmitVtInstance(INamedTypeSymbol c, HashSet<IMethodSymbol> reachable)
    {
        var decl = c.DeclaringSyntaxReferences.First().GetSyntax();
        var inits = new List<string>();
        var depth = Depth(c);
        var tiPath = string.Concat(Enumerable.Repeat("es_base.", depth)) + "ti";
        inits.Add($".{tiPath}.base = {(BaseClass(c) is { } bc ? TypeIdOf(bc) : "NULL")}");
        foreach (var def in AllSlots(c))
        {
            var impl = FinalImpl(c, def);
            if (impl == null || !reachable.Contains(impl.OriginalDefinition)) continue;
            var thunk = $"{TypeName(c)}__{VSlot(def)}";
            var args = new List<string> { $"({TypeName(impl.ContainingType)} *)self" };
            for (var k = 0; k < def.Parameters.Length; k++) args.Add("a" + k);
            Line($"static __attribute__((unused)) {Decl(def.ReturnType, $"{thunk}({SlotParams(def, decl, true)})", decl)}");
            Line("{");
            Line($"    {(def.ReturnsVoid ? "" : "return ")}{FuncName(impl)}({string.Join(", ", args)});");
            Line("}");
            var slotPath = string.Concat(Enumerable.Repeat("es_base.", depth - Depth(def.ContainingType))) + VSlot(def);
            inits.Add($".{slotPath} = {thunk}");
        }
        Line($"static __attribute__((unused)) const struct {CvtName(c)} {TypeName(c)}_vt = {{ {string.Join(", ", inits)} }};");
        Line("");
    }

    private void EmitInterface(INamedTypeSymbol i)
    {
        var decl = i.DeclaringSyntaxReferences.First().GetSyntax();
        Line($"struct {TypeName(i)}_vtbl");
        Line("{");
        _indent++;
        foreach (var m in i.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
        {
            if (m.IsGenericMethod) Unsupported(decl, "generic method in an interface");
            Line(Decl(m.ReturnType, $"(*{SlotName(m)})({SlotParams(m, decl, false)})", decl) + ";");
        }
        _indent--;
        Line("};");
        Line("");
    }

    /// <summary>The function table that lets a class be used as an interface, with one small function per method that casts the object back.</summary>
    private void EmitVtable(INamedTypeSymbol i, INamedTypeSymbol c)
    {
        var decl = c.DeclaringSyntaxReferences.First().GetSyntax();
        var slots = new List<string>();
        foreach (var im in i.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
        {
            if (c.FindImplementationForInterfaceMember(im) is not IMethodSymbol impl) { Unsupported(decl, $"'{c.Name}' does not implement '{im.Name}'"); continue; }
            var thunk = $"{TypeName(c)}_{QualifiedName(i)}_{SlotName(im)}";
            var args = new List<string>();
            for (var k = 0; k < im.Parameters.Length; k++) args.Add("a" + k);
            Line($"static __attribute__((unused)) {Decl(im.ReturnType, $"{thunk}({SlotParams(im, decl, true)})", decl)}");
            Line("{");
            Line($"    {(im.ReturnsVoid ? "" : "return ")}{InstanceCall(impl, $"({TypeName(c)} *)self", args)};");
            Line("}");
            slots.Add($".{SlotName(im)} = {thunk}");
        }
        Line($"static __attribute__((unused)) const struct {TypeName(i)}_vtbl {VtableName(i, c)} = {{ {string.Join(", ", slots)} }};");
        Line("");
    }

    private void EmitClass(INamedTypeSymbol c)
    {
        var decl = (ClassDeclarationSyntax)c.DeclaringSyntaxReferences.First().GetSyntax();
        foreach (var member in decl.Members)
        {
            // Nested types are emitted on their own (their C name includes the outer class: Es_Outer_Inner).
            var supported = member is FieldDeclarationSyntax or EnumDeclarationSyntax or StructDeclarationSyntax
                or ClassDeclarationSyntax or InterfaceDeclarationSyntax
                or MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax
                or PropertyDeclarationSyntax or IndexerDeclarationSyntax or ConstructorDeclarationSyntax or EventFieldDeclarationSyntax;
            if (!supported) Unsupported(member, member is EventDeclarationSyntax ? "an event with add/remove accessors; declare it as 'event Action Name;'" : $"{member.Kind()} in a class");
        }
        if (FieldInits(c).Count > 0) _fieldInitClasses.Add(c);

        var name = TypeName(c);
        Line($"struct {name}");
        Line("{");
        _indent++;
        var baseClass = BaseClass(c);
        if (baseClass != null) Line($"{TypeName(baseClass)} es_base;");
        else if (IsPoly(c)) Line($"const struct {CvtName(c)} *vt;");
        foreach (var field in c.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst && f.AssociatedSymbol == null))
            if (field.RefKind != RefKind.None) Unsupported(field.DeclaringSyntaxReferences.First().GetSyntax(), "ref field");
            else if (field.Type is IArrayTypeSymbol arr)
            {
                // A fixed-size array inside the object (new T[N] or an initializer list; the elements are set by the field initializer function).
                var fieldDecl = field.DeclaringSyntaxReferences.First().GetSyntax();
                Line(ArrayDeclarator(arr, field.Name, (fieldDecl as VariableDeclaratorSyntax)?.Initializer?.Value, fieldDecl, withItems: false) + ";");
            }
            else Line(Decl(field.Type, field.Name, decl) + ";");
        foreach (var p in c.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && IsAutoProperty(p)))
            Line(Decl(p.Type, p.Name, decl) + ";");
        foreach (var ev in c.GetMembers().OfType<IEventSymbol>().Where(e => !e.IsStatic && IsFieldLikeEvent(e)))
        {
            _usesClosures = true;
            CType(ev.Type, decl);   // the delegate typedef, used when the event is raised
            Line($"es_event {ev.Name};");   // the handlers (see CEmitter.Closures.cs)
        }
        if (baseClass == null && !IsPoly(c) && !c.GetMembers().Any(m => m is IFieldSymbol { IsStatic: false, IsConst: false, AssociatedSymbol: null } || m is IPropertySymbol { IsStatic: false } pp && IsAutoProperty(pp)
                || m is IEventSymbol { IsStatic: false } pe && IsFieldLikeEvent(pe)))
            Line("char ES_empty;");   // C has no empty structs
        _indent--;
        Line("};");
        Line("");
    }

    /// <summary>Classes with instance field (or auto-property) initializers that need code: they get a <c>NAME_finit(self)</c> function.</summary>
    private readonly List<INamedTypeSymbol> _fieldInitClasses = new();

    /// <summary>
    /// The instance field and auto-property initializers declared in class <paramref name="c"/> (not its bases) that take code: an array
    /// with an element list, or any other value. <c>new T[N]</c> takes none, because a new object is all zeros.
    /// </summary>
    private static List<(ISymbol Member, string Name, ExpressionSyntax Init)> FieldInits(INamedTypeSymbol c)
    {
        var result = new List<(ISymbol, string, ExpressionSyntax)>();
        foreach (var member in c.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false, IsConst: false, AssociatedSymbol: null } f &&
                f.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } fi })
            {
                if (f.Type is IArrayTypeSymbol && ArrayItems(fi.Value) == null) continue;
                result.Add((f, f.Name, fi.Value));
            }
            else if (member is IPropertySymbol { IsStatic: false } p && IsAutoProperty(p) &&
                     p.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax { Initializer: { } pi })
                result.Add((p, p.Name, pi.Value));
        }
        return result;
    }

    private static string FieldInitName(INamedTypeSymbol c) => TypeName(c) + "_finit";

    private void EmitFieldInit(INamedTypeSymbol c)
    {
        Line($"static void {FieldInitName(c)}({TypeName(c)} *{ThisName})");
        Line("{");
        _indent++;
        _this = ThisKind.Pointer;
        _thisType = c;
        _thisIsClass = true;
        foreach (var (member, name, init) in FieldInits(c))
        {
            var place = $"{ThisName}->{MemberPath(c, c, name)}";
            if (member is IFieldSymbol { Type: IArrayTypeSymbol } && ArrayItems(init) is { } items)
            {
                for (var i = 0; i < items.Expressions.Count; i++) Line($"{place}[{i}] = {Expr(items.Expressions[i], top: true)};");
            }
            else Line($"{place} = {Expr(init, top: true)};");
        }
        _this = ThisKind.None;
        _thisType = null;
        _thisIsClass = false;
        _indent--;
        Line("}");
        Line("");
    }

    /// <summary>
    /// What creating an object of <paramref name="c"/> runs when no explicit constructor of <paramref name="c"/> does it: the field initializers
    /// of <paramref name="c"/> (if <paramref name="includeSelf"/>), then those of each base class without constructors, up to the nearest
    /// base with explicit constructors, whose parameterless one runs (it runs its own initializers and the rest of the chain).
    /// </summary>
    private string ImplicitInits(INamedTypeSymbol c, string self, bool includeSelf)
    {
        var sb = new StringBuilder();
        if (includeSelf && _fieldInitClasses.Contains(c, SymbolEqualityComparer.Default)) sb.Append($"{FieldInitName(c)}(({TypeName(c)} *){self}); ");
        for (var b = BaseClass(c); b != null; b = BaseClass(b))
        {
            var explicitCtors = b.InstanceConstructors.Where(k => !k.IsImplicitlyDeclared).ToList();
            if (explicitCtors.Count > 0)
            {
                var none = explicitCtors.FirstOrDefault(k => k.Parameters.Length == 0);
                if (none != null) sb.Append($"{FuncName(none)}_init(({TypeName(b)} *){self}); ");
                break;
            }
            if (_fieldInitClasses.Contains(b, SymbolEqualityComparer.Default)) sb.Append($"{FieldInitName(b)}(({TypeName(b)} *){self}); ");
        }
        return sb.ToString().TrimEnd();
    }

    private void EmitStaticField(IFieldSymbol f)
    {
        var decl = f.DeclaringSyntaxReferences.First().GetSyntax();
        var init = (decl as VariableDeclaratorSyntax)?.Initializer?.Value;
        // An initializer that is not a C constant (a call, new of a class, another field's value) runs at start-up instead.
        var arrayItems = f.Type is IArrayTypeSymbol ? ArrayItems(init) : null;
        var deferred = f.Type is IArrayTypeSymbol ? arrayItems != null && !arrayItems.Expressions.All(IsCConstant) : init != null && !IsCConstant(init);
        var text = f.Type is IArrayTypeSymbol arr
            ? (deferred ? ArrayDeclarator(arr, FieldName(f), init, decl, withItems: false) + " = {0}" : ArrayDeclarator(arr, FieldName(f), init, decl))
            : Decl(f.Type, FieldName(f), decl);
        // Chip RAM is a 68k-only concept in the C dialect: PowerPC systems allocate chip memory
        // at run time instead, so the attribute is a no-op there.
        if (f.IsVolatile) text = f.Type is IArrayTypeSymbol ? "volatile " + text : VolatileDecl(f, decl);   // shared with an interrupt server
        if (HasAttribute(f, "ChipRamAttribute") && IsDefined("M68K"))
            text = "__chip " + text;
        if (deferred) _deferredStatics.Add((f, FieldName(f), init!));
        Line(f.Type is IArrayTypeSymbol || init == null || deferred ? $"static {text};" : $"static {text} = {Expr(init, top: true)};");
    }

    /// <summary>The element list of an array creation (<c>new T[] { ... }</c>, <c>{ ... }</c>), or null for <c>new T[N]</c>.</summary>
    private static InitializerExpressionSyntax? ArrayItems(ExpressionSyntax? init) => init switch
    {
        ArrayCreationExpressionSyntax ac => ac.Initializer,
        ImplicitArrayCreationExpressionSyntax iac => iac.Initializer,
        InitializerExpressionSyntax ie => ie,
        _ => null,
    };

    /// <summary>Static fields and properties whose initializer is not a C constant: assigned at start-up, before the static constructors.</summary>
    private readonly List<(ISymbol Symbol, string Target, ExpressionSyntax Init)> _deferredStatics = new();

    /// <summary>True when the C of <paramref name="e"/> is a constant expression that may initialize a C static (literals, constants, null, default, a struct with constant fields).</summary>
    private bool IsCConstant(ExpressionSyntax e)
    {
        if (Model(e).GetConstantValue(e).HasValue) return true;
        var info = Model(e).GetTypeInfo(e);
        if (Model(e).GetConversion(e).IsUserDefined) return false;
        if (IsInterfaceType(info.ConvertedType) && !IsInterfaceType(info.Type))
            return e is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression };
        switch (e)
        {
            case ParenthesizedExpressionSyntax p: return IsCConstant(p.Expression);
            case CastExpressionSyntax c: return IsCConstant(c.Expression) && Model(c).GetSymbolInfo(c).Symbol is not IMethodSymbol;
            case LiteralExpressionSyntax l: return l.IsKind(SyntaxKind.NullLiteralExpression) || l.IsKind(SyntaxKind.DefaultLiteralExpression);
            case DefaultExpressionSyntax: return true;
            case BaseObjectCreationExpressionSyntax oc:
                var type = Model(oc).GetTypeInfo(oc).Type;
                if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } || IsSpan(type)) return false;
                if (Model(oc).GetSymbolInfo(oc).Symbol is IMethodSymbol { IsImplicitlyDeclared: false }) return false;   // a constructor is a call
                if ((oc.ArgumentList?.Arguments.Count ?? 0) > 0) return false;
                return oc.Initializer == null || oc.Initializer.Expressions.All(x => x is AssignmentExpressionSyntax a && IsCConstant(a.Right));
        }
        return false;
    }

    /// <summary>The start-up assignments of <see cref="_deferredStatics"/>: declaration order, except that a field another one reads is set first.</summary>
    private void EmitDeferredStatics()
    {
        var done = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var byField = new Dictionary<ISymbol, (ISymbol Symbol, string Target, ExpressionSyntax Init)>(SymbolEqualityComparer.Default);
        foreach (var d in _deferredStatics) byField[d.Symbol] = d;
        void Emit((ISymbol Symbol, string Target, ExpressionSyntax Init) d)
        {
            if (!done.Add(d.Symbol)) return;
            foreach (var n in d.Init.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                if (Model(n).GetSymbolInfo(n).Symbol is { } used && byField.TryGetValue(used, out var dep)) Emit(dep);
            if (d.Symbol is IFieldSymbol { Type: IArrayTypeSymbol } && ArrayItems(d.Init) is { } items)
            {
                for (var i = 0; i < items.Expressions.Count; i++) Line($"{d.Target}[{i}] = {Expr(items.Expressions[i], top: true)};");
            }
            else Line($"{d.Target} = {Expr(d.Init, top: true)};");
        }
        foreach (var d in _deferredStatics) Emit(d);
    }

    // volatile on a pointer or reference field must qualify the pointer itself ("T * volatile x"), not what it points at.
    private string VolatileDecl(IFieldSymbol f, SyntaxNode decl)
    {
        var type = CType(f.Type, decl);
        return type.EndsWith("*") ? type + "volatile " + FieldName(f) : "volatile " + type + " " + FieldName(f);
    }

    private void EmitStaticProperty(IPropertySymbol p)
    {
        var decl = (PropertyDeclarationSyntax)p.DeclaringSyntaxReferences.First().GetSyntax();
        var name = $"{TypeName(p.ContainingType)}_{p.Name}";
        var text = Decl(p.Type, name, decl);
        if (decl.Initializer != null && !IsCConstant(decl.Initializer.Value))
        {
            _deferredStatics.Add((p, name, decl.Initializer.Value));
            Line($"static {text};");
            return;
        }
        Line(decl.Initializer == null ? $"static {text};" : $"static {text} = {Expr(decl.Initializer.Value, top: true)};");
    }

    private string Prototype(IMethodSymbol m)
    {
        var node = m.DeclaringSyntaxReferences.First().GetSyntax();
        var parts = new List<string>();
        if (m.MethodKind == MethodKind.AnonymousFunction) parts.Add("void *_env");   // a lambda is called through a delegate, which passes its environment
        if (!m.IsStatic && m.MethodKind is not (MethodKind.Constructor or MethodKind.LocalFunction or MethodKind.AnonymousFunction))
            parts.Add($"{TypeName(m.ContainingType)} *{ThisName}");
        parts.AddRange(m.Parameters.Where(_ => !IsArgsEntry(m)).Select(p => p.RefKind switch
        {
            RefKind.None => Decl(p.Type, p.Name, node),
            RefKind.Ref or RefKind.Out => Decl(p.Type, "*" + p.Name, node),
            RefKind.In or RefKind.RefReadOnlyParameter => Decl(p.Type, "*" + p.Name, node),
            _ => Unsupported(node, "this kind of ref parameter"),
        }));
        var ps = parts.Count == 0 ? "void" : string.Join(", ", parts);
        // A constructor is a function that builds and returns the struct.
        var ret = m.MethodKind == MethodKind.Constructor ? m.ContainingType : m.ReturnType;
        if (m.ReturnsByRef || m.ReturnsByRefReadonly)
        {
            // ref T F(...)  ->  T *F(...): the function returns the address of the place.
            if (m.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) || m.IsVirtual || m.IsAbstract || m.IsOverride || ret is IArrayTypeSymbol)
                Unsupported(node, "ref return from a property, indexer, lambda, virtual or abstract member, or of an array type");
            return Decl(ret, "*" + FuncName(m) + "(" + ps + ")", node);
        }
        return Decl(ret, FuncName(m) + "(" + ps + ")", node);
    }

    /// <summary>A class constructor is two functions: NAME allocates and calls NAME_init, which runs the body on an existing object (so a derived constructor can run it for its base part).</summary>
    private string InitPrototype(IMethodSymbol m)
    {
        var node = m.DeclaringSyntaxReferences.First().GetSyntax();
        var parts = new List<string> { $"{TypeName(m.ContainingType)} *{ThisName}" };
        parts.AddRange(m.Parameters.Select(p => p.RefKind == RefKind.None ? Decl(p.Type, p.Name, node) : Decl(p.Type, "*" + p.Name, node)));
        return $"void {FuncName(m)}_init({string.Join(", ", parts)})";
    }

    /// <summary>The C call of an instance method: virtual members go through the object's function table.</summary>
    private string InstanceCall(IMethodSymbol m, string receiver, IEnumerable<string> args, bool nonVirtual = false)
    {
        var list = args.ToList();
        if (IsHeapClass(m.ContainingType) && IsHier(m.ContainingType))
        {
            if (!nonVirtual && IsDynamic(m) && IsPoly(m.ContainingType))
            {
                var def = RootDef(m);
                var t = $"ES_v{_objectTemps++}";
                var cls = TypeName(def.ContainingType);
                list.Insert(0, t);
                return $"({{ {cls} *{t} = ({cls} *)({receiver}); ((const struct {CvtName(def.ContainingType)} *){t}->{VtPath(def.ContainingType)})->{VSlot(def)}({string.Join(", ", list)}); }})";
            }
            receiver = $"(({TypeName(m.ContainingType)} *)({receiver}))";
        }
        list.Insert(0, receiver);
        return $"{FuncName(m)}({string.Join(", ", list)})";
    }

    private void EmitMethod(IMethodSymbol m)
    {
        var decl = m.DeclaringSyntaxReferences.First().GetSyntax();
        var (block, arrow) = BodyOf(decl);

        LineDirective(decl is MethodDeclarationSyntax md ? md.Identifier.GetLocation() : decl.GetLocation());
        var isCtor = m.MethodKind == MethodKind.Constructor;
        var isClassCtor = isCtor && IsHeapClass(m.ContainingType);
        if (isClassCtor)
        {
            // NAME(params): allocate (setting the class's function table), then run NAME_init on the new object.
            Line(Prototype(m));
            Line("{");
            var created = IsPoly(m.ContainingType) ? $"{TypeName(m.ContainingType)}_alloc()" : $"ES_NEW({TypeName(m.ContainingType)})";
            _usesAlloc = true;
            Line($"    {TypeName(m.ContainingType)} *{ThisName} = {created};");
            Line($"    {FuncName(m)}_init({string.Join(", ", m.Parameters.Select(p => p.Name).Prepend(ThisName))});");
            Line($"    return {ThisName};");
            Line("}");
            Line("");
            Line(InitPrototype(m));
        }
        else Line(Prototype(m));
        Line("{");
        _indent++;
        _thisIsClass = IsHeapClass(m.ContainingType) && !m.IsStatic;
        _thisType = m.ContainingType;
        _this = isCtor && !isClassCtor ? ThisKind.Value : m.IsStatic || m.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction ? ThisKind.None : ThisKind.Pointer;
        _captures.Clear();
        _fnNode = decl;
        _outerFrames = new List<Frame>();
        if (m.MethodKind == MethodKind.AnonymousFunction && decl is LambdaExpressionSyntax heapLam && IsHeapLambda(heapLam))
            HeapLambdaPrologue(heapLam, m);
        else if (m.MethodKind == MethodKind.AnonymousFunction)
        {
            var caps = FindCaptures(decl);
            if (caps.Locals.Count > 0 || caps.UsesThis)
            {
                Line($"struct {EnvName(m)} *_cap = (struct {EnvName(m)} *)_env;");
                for (var ci = 0; ci < caps.Locals.Count; ci++) _captures[caps.Locals[ci]] = $"(*_cap->c{ci}_{caps.Locals[ci].Name})";
                if (caps.UsesThis)
                {
                    Line($"{TypeName(m.ContainingType)} *{ThisName} = _cap->{ThisName};");
                    _this = ThisKind.Pointer;
                }
            }
        }
        EmitFrame(decl);   // the parameters and top-level locals that heap closures capture
        if (isCtor && !isClassCtor) Line($"{CType(m.ContainingType, decl)} {ThisName} = {{0}};");
        if (isCtor && !isClassCtor && decl is ConstructorDeclarationSyntax { Initializer: { } init } &&
            Model(init).GetSymbolInfo(init).Symbol is IMethodSymbol { IsImplicitlyDeclared: false } chained)
            Line($"{ThisName} = {FuncName(chained)}({string.Join(", ", init.ArgumentList.Arguments.Select(a => Argument(a, chained)))});");
        if (isClassCtor)
        {
            // this(...) and base(...) run the other constructor's body on this object; without an initializer the nearest explicit base() runs.
            var initializer = (decl as ConstructorDeclarationSyntax)?.Initializer;
            // C# order: this class's field initializers, then the base constructor, then the body (this(...) leaves them to the other constructor).
            if (initializer is not { RawKind: (int)SyntaxKind.ThisConstructorInitializer } && _fieldInitClasses.Contains(m.ContainingType, SymbolEqualityComparer.Default))
                Line($"{FieldInitName(m.ContainingType)}({ThisName});");
            if (initializer != null && Model(initializer).GetSymbolInfo(initializer).Symbol is IMethodSymbol { IsImplicitlyDeclared: false } other)
            {
                var chainedArgs = initializer.ArgumentList.Arguments.Select(a => Argument(a, other)).Prepend($"({TypeName(other.ContainingType)} *){ThisName}");
                Line($"{FuncName(other)}_init({string.Join(", ", chainedArgs)});");
            }
            // No initializer, or base() naming a base class that has no constructor of its own: the implicit chain.
            else if ((initializer == null || initializer.IsKind(SyntaxKind.BaseConstructorInitializer)) && ImplicitInits(m.ContainingType, ThisName, includeSelf: false) is { Length: > 0 } implicitBase) Line(implicitBase);
        }
        if (IsArgsEntry(m))
        {
            // args: a variable-length array filled from the same argv words as the Args class (so ES_LEN gives the count; none is a zero-length array).
            _natives.Add("es_args_count");
            var an = m.Parameters[0].Name;
            Line($"int32_t es_n_{an} = es_args_count();");
            Line($"const char *{an}[es_n_{an}];");
            Line($"for (int32_t es_i = 0; es_i < es_n_{an}; es_i++) {an}[es_i] = es_args_get(es_i);");
        }
        var asmOpened = BeginSpecialMethod(m, decl);   // [InlineAsm]: the assembly, then the C# body under #else
        if (block != null)
        {
            foreach (var st in block.Statements) Statement(st);
        }
        else if (arrow != null || decl is LambdaExpressionSyntax)
        {
            var body = arrow?.Expression ?? ((LambdaExpressionSyntax)decl).ExpressionBody!;
            LineDirective(body.GetLocation());
            foreach (var h in HoistedDeclarations(new SyntaxNode[] { body })) Line(h);
            var e = Expr(body, top: true);
            Line(m.ReturnsVoid ? e + ";" : "return " + e + ";");
        }
        EndSpecialMethod(asmOpened);
        if (isCtor && !isClassCtor) Line($"return {ThisName};");
        _this = ThisKind.None;
        _thisIsClass = false;
        _thisType = null;
        _captures.Clear();
        _fnNode = null;
        _outerFrames = new List<Frame>();
        _indent--;
        Line("}");
        Line("");
        _lastLineFile = null;
        EmitInterruptServer(m, decl);
        EmitHookFunction(m, decl);
    }

    // The runtime's own fatal errors (index out of range, out of memory) end with exit(20), which skips the code after
    // the entry point; es_fatal first runs the [AtExit] methods through a hook that main installs (once), so
    // for example interrupt servers come off exec's chain before their code is unloaded.
    private void AppendFatalHelper(StringBuilder result)
    {
        // Error messages: stdio's stderr elsewhere. On AROS, dos.library: stderr is posixc.library's, and on hosted i386 AROS
        // (nightly 20261116) a program that has posixc.library open and makes a Zune Application crashes in the C library's exit
        // code (reproduced in plain C), so an E# program does not open posixc.library at all. Only for Amiga targets: the sdl
        // target's C stays free of Amiga headers.
        if (IsDefined("AMIGA"))
        {
            result.AppendLine("#ifdef __AROS__");
            result.AppendLine("#include <proto/dos.h>");
            result.AppendLine("static __attribute__((unused)) void es_errstr(const char *s) { __typeof__(ErrorOutput()) f = ErrorOutput(); if (!f) f = Output(); if (f) { FPuts(f, (CONST_STRPTR)s); Flush(f); } }");
            result.AppendLine("static __attribute__((unused)) void es_errint(long v)");
            result.AppendLine("{");
            result.AppendLine("    char b[24]; int i = 23; unsigned long u = v < 0 ? 0ul - (unsigned long)v : (unsigned long)v;");
            result.AppendLine("    b[i] = 0;");
            result.AppendLine("    do { b[--i] = (char)('0' + u % 10); u /= 10; } while (u != 0);");
            result.AppendLine("    if (v < 0) b[--i] = '-';");
            result.AppendLine("    es_errstr(b + i);");
            result.AppendLine("}");
            result.AppendLine("#else");
        }
        result.AppendLine("static __attribute__((unused)) void es_errstr(const char *s) { fputs(s, stderr); }");
        if (IsDefined("AMIGA")) result.AppendLine("#endif");
        result.AppendLine("static void (*es_exit_hook)(void);");
        result.AppendLine("static void es_fatal(void)");
        result.AppendLine("{");
        result.AppendLine("    void (*h)(void) = es_exit_hook;");
        result.AppendLine("    es_exit_hook = NULL;");
        result.AppendLine("    if (h != NULL) h();");
        result.AppendLine("    exit(20);");
        result.AppendLine("}");
    }

    /// <summary>
    /// The Workbench (Wanderer) start on AROS, in every AROS program (issue #107; in the C of every Amiga target, inside #if __AROS__).
    /// The AROS startup code waits for the WBStartup message on the process's own port, pr_MsgPort, at an offset compiled in from the SDK's struct Process. The x86-64 toolchain
    /// (amigadev/crosstools, libraries from 2020) was built without the SMP field that newer x86-64 AROS (ABIv1, nightly 20261114)
    /// has at the end of struct Task, so its startup waits 48 bytes too early in the Process and crashes (exec RemHead, address
    /// error). So E# keeps the toolchain's Workbench entry out (defining __nowbsupport and WBenchMsg here means its object is not
    /// linked) and adds its own to the PROGRAM_ENTRIES set of aros/startup.h at the same place (-50: before the console window
    /// and command line entries, which look at WBenchMsg). It finds the port at run time: the first MsgPort at or after the
    /// compiled offset whose node type is NT_MSGPORT, whose signal bit is SIGB_DOS and whose task is this one. Then it does what
    /// every Amiga C startup does: argc 0 and argv the message, the program's directory as the current one, and after the
    /// program the message replied under Forbid (so Workbench unloads the program only after it has ended).
    /// </summary>
    private const string ArosWorkbenchStart = @"#if defined(__AROS__)
#include <proto/exec.h>
#include <proto/dos.h>
#include <aros/startup.h>
#include <dos/dosextens.h>
#include <workbench/startup.h>
int __nowbsupport = 1;
struct WBStartup *WBenchMsg = NULL;
static struct MsgPort *es_aros_process_port(void *me)   /* this process */
{
    char *at = (char *)me + offsetof(struct Process, pr_MsgPort);
    for (int d = 0; d <= 128; d += (int)sizeof(void *))
    {
        struct MsgPort *p = (struct MsgPort *)(at + d);
        if (p->mp_Node.ln_Type == NT_MSGPORT && p->mp_SigBit == SIGB_DOS && p->mp_SigTask == me) return p;
    }
    return NULL;
}
static void es_aros_wbstart(struct ExecBase *SysBase)
{
    struct MsgPort *port = Cli() == NULL ? es_aros_process_port(FindTask(NULL)) : NULL;
    BPTR dir = BNULL, old = BNULL;
    if (port != NULL)
    {
        WaitPort(port);
        WBenchMsg = (struct WBStartup *)GetMsg(port);
    }
    if (WBenchMsg != NULL)
    {
        __argc = 0;
        __argv = (char **)WBenchMsg;
        if (WBenchMsg->sm_NumArgs > 0 && WBenchMsg->sm_ArgList != NULL && WBenchMsg->sm_ArgList[0].wa_Lock != BNULL
            && (dir = DupLock(WBenchMsg->sm_ArgList[0].wa_Lock)) != BNULL)
            old = CurrentDir(dir);
    }
    __startup_entries_next();
    if (WBenchMsg != NULL)
    {
        if (dir != BNULL) { CurrentDir(old); UnLock(dir); }
        Forbid();
        ReplyMsg(&WBenchMsg->sm_Message);
    }
}
ADD2SET(es_aros_wbstart, PROGRAM_ENTRIES, -50)
#endif";

    private void EmitMain()
    {
        var entry = FindEntryPoints(_compilation, _user).FirstOrDefault();
        if (entry == null) return;
        if (entry.Parameters.Length != 0 && !IsArgsEntry(entry))
            Unsupported(entry.DeclaringSyntaxReferences.First().GetSyntax(), "entry point parameters other than string[] args");

        if ((_usesBounds || _usesAlloc) && _atExit.Count > 0)
        {
            Line("static void es_run_atexit(void)");
            Line("{");
            foreach (var m in _atExit) Line("    " + FuncName(m) + "();");
            Line("}");
            Line("");
        }
        if (IsDefined("AMIGA")) foreach (var l in ArosWorkbenchStart.Split('\n')) Line(l.TrimEnd('\r'));
        Line(UsesArgs ? "int main(int argc, char **argv)" : "int main(void)");
        Line("{");
        _indent++;
        if (UsesArgs)
        {
            Line("#if defined(__AMIGA__) || defined(__amigaos__) || defined(__amigaos4__) || defined(__MORPHOS__) || defined(__AROS__)");
            Line("if (argc == 0) es_args_from_workbench((struct WBStartup *)argv); else { es_argc = argc; es_argv = argv; }");
            Line("#else");
            Line("es_argc = argc; es_argv = argv;");
            Line("#endif");
        }
        if ((_usesBounds || _usesAlloc) && _atExit.Count > 0) Line("es_exit_hook = es_run_atexit;");
        // Static field initializers that are not C constants, then the static constructors, run once at start-up, in the order the types
        // appear (C# runs them lazily, before a type is first used).
        EmitDeferredStatics();
        foreach (var cctor in _staticConstructors) Line(FuncName(cctor) + "();");
        // [AtExit] methods of the classes the program used run after the entry point returns, in the order they were reached.
        if (entry.ReturnsVoid)
        {
            Line(FuncName(entry) + "();");
            foreach (var m in _atExit) Line(FuncName(m) + "();");
            Line("return 0;");
        }
        else if (_atExit.Count == 0)
        {
            Line("return (int)" + FuncName(entry) + "();");
        }
        else
        {
            Line("int es_rc = (int)" + FuncName(entry) + "();");
            foreach (var m in _atExit) Line(FuncName(m) + "();");
            Line("return es_rc;");
        }
        _indent--;
        Line("}");
    }

    // ------------------------------------------------------------------ statements

    /// <summary>
    /// Variables declared inside an expression (<c>out var x</c>, <c>out int _</c>, <c>out _</c>) are declared in C
    /// just before the statement. Statements that scope them to themselves (if, while, ...) get a block of their own.
    /// </summary>
    private void Statement(StatementSyntax st)
    {
        var hoisted = HoistedDeclarations(st);
        if (hoisted.Count == 0)
        {
            StatementCore(st);
            return;
        }
        // C# lets a variable declared in an 'if' condition ('if (!TryX(out var v)) return; Use(v);') live on in the
        // enclosing block, so an 'if' written directly in a block is not wrapped; loops and switches scope theirs.
        var scoped = st is IfStatementSyntax && st.Parent is not (BlockSyntax or SwitchSectionSyntax) or WhileStatementSyntax or DoStatementSyntax
            or ForStatementSyntax or ForEachStatementSyntax or SwitchStatementSyntax;
        LineDirective(st.GetLocation());
        if (scoped) { Line("{"); _indent++; }
        foreach (var h in hoisted) Line(h);
        StatementCore(st);
        if (scoped) { _indent--; Line("}"); }
    }

    /// <summary>A call statement to a [Conditional] method whose symbol is not defined here (C# rule): no code, and no argument evaluation.</summary>
    private bool IsCompiledOutCall(ExpressionSyntax e)
    {
        if (e is not InvocationExpressionSyntax inv || Model(inv).GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return false;
        var conditions = m.GetAttributes()
            .Where(a => a.AttributeClass is { Name: "ConditionalAttribute" } && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string)
            .Select(a => (string)a.ConstructorArguments[0].Value!).ToList();
        if (conditions.Count == 0) return false;
        var defined = inv.SyntaxTree.Options.PreprocessorSymbolNames;
        return !conditions.Any(c => defined.Contains(c));
    }

    private List<string> HoistedDeclarations(StatementSyntax st) => HoistedDeclarations(HoistRoots(st));

    private static List<SyntaxNode> HoistRoots(StatementSyntax st)
    {
        var roots = new List<SyntaxNode>();
        switch (st)
        {
            case ExpressionStatementSyntax e: roots.Add(e.Expression); break;
            case LocalDeclarationStatementSyntax l: roots.AddRange(l.Declaration.Variables.Where(v => v.Initializer != null).Select(v => (SyntaxNode)v.Initializer!.Value)); break;
            case ReturnStatementSyntax { Expression: { } r }: roots.Add(r); break;
            case IfStatementSyntax i:
                for (StatementSyntax? cur = i; cur is IfStatementSyntax ci; cur = ci.Else?.Statement) roots.Add(ci.Condition);
                break;
            case WhileStatementSyntax w: roots.Add(w.Condition); break;
            case DoStatementSyntax d: roots.Add(d.Condition); break;
            case ForStatementSyntax f:
                if (f.Declaration != null) roots.AddRange(f.Declaration.Variables.Where(v => v.Initializer != null).Select(v => (SyntaxNode)v.Initializer!.Value));
                roots.AddRange(f.Initializers);
                if (f.Condition != null) roots.Add(f.Condition);
                roots.AddRange(f.Incrementors);
                break;
            case ForEachStatementSyntax fe: roots.Add(fe.Expression); break;
            case SwitchStatementSyntax sw: roots.Add(sw.Expression); break;
        }
        return roots;
    }

    private List<string> HoistedDeclarations(IEnumerable<SyntaxNode> roots)
    {
        var lines = new List<string>();
        foreach (var node in roots.SelectMany(r => r.DescendantNodesAndSelf()))
        {
            if (node is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax }) continue;
            // out variables in a when clause are declared by PatternLocalDecls
            if (node is DeclarationExpressionSyntax de0 && de0.Ancestors().OfType<WhenClauseSyntax>().Any()) continue;
            if (node is IdentifierNameSyntax { Identifier.Text: "_" } id0 && id0.Ancestors().OfType<WhenClauseSyntax>().Any()) continue;
            if (node is DeclarationExpressionSyntax decl)
            {
                if (decl.Designation is SingleVariableDesignationSyntax single &&
                    Model(decl).GetDeclaredSymbol(single) is ILocalSymbol local)
                    lines.Add(Decl(local.Type, local.Name, decl) + ";");
                else if (Model(decl).GetTypeInfo(decl).Type is { } t)
                    lines.Add(Decl(t, DiscardName(decl), decl) + ";");
            }
            else if (node is SingleVariableDesignationSyntax pd && pd.Parent is not DeclarationExpressionSyntax &&
                     !pd.Ancestors().OfType<SwitchExpressionArmSyntax>().Any() &&
                     Model(pd).GetDeclaredSymbol(pd) is ILocalSymbol pl)
            {
                lines.Add(Decl(pl.Type, pl.Name, pd) + ";");
            }
            else if (node is IdentifierNameSyntax { Identifier.Text: "_", Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax } } } id &&
                     Model(id).GetSymbolInfo(id).Symbol is IDiscardSymbol discard)
            {
                lines.Add(Decl(discard.Type, DiscardName(id), id) + ";");
            }
        }
        return lines;
    }

    private void StatementCore(StatementSyntax st)
    {
        LineDirective(st.GetLocation());
        switch (st)
        {
            case BlockSyntax b:
                Line("{");
                _indent++;
                EmitFrame(b);
                foreach (var s in b.Statements) Statement(s);
                _indent--;
                Line("}");
                break;

            case UnsafeStatementSyntax u:
                Statement(u.Block);
                break;

            case FixedStatementSyntax fx:
                EmitFixed(fx);
                break;

            case LocalDeclarationStatementSyntax l:
                foreach (var v in l.Declaration.Variables)
                {
                    var sym = (ILocalSymbol)Model(l).GetDeclaredSymbol(v)!;
                    if (sym.Type is IArrayTypeSymbol localArray)
                    {
                        if (sym.RefKind != RefKind.None) { Unsupported(v, "ref local of an array type"); continue; }
                        Line(ArrayDeclarator(localArray, sym.Name, v.Initializer?.Value, l) + ";");
                        continue;
                    }
                    if (sym.RefKind != RefKind.None)
                    {
                        // ref T x = ref place;  ->  T *x = &place;  (uses of x read and write through the pointer)
                        if (v.Initializer == null) { Unsupported(v, "ref local without an initializer"); continue; }
                        Line($"{Decl(sym.Type, "*" + sym.Name, l)} = {Expr(v.Initializer.Value, top: true)};");
                        continue;
                    }
                    if (v.Initializer != null && sym.Type.TypeKind == TypeKind.Delegate) CheckBorrowedCopy(sym, v.Initializer.Value, v);
                    if (_captures.TryGetValue(sym, out var framed))
                    {
                        // Captured by a heap closure: the variable lives in the scope's frame.
                        if (v.Initializer != null) Line($"{framed} = {Expr(v.Initializer.Value, top: true)};");
                        continue;
                    }
                    var text = Decl(sym.Type, sym.Name, l);
                    if (l.IsConst) text = "const " + text;
                    Line(v.Initializer != null
                        ? $"{text} = {Expr(v.Initializer.Value, top: true)};"
                        : text + ";");
                }
                break;

            case ExpressionStatementSyntax e:
                if (IsCompiledOutCall(e.Expression)) break;
                Line(Expr(e.Expression, top: true) + ";");
                break;

            case ReturnStatementSyntax r:
                Line(r.Expression == null
                    ? (_this == ThisKind.Value ? $"return {ThisName};" : "return;")
                    : $"return {Expr(r.Expression, top: true)};");
                break;

            case IfStatementSyntax i:
                EmitIf(i, isElseIf: false);
                break;

            case WhileStatementSyntax w:
                Line($"while ({Cond(Expr(w.Condition, top: true))})");
                Body(w.Statement);
                break;

            case DoStatementSyntax d:
                Line("do");
                Body(d.Statement);
                Line($"while ({Cond(Expr(d.Condition, top: true))});");
                break;

            case ForStatementSyntax f:
                EmitFor(f);
                break;

            case ForEachStatementSyntax fe:
                EmitForEach(fe);
                break;

            case BreakStatementSyntax:
                Line("break;");
                break;

            case ContinueStatementSyntax:
                Line("continue;");
                break;

            case SwitchStatementSyntax sw:
                EmitSwitch(sw);
                break;

            case EmptyStatementSyntax:
                Line(";");
                break;

            case LocalFunctionStatementSyntax lf:
                CheckNoCapture(lf, "local function");
                break; // emitted as a function of its own


            default:
                Unsupported(st, st.Kind().ToString());
                break;
        }
    }

    private void Body(StatementSyntax st)
    {
        if (st is BlockSyntax) Statement(st);
        else
        {
            Line("{");
            _indent++;
            Statement(st);
            _indent--;
            Line("}");
        }
    }

    /// <summary>
    /// <c>fixed (T* p = array_or_span) { ... }</c>: nothing moves in C, so this is just a scoped pointer
    /// to the first element (array) or to the span's data.
    /// </summary>
    private void EmitFixed(FixedStatementSyntax fx)
    {
        Line("{");
        _indent++;
        foreach (var v in fx.Declaration.Variables)
        {
            if (v.Initializer == null) { Unsupported(v, "fixed without an initializer"); continue; }
            var sym = (ILocalSymbol)Model(fx).GetDeclaredSymbol(v)!;
            var init = v.Initializer.Value;
            var initType = Model(fx).GetTypeInfo(init).Type;
            string value;
            if (IsSpan(initType))
                // A ReadOnlySpan's pointer is const in C; C# allows a plain pointer to it in fixed, so cast (no -Wdiscarded-qualifiers).
                value = RequireSimple(init, "fixed over a span") ? $"({CType(sym.Type, fx)})({Expr(init)}.ptr)" : "0";
            else if (initType is IArrayTypeSymbol)
                value = Expr(init);
            else if (initType is IPointerTypeSymbol)
                value = Expr(init, top: true);
            else
            {
                Unsupported(init, "fixed over this kind of expression");
                value = "0";
            }
            Line($"{Decl(sym.Type, sym.Name, fx)} = {value};");
        }
        Statement(fx.Statement);
        _indent--;
        Line("}");
    }

    private void EmitIf(IfStatementSyntax i, bool isElseIf)
    {
        Line($"{(isElseIf ? "else " : "")}if ({Cond(Expr(i.Condition, top: true))})");
        Body(i.Statement);
        if (i.Else == null) return;
        if (i.Else.Statement is IfStatementSyntax nested)
        {
            LineDirective(nested.GetLocation());
            EmitIf(nested, isElseIf: true);
        }
        else
        {
            Line("else");
            Body(i.Else.Statement);
        }
    }

    private void EmitFor(ForStatementSyntax f)
    {
        string init = "";
        if (f.Declaration != null)
        {
            var parts = new List<string>();
            ITypeSymbol? type = null;
            foreach (var v in f.Declaration.Variables)
            {
                var sym = (ILocalSymbol)Model(f).GetDeclaredSymbol(v)!;
                type = sym.Type;
                if (sym.RefKind != RefKind.None) Unsupported(v, "ref local declared in a for statement");
                parts.Add(v.Initializer != null
                    ? $"{sym.Name} = {Expr(v.Initializer.Value, top: true)}"
                    : sym.Name);
            }
            init = CType(type!, f).TrimEnd() + " " + string.Join(", ", parts);
        }
        else
        {
            init = string.Join(", ", f.Initializers.Select(e => Expr(e, top: true)));
        }

        var cond = f.Condition == null ? "" : Expr(f.Condition, top: true);
        var incr = string.Join(", ", f.Incrementors.Select(e => Expr(e, top: true)));
        Line($"for ({init}; {Cond(cond)}; {incr})");
        Body(f.Statement);
    }

    private void EmitForEach(ForEachStatementSyntax fe)
    {
        if (IsLinqSource(fe.Expression)) { EmitLinqForEach(fe); return; }
        var collType = Model(fe).GetTypeInfo(fe.Expression).Type;
        var local = (ILocalSymbol)Model(fe).GetDeclaredSymbol(fe)!;
        if (collType is not IArrayTypeSymbol && !IsSpan(collType))
        {
            EmitPatternForEach(fe, collType, local);
            return;
        }
        if (!RequireSimple(fe.Expression, "foreach")) return;

        var coll = Expr(fe.Expression);
        var i = $"_i{++_temp}";
        string len, elem;
        if (collType is IArrayTypeSymbol)
        {
            _usesLen = true;
            len = $"ES_LEN({coll})";
            elem = $"{coll}[{i}]";
        }
        else
        {
            len = coll + ".length";
            elem = $"{coll}.ptr[{i}]";
        }

        Line($"for (int32_t {i} = 0; {i} < {len}; {i}++)");
        Line("{");
        _indent++;
        Line(local.RefKind == RefKind.None
            ? Decl(local.Type, local.Name, fe) + $" = {elem};"
            : Decl(local.Type, "*" + local.Name, fe) + $" = ({CType(local.Type, fe)} *)&{elem};");   // foreach (ref var x in span); the cast drops a read-only span's const
        Statement(fe.Statement);
        _indent--;
        Line("}");
    }

    /// <summary>
    /// foreach over a type with the GetEnumerator pattern (a struct or class GetEnumerator returning a struct or class
    /// enumerator with MoveNext and Current), lowered without any allocation:
    /// <c>{ E e = GetEnumerator(&amp;coll); while (MoveNext(&amp;e)) { T x = Current(&amp;e); body } }</c>.
    /// </summary>
    private void EmitPatternForEach(ForEachStatementSyntax fe, ITypeSymbol? collType, ILocalSymbol local)
    {
        var info = Model(fe).GetForEachStatementInfo(fe);
        var get = info.GetEnumeratorMethod;
        var move = info.MoveNextMethod;
        var cur = info.CurrentProperty;
        if (collType == null || get == null || move == null || cur == null || info.IsAsynchronous)
        {
            Unsupported(fe, "foreach over anything but an array, a span or a type with a GetEnumerator method");
            return;
        }
        if (get.IsExtensionMethod || get.IsStatic || get.MethodKind != MethodKind.Ordinary || IsInterfaceType(collType) || IsInterfaceType(get.ContainingType))
        {
            Unsupported(fe, "foreach through an extension or interface GetEnumerator (use a struct or class with its own GetEnumerator)");
            return;
        }
        var enumType = get.ReturnType;
        if (enumType is not INamedTypeSymbol { TypeKind: TypeKind.Struct } && !IsHeapClass(enumType))
        {
            Unsupported(fe, "foreach whose enumerator is not a struct or class (an interface enumerator would be boxed)");
            return;
        }
        // A non-sealed class enumerator gets a Dispose from Roslyn for a possible IDisposable subclass; only an enumerator that has one itself matters.
        if (info.DisposeMethod != null && (SymbolEqualityComparer.Default.Equals(info.DisposeMethod.ContainingType, enumType) ||
            enumType.AllInterfaces.Any(i => i.Name == "IDisposable")))
        {
            Unsupported(fe, "foreach over an enumerator with Dispose (E# does not call it; remove Dispose or loop with MoveNext)");
            return;
        }
        if (get.ReturnsByRef || get.ReturnsByRefReadonly || cur.ReturnsByRef || cur.ReturnsByRefReadonly || local.RefKind != RefKind.None)
        {
            Unsupported(fe, "foreach by ref over an enumerator");
            return;
        }
        if (!info.ElementConversion.IsIdentity && !(info.ElementConversion.IsImplicit && info.ElementConversion.IsNumeric))
        {
            Unsupported(fe, "foreach with a conversion of the element (declare the loop variable with the enumerator's type)");
            return;
        }

        // The collection: a class reference or a variable is used in place; any other value (a call's result) is copied first.
        var core = fe.Expression;
        while (core is ParenthesizedExpressionSyntax pe) core = pe.Expression;
        var n = ++_temp;
        Line("{");
        _indent++;
        string? receiver;
        if (IsRef(collType) || core is ThisExpressionSyntax || IsAddressable(core))
            receiver = ReceiverOf(fe.Expression, fe, "GetEnumerator", "foreach");
        else
        {
            Line($"{Decl(collType, $"_c{n}", fe)} = {Expr(fe.Expression, top: true)};");
            receiver = $"&_c{n}";
        }
        if (receiver != null)
        {
            var e = $"_e{n}";
            var eIsClass = IsHeapClass(enumType);
            var eAddr = eIsClass ? e : "&" + e;
            Line($"{Decl(enumType, e, fe)} = {InstanceCall(get, receiver, System.Array.Empty<string>())};");
            Line($"while ({InstanceCall(move, eAddr, System.Array.Empty<string>())})");
            Line("{");
            _indent++;
            string current;
            if (IsAutoProperty(cur))
                current = eIsClass ? $"{e}->{MemberPath(enumType, cur.ContainingType, cur.Name)}" : $"{e}.{cur.Name}";
            else if (cur.GetMethod == null)
                current = Unsupported(fe, "enumerator Current without a getter");
            else
                current = InstanceCall(cur.GetMethod, eAddr, System.Array.Empty<string>());
            if (!info.ElementConversion.IsIdentity) current = $"(({CType(local.Type, fe)}){current})";
            Line(Decl(local.Type, local.Name, fe) + $" = {current};");
            Line($"(void){local.Name};");   // a loop that only counts does not read it
            Statement(fe.Statement);
            _indent--;
            Line("}");
        }
        _indent--;
        Line("}");
    }

    private void EmitSwitch(SwitchStatementSyntax sw)
    {
        if (sw.Sections.SelectMany(s => s.Labels).Any(l => l is CasePatternSwitchLabelSyntax ||
                l is CaseSwitchLabelSyntax c && Model(c).GetSymbolInfo(c.Value).Symbol is INamedTypeSymbol))
        {
            EmitPatternSwitch(sw);   // CEmitter.Patterns.cs
            return;
        }
        if (Model(sw).GetTypeInfo(sw.Expression).Type?.SpecialType == SpecialType.System_String)
        {
            EmitStringSwitch(sw);
            return;
        }
        Line($"switch ({Expr(sw.Expression, top: true)})");
        Line("{");
        _indent++;
        foreach (var section in sw.Sections)
        {
            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case CaseSwitchLabelSyntax c: Line($"case {Expr(c.Value, top: true)}:"); break;
                    case DefaultSwitchLabelSyntax: Line("default:"); break;
                    default: Unsupported(label, "pattern case label"); break;
                }
            }
            Line("{");
            _indent++;
            foreach (var s in section.Statements) Statement(s);
            _indent--;
            Line("}");
        }
        _indent--;
        Line("}");
    }

    /// <summary>
    /// <c>switch</c> on a string: C cannot switch on text, so the subject is matched against each case label in order (by the
    /// characters; <c>case null</c> matches null) giving the label's number, and a C switch on that number keeps break and fall-out as they are.
    /// </summary>
    private void EmitStringSwitch(SwitchStatementSyntax sw)
    {
        var subject = "es_ss" + _patternTemp++;
        var tests = new List<string>();
        var numbers = new Dictionary<CaseSwitchLabelSyntax, int>();
        foreach (var label in sw.Sections.SelectMany(s => s.Labels).OfType<CaseSwitchLabelSyntax>())
        {
            numbers[label] = tests.Count;
            tests.Add(IsNullConstant(label.Value) ? $"{subject} == NULL" : StrEq(subject, Expr(label.Value, top: true)));
        }
        var pick = new StringBuilder();
        for (var i = 0; i < tests.Count; i++) pick.Append($"{tests[i]} ? {i} : ");
        pick.Append("-1");
        Line("{");
        _indent++;
        Line($"const char *{subject} = {Expr(sw.Expression, top: true)};");
        if (tests.Count == 0) Line($"(void){subject};");
        Line($"switch ({pick})");
        Line("{");
        _indent++;
        foreach (var section in sw.Sections)
        {
            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case CaseSwitchLabelSyntax c: Line($"case {numbers[c]}:"); break;
                    case DefaultSwitchLabelSyntax: Line("default:"); break;
                    default: Unsupported(label, "pattern case label"); break;
                }
            }
            Line("{");
            _indent++;
            foreach (var s in section.Statements) Statement(s);
            _indent--;
            Line("}");
        }
        _indent--;
        Line("}");
        _indent--;
        Line("}");
    }

    // ------------------------------------------------------------------ expressions

    private string Expr(ExpressionSyntax e, bool top = false)
    {
        // x! only silences a nullable warning: no code. Parentheses add none either (the emitter brackets what it needs).
        // The operand carries the same converted type, so an implicit conversion is applied once, on the operand,
        // and not a second time on the '!' or '( )' node.
        if (e is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } nf)
            return Expr(nf.Operand, top);
        if (e is ParenthesizedExpressionSyntax paren)
            return Expr(paren.Expression, top);

        var info = Model(e).GetTypeInfo(e);

        // 'null' where a span is expected is the empty span (the compiler sees it as the array -> span operator).
        if (e is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression) &&
            info.ConvertedType is INamedTypeSymbol nullSpan && IsSpan(nullSpan))
            return $"(({SpanType(nullSpan, e)}){{ NULL, 0 }})";

        // null where an interface or delegate is expected is an empty value.
        if (e is LiteralExpressionSyntax nl && nl.IsKind(SyntaxKind.NullLiteralExpression) && (IsInterfaceType(info.ConvertedType) || info.ConvertedType?.TypeKind == TypeKind.Delegate))
            return $"(({CType(info.ConvertedType!, e)}){{0}})";

        var text = ExprCore(e, top);

        // A class converted to an interface it implements: pair the object with that class's function table.
        if (IsInterfaceType(info.ConvertedType) && info.Type != null && !IsInterfaceType(info.Type))
        {
            if (info.Type is INamedTypeSymbol cls && IsHeapClass(cls))
                return $"(({CType(info.ConvertedType!, e)}){{ (void *){text}, &{VtableName((INamedTypeSymbol)info.ConvertedType!, cls)} }})";
            return Unsupported(e, "converting a struct to an interface (boxing); only classes can be used as interface values");
        }

        // A derived class used where its base class is expected: the base part is the first member, so the pointer is the same, only its type changes.
        if (info.Type is INamedTypeSymbol upFrom && info.ConvertedType is INamedTypeSymbol upTo && IsHeapClass(upFrom) && IsHeapClass(upTo)
            && !SymbolEqualityComparer.Default.Equals(upFrom, upTo) && IsBaseOrSame(upFrom, upTo))
            return $"(({TypeName(upTo)} *)({text}))";

        // Implicit user-defined conversion (operator implicit) written in E# source: call the conversion function.
        var conversion = Model(e).GetConversion(e);
        if (conversion.IsUserDefined && conversion.MethodSymbol is { } userConv && e is not CastExpressionSyntax &&
            userConv.Locations.Any(l => l.IsInSource))
            return $"{FuncName(userConv)}({text})";

        // Implicit array -> Span and Span -> ReadOnlySpan conversions.
        if (info.ConvertedType is INamedTypeSymbol target && IsSpan(target))
        {
            if (info.Type is IArrayTypeSymbol && RequireSimple(e, "array to span conversion"))
            {
                _usesLen = true;
                return $"(({SpanType(target, e)}){{ {text}, ES_LEN({text}) }})";
            }
            if (IsSpan(info.Type) && info.Type!.Name != target.Name)
            {
                var to = SpanType(target, e);
                var from = SpanType((INamedTypeSymbol)info.Type, e);
                var helper = "ES_ToReadOnly_" + Mangle(target.TypeArguments[0]);
                _spans[helper] = $"static inline {to} {helper}({from} s) {{ return ({to}){{ s.ptr, s.length }}; }}\n";
                return $"{helper}({text})";
            }
        }
        return text;
    }

    private string ExprCore(ExpressionSyntax e, bool top)
    {
        switch (e)
        {
            // Reached only from callers that want the value without its implicit conversion (Expr unwraps these itself).
            case ParenthesizedExpressionSyntax p:
                return ExprCore(p.Expression, top);
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } nf:
                return ExprCore(nf.Operand, top);

            case LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression }:
                return DefaultValue(e);

            case LiteralExpressionSyntax lit:
                return Literal(lit);

            case IdentifierNameSyntax id:
                return Name(id);

            case InterpolatedStringExpressionSyntax interp:
                return Interpolation(interp);

            case LambdaExpressionSyntax lam:
                return Lambda(lam);

            case ConditionalAccessExpressionSyntax ca:
                return ConditionalInvoke(ca) ?? Unsupported(ca, "?. other than x?.Invoke(...) on an event or a delegate");

            case QueryExpressionSyntax query:
                return Unsupported(query, "a query whose result is kept: E# builds no sequences, so consume it right away with foreach or a call such as (query).Sum()");

            case MemberAccessExpressionSyntax ma:
                return MemberAccess(ma);

            case ElementAccessExpressionSyntax ea:
                return ElementAccess(ea);

            case SizeOfExpressionSyntax so:
            {
                var constant = Model(so).GetConstantValue(so);
                if (constant.HasValue) return ConstantLiteral(constant.Value!, Model(so).GetTypeInfo(so).Type!);
                var sized = Model(so).GetTypeInfo(so.Type).Type!;
                return $"((int32_t)sizeof({CType(sized, so)}))";
            }

            case BaseExpressionSyntax when _thisIsClass && _this == ThisKind.Pointer:
                return $"(({TypeName(BaseClass(_thisType!)!)} *){ThisName})";
            case ThisExpressionSyntax:
                return ThisValue(e);

            case InvocationExpressionSyntax inv:
            {
                // A compiled-out [Conditional] call used as an expression body, lambda body or for incrementor.
                if (IsCompiledOutCall(inv)) return "(void)0";
                // A ref-returning method returns a pointer; used as a value it is dereferenced (and stays an lvalue).
                var call = Invocation(inv);
                return ReturnsRef(inv) ? $"(*{call})" : call;
            }

            case RefExpressionSyntax re:
                // Only where an address is wanted (ref local initializer, return ref, x = ref y); a ref conditional used as a value is handled above.
                if (re.Parent is ConditionalExpressionSyntax) return Unsupported(re, "ref here");
                return RefAddress(re.Expression);

            case BinaryExpressionSyntax b:
                return Binary(b, top);

            case PrefixUnaryExpressionSyntax u:
                return Prefix(u);

            case PostfixUnaryExpressionSyntax pu:
                if (WrittenProperty(pu.Operand) is { } pp)
                    return PropertyStep(pp, pu.Operand, pu, pu.IsKind(SyntaxKind.PostIncrementExpression));
                return Expr(pu.Operand) + pu.OperatorToken.Text;

            case AssignmentExpressionSyntax a:
            {
                if (a.IsKind(SyntaxKind.SimpleAssignmentExpression) && (a.Left is TupleExpressionSyntax || a.Left is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax }))
                    return Deconstruction(a);
                if (a.Right is RefExpressionSyntax && IsRefVariable(Model(a).GetSymbolInfo(a.Left).Symbol) is { } rebound)
                {
                    // r = ref other;  points the ref local somewhere else.
                    var rs = $"{rebound.Name} = {Expr(a.Right, top: true)}";
                    return top ? rs : $"({rs})";
                }
                if (a.IsKind(SyntaxKind.CoalesceAssignmentExpression)) return CoalesceAssignment(a);
                if (Model(a).GetSymbolInfo(a.Left).Symbol is IEventSymbol assignedEvent) return EventAssignment(a, assignedEvent);
                if (a.IsKind(SyntaxKind.SimpleAssignmentExpression) && Model(a).GetSymbolInfo(a.Left).Symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } assignedLocal)
                    CheckBorrowedCopy(assignedLocal, a.Right, a);
                if (WrittenProperty(a.Left) is { } wp) return PropertyAssignment(wp, a);
                if (a.Kind() != SyntaxKind.SimpleAssignmentExpression &&
                    Model(a).GetSymbolInfo(a).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } op)
                {
                    if (!RequireSimple(a.Left, "compound assignment with an operator")) return "0";
                    var l = Expr(a.Left);
                    var call = $"{l} = {FuncName(op)}({l}, {Expr(a.Right, top: true)})";
                    return top ? call : $"({call})";
                }
                var s = $"{Expr(a.Left)} {a.OperatorToken.Text} {Expr(a.Right)}";
                return top ? s : $"({s})";
            }

            case TupleExpressionSyntax te:
            {
                var tt = Model(te).GetTypeInfo(te).ConvertedType as INamedTypeSymbol;
                if (tt is not { IsTupleType: true }) tt = Model(te).GetTypeInfo(te).Type as INamedTypeSymbol;
                if (tt is not { IsTupleType: true }) return Unsupported(te, "tuple without a type");
                return $"(({TupleType(tt, te)}){{ {string.Join(", ", te.Arguments.Select(a => Expr(a.Expression, top: true)))} }})";
            }

            case IsPatternExpressionSyntax ip:
                return IsPattern(ip);

            case SwitchExpressionSyntax swe:
                return SwitchExpression(swe);

            case ConditionalExpressionSyntax { WhenTrue: RefExpressionSyntax, WhenFalse: RefExpressionSyntax } rc:
                return $"(*{RefAddress(rc)})";   // c ? ref a : ref b used as a value: a C lvalue

            case ConditionalExpressionSyntax c:
            {
                var s = $"{Expr(c.Condition)} ? {Expr(c.WhenTrue)} : {Expr(c.WhenFalse)}";
                return top ? s : $"({s})";
            }

            case CastExpressionSyntax cast:
            {
                if (Model(cast).GetSymbolInfo(cast).Symbol is IMethodSymbol { MethodKind: MethodKind.Conversion } conv)
                    return $"{FuncName(conv)}({Expr(cast.Expression, top: true)})";
                var t = Model(cast).GetTypeInfo(cast.Type).Type!;
                return $"(({CType(t, cast)}){Expr(cast.Expression)})";
            }

            case DefaultExpressionSyntax:
                return DefaultValue(e);

            case ObjectCreationExpressionSyntax oc:
                return ObjectCreation(oc);

            case ImplicitObjectCreationExpressionSyntax io:
                Unsupported(io, "target-typed new");
                return "0";

            default:
                Unsupported(e, e.Kind().ToString());
                return "0";
        }
    }

    private string Prefix(PrefixUnaryExpressionSyntax u)
    {
        if (FunctionAddress(u) is { } address) return address;   // &M of an [Interrupt] method
        var op = u.OperatorToken.Text;
        if ((u.IsKind(SyntaxKind.PreIncrementExpression) || u.IsKind(SyntaxKind.PreDecrementExpression)) &&
            WrittenProperty(u.Operand) is { } pp)
            return PropertyStep(pp, u.Operand, u, u.IsKind(SyntaxKind.PreIncrementExpression));
        if (Model(u).GetSymbolInfo(u).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } user)
        {
            if (u.IsKind(SyntaxKind.PreIncrementExpression) || u.IsKind(SyntaxKind.PreDecrementExpression))
            {
                Unsupported(u, "++/-- on an operator type");
                return "0";
            }
            return $"{FuncName(user)}({Expr(u.Operand, top: true)})";
        }
        return $"{op}{Expr(u.Operand)}";
    }

    private string Binary(BinaryExpressionSyntax b, bool top)
    {
        if (b.IsKind(SyntaxKind.IsExpression) || b.IsKind(SyntaxKind.AsExpression))
        {
            // 'x is K' with a named constant K is a constant pattern, not a type test (the parser cannot tell them apart).
            if (Model(b).GetSymbolInfo(b.Right).Symbol is not ITypeSymbol && Model(b).GetConstantValue(b.Right).HasValue)
                return NamedConstantIs(b);
            var from = Model(b).GetTypeInfo(b.Left).Type as INamedTypeSymbol;
            var target = Model(b).GetTypeInfo(b.Right).Type as INamedTypeSymbol;
            if (from == null || target == null || !CanTypeTest(from, target))
                return Unsupported(b, b.Kind() + " (only to a class, from a class or an interface)");
            var tmp = "es_t" + _patternTemp++;
            var isTest = TypeIs(tmp, from, target, b);
            return b.IsKind(SyntaxKind.IsExpression)
                ? $"({{ {CType(from, b)} {tmp} = {Expr(b.Left, top: true)}; {isTest}; }})"
                : $"({{ {CType(from, b)} {tmp} = {Expr(b.Left, top: true)}; ({isTest}) ? {TypeAs(tmp, from, target)} : ({TypeName(target)} *)NULL; }})";
        }

        if (b.IsKind(SyntaxKind.CoalesceExpression)) return Coalesce(b);

        var isEquality = b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression);
        var leftType = Model(b).GetTypeInfo(b.Left).Type;
        var rightType = Model(b).GetTypeInfo(b.Right).Type;
        if (isEquality && (leftType is INamedTypeSymbol { IsTupleType: true } || rightType is INamedTypeSymbol { IsTupleType: true }))
            return Unsupported(b, "== and != on tuples; compare the elements");

        // string == string compares the characters (C# semantics), not the addresses; against the null constant it is a pointer test.
        if (isEquality && leftType?.SpecialType == SpecialType.System_String && rightType?.SpecialType == SpecialType.System_String
            && !IsNullConstant(b.Left) && !IsNullConstant(b.Right))
        {
            if (Model(b).GetConstantValue(b) is { HasValue: true, Value: bool folded }) return folded ? "true" : "false";   // constant: also valid in a static initializer
            var eq = StrEq(Expr(b.Left, top: true), Expr(b.Right, top: true));
            return b.IsKind(SyntaxKind.EqualsExpression) ? eq : $"(!{eq})";
        }

        if (Model(b).GetSymbolInfo(b).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } user)
        {
            // A record's == and != are made by the C# compiler (member-wise, strings by characters); E# has no body to call for them.
            if (user.IsImplicitlyDeclared || !user.Locations.Any(l => l.IsInSource))
                return Unsupported(b, $"operator {b.OperatorToken.Text} that the C# compiler generates (records); write the operator yourself");
            return $"{FuncName(user)}({Expr(b.Left, top: true)}, {Expr(b.Right, top: true)})";
        }

        var type = Model(b).GetTypeInfo(b).Type;
        if (type?.SpecialType == SpecialType.System_String)
        {
            var folded = Model(b).GetConstantValue(b);
            if (folded.HasValue && folded.Value is string constant) return CString(constant);
            if (!b.IsKind(SyntaxKind.AddExpression)) return Unsupported(b, "string operator");
            var leaves = new List<ExpressionSyntax>();
            void Flatten(ExpressionSyntax x)
            {
                if (x is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } inner && Model(inner).GetTypeInfo(inner).Type?.SpecialType == SpecialType.System_String
                    && !Model(inner).GetConstantValue(inner).HasValue)
                {
                    Flatten(inner.Left);
                    Flatten(inner.Right);
                }
                else leaves.Add(x);
            }
            Flatten(b);
            return FormatParts(b, leaves.Select(l => (l, (ExpressionSyntax?)null, (string?)null)).ToList(), leaves.Select(l => ((string?)null, (string?)null)).ToList());
        }

        static bool IsDelegate(ITypeSymbol? t) => t?.TypeKind == TypeKind.Delegate;
        if (isEquality && (IsNullConstant(b.Left) || IsNullConstant(b.Right)) &&
            Model(b).GetSymbolInfo(IsNullConstant(b.Left) ? b.Right : b.Left).Symbol is IEventSymbol testedEvent)
        {
            // An event is null when it has no handlers.
            var place = EventPlace(IsNullConstant(b.Left) ? b.Right : b.Left, testedEvent);
            return place == null ? "0" : $"(({place}).live {(b.IsKind(SyntaxKind.EqualsExpression) ? "==" : "!=")} 0)";
        }
        if (b.OperatorToken.Text is "==" or "!=" &&
            (IsInterfaceType(Model(b).GetTypeInfo(b.Left).Type) || IsInterfaceType(Model(b).GetTypeInfo(b.Right).Type) ||
             IsDelegate(Model(b).GetTypeInfo(b.Left).Type) || IsDelegate(Model(b).GetTypeInfo(b.Right).Type)))
        {
            // Interface values are equal when they hold the same object, delegates when they hold the same function (and environment); null is an empty value.
            string Side(ExpressionSyntax x) => x is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.NullLiteralExpression) ? "NULL"
                : IsInterfaceType(Model(x).GetTypeInfo(x).Type) ? $"{Expr(x)}.obj" : IsDelegate(Model(x).GetTypeInfo(x).Type) ? $"{Expr(x)}.fn" : Expr(x);
            var cmp = $"{Side(b.Left)} {b.OperatorToken.Text} {Side(b.Right)}";
            return top ? cmp : $"({cmp})";
        }

        // Two class references of different (base and derived) classes: the same object has the same address, so compare the addresses
        // as untyped pointers (C would warn about comparing distinct pointer types).
        if (isEquality && leftType is INamedTypeSymbol lc && rightType is INamedTypeSymbol rc && IsHeapClass(lc) && IsHeapClass(rc)
            && !SymbolEqualityComparer.Default.Equals(lc, rc))
        {
            var cmp = $"(const void *)({Expr(b.Left, top: true)}) {b.OperatorToken.Text} (const void *)({Expr(b.Right, top: true)})";
            return top ? cmp : $"({cmp})";
        }

        var s = $"{Expr(b.Left)} {b.OperatorToken.Text} {Expr(b.Right)}";
        return top ? s : $"({s})";
    }

    /// <summary>True for the null literal and other constants whose value is null (<c>default</c>, <c>(string)null</c>, a null const).</summary>
    // 'x is K' where K names a constant: strings compare by characters (a null constant is a pointer test),
    // numbers, chars, bools and enums of the same type compare with ==; anything else is not supported.
    private string NamedConstantIs(BinaryExpressionSyntax b)
    {
        var subjectType = Model(b).GetTypeInfo(b.Left).Type;
        var constType = Model(b).GetTypeInfo(b.Right).Type;
        if (subjectType?.SpecialType == SpecialType.System_String)
            return IsNullConstant(b.Right) ? $"({Expr(b.Left, top: true)} == NULL)" : StrEq(Expr(b.Left, top: true), Expr(b.Right, top: true));
        var simple = subjectType != null && (subjectType.TypeKind == TypeKind.Enum ||
            subjectType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double);
        if (!simple || !SymbolEqualityComparer.Default.Equals(subjectType, constType))
            return Unsupported(b, "'is' with a named constant on this type");
        return $"({Expr(b.Left, top: true)} == {Expr(b.Right, top: true)})";
    }

    private bool IsNullConstant(ExpressionSyntax e)
    {
        var c = Model(e).GetConstantValue(e);
        return c.HasValue && c.Value == null;
    }

    /// <summary>A C condition: the strings <paramref name="left"/> and <paramref name="right"/> are equal as C# compares them (characters; null equals only null).</summary>
    private string StrEq(string left, string right)
    {
        _usesStreq = true;
        return $"es_streq({left}, {right})";
    }

    /// <summary><c>a ?? b</c> for references (classes, strings, interface values, delegates): a is evaluated once; b only when a is null.</summary>
    private string Coalesce(BinaryExpressionSyntax b)
    {
        var info = Model(b).GetTypeInfo(b);
        var type = info.Type ?? info.ConvertedType;
        var leftType = Model(b).GetTypeInfo(b.Left).Type;
        if (type == null || leftType == null) return Unsupported(b, "?? without a type");
        var tmp = "es_c" + _patternTemp++;
        string test, left, right;
        if (IsInterfaceType(type) && IsInterfaceType(leftType) || type.TypeKind == TypeKind.Delegate && leftType.TypeKind == TypeKind.Delegate)
        {
            test = $"{tmp}{(IsInterfaceType(type) ? ".obj" : ".fn")} != NULL";
            left = Expr(b.Left, top: true);
            right = Expr(b.Right, top: true);
        }
        else if (IsHeapClass(type) && IsHeapClass(leftType))
        {
            // Casts keep C quiet when one side is a derived class of the result type.
            test = $"{tmp} != NULL";
            left = $"({CType(type, b)})({Expr(b.Left, top: true)})";
            right = $"({CType(type, b)})({Expr(b.Right, top: true)})";
        }
        else return Unsupported(b, leftType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            ? "?? on a nullable value type"
            : $"?? on '{leftType.ToDisplayString()}' (supported: classes, strings, interface values and delegates; both sides the same kind)");
        return $"({{ {Decl(type, tmp, b)} = {left}; {test} ? {tmp} : {right}; }})";
    }

    /// <summary><c>x ??= y</c> on a variable or field of a reference type: y is evaluated and stored only when x is null.</summary>
    private string CoalesceAssignment(AssignmentExpressionSyntax a)
    {
        var type = Model(a).GetTypeInfo(a.Left).Type;
        if (type == null || WrittenProperty(a.Left) != null || !(IsHeapClass(type) || IsInterfaceType(type) || type.TypeKind == TypeKind.Delegate))
            return Unsupported(a, "??= other than on a variable or field of a class, string, interface or delegate type");
        if (!RequireSimple(a.Left, "??=")) return "0";
        var target = Expr(a.Left);
        var test = IsInterfaceType(type) ? $"{target}.obj == NULL" : type.TypeKind == TypeKind.Delegate ? $"{target}.fn == NULL" : $"{target} == NULL";
        var value = IsHeapClass(type) ? $"({CType(type, a)})({Expr(a.Right, top: true)})" : Expr(a.Right, top: true);
        var set = $"if ({Cond(test)}) {target} = {value};";
        return IsStatementContext(a) ? $"({{ {set} }})" : $"({{ {set} {target}; }})";
    }

    /// <summary>
    /// <c>$"score {n,5:D3}"</c>: formats into one of a few static buffers and gives the text as a string. The text is valid until
    /// eight more strings have been formatted, so use it right away (draw it, print it, copy it) and do not keep it.
    /// Holes can be integers (alignment <c>,N</c> and formats <c>D</c><i>n</i> / <c>X</c><i>n</i>), chars, bools and strings.
    /// </summary>
    private string Interpolation(InterpolatedStringExpressionSyntax interp)
    {
        var folded = Model(interp).GetConstantValue(interp);
        if (folded.HasValue && folded.Value is string constant) return CString(constant);
        var parts = new List<(ExpressionSyntax? Hole, ExpressionSyntax? Align, string? Text)>();
        var formats = new List<(string?, string?)>();
        foreach (var c in interp.Contents)
        {
            switch (c)
            {
                case InterpolatedStringTextSyntax t:
                    parts.Add((null, null, t.TextToken.ValueText.Replace("{{", "{").Replace("}}", "}")));
                    formats.Add((null, null));
                    break;
                case InterpolationSyntax i:
                    parts.Add((i.Expression, i.AlignmentClause?.Value, null));
                    formats.Add((i.FormatClause?.FormatStringToken.ValueText, null));
                    break;
            }
        }
        return FormatParts(interp, parts, formats);
    }

    private string FormatParts(SyntaxNode at, List<(ExpressionSyntax? Hole, ExpressionSyntax? Align, string? Text)> parts, List<(string? Format, string? _)> formats)
    {
        _usesFmt = true;
        var id = _objectTemps++;
        var buf = $"ES_f{id}";
        var len = $"ES_n{id}";
        var sb = new StringBuilder($"({{ char *{buf} = es_fmt_begin(); int {len} = 0; ");
        for (var i = 0; i < parts.Count; i++)
        {
            var (hole, align, text) = parts[i];
            if (hole == null)
            {
                if (text!.Length > 0) sb.Append($"{len} = es_put_str({buf}, {len}, {CString(text)}, 0); ");
                continue;
            }
            var width = "0";
            if (align != null)
            {
                var w = Model(align).GetConstantValue(align);
                if (!w.HasValue || w.Value is not int wi) { Unsupported(align, "alignment that is not a constant integer"); continue; }
                width = wi.ToString(CultureInfo.InvariantCulture);
            }
            var format = formats[i].Item1;
            int digits = 0, hex = 0;
            if (!string.IsNullOrEmpty(format))
            {
                var f = format!;
                if ((f[0] is 'D' or 'd' or 'X' or 'x') && (f.Length == 1 || int.TryParse(f.AsSpan(1), out digits)))
                    hex = f[0] is 'X' or 'x' ? (f[0] == 'X' ? 1 : 2) : 0;
                else Unsupported(hole, $"format '{format}' (only D<n> and X<n> for integers)");
            }
            var type = Model(hole).GetTypeInfo(hole).Type;
            var value = Expr(hole, top: true);
            switch (type?.SpecialType)
            {
                case SpecialType.System_String:
                    sb.Append($"{len} = es_put_str({buf}, {len}, {value}, {width}); ");
                    break;
                case SpecialType.System_Char:
                    sb.Append($"{len} = es_put_chr({buf}, {len}, (int){value}, {width}); ");
                    break;
                case SpecialType.System_Boolean:
                    sb.Append($"{len} = es_put_str({buf}, {len}, ({value}) ? \"True\" : \"False\", {width}); ");
                    break;
                case SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32:
                    sb.Append($"{len} = es_put_i({buf}, {len}, (int32_t)({value}), {width}, {digits}, {hex}); ");
                    break;
                case SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32:
                    sb.Append($"{len} = es_put_u({buf}, {len}, (uint32_t)({value}), {width}, {digits}, {hex}); ");
                    break;
                case SpecialType.System_Int64 or SpecialType.System_IntPtr:
                    sb.Append($"{len} = es_put_i64({buf}, {len}, (int64_t)({value}), {width}, {digits}, {hex}); ");
                    break;
                case SpecialType.System_UInt64 or SpecialType.System_UIntPtr:
                    sb.Append($"{len} = es_put_u64({buf}, {len}, (uint64_t)({value}), {width}, {digits}, {hex}); ");
                    break;
                default:
                    if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } en)
                    {
                        // The member's name, or the number when the value is none of the members (as ToString does); 'D' and 'X' give the number.
                        var under = en.EnumUnderlyingType!.SpecialType;
                        var signed = under is SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64;
                        var wide = under is SpecialType.System_Int64 or SpecialType.System_UInt64;
                        var num = (wide, signed) switch
                        {
                            (true, true) => $"es_put_i64({buf}, {len}, (int64_t)ES_v{id}_{i}, {width}, {digits}, {hex})",
                            (true, false) => $"es_put_u64({buf}, {len}, (uint64_t)ES_v{id}_{i}, {width}, {digits}, {hex})",
                            (false, true) => $"es_put_i({buf}, {len}, (int32_t)ES_v{id}_{i}, {width}, {digits}, {hex})",
                            _ => $"es_put_u({buf}, {len}, (uint32_t)ES_v{id}_{i}, {width}, {digits}, {hex})",
                        };
                        if (string.IsNullOrEmpty(format))
                            sb.Append($"{{ {CType(en, hole)} ES_v{id}_{i} = {value}; const char *ES_s{id}_{i} = {EnumNameFunction(en)}(ES_v{id}_{i}); {len} = ES_s{id}_{i} ? es_put_str({buf}, {len}, ES_s{id}_{i}, {width}) : {num}; }} ");
                        else
                            sb.Append($"{{ {CType(en, hole)} ES_v{id}_{i} = {value}; {len} = {num}; }} ");
                    }
                    else
                        Unsupported(hole, $"{type?.ToDisplayString()} in a string (strings can hold integers, chars, bools, enums and strings)");
                    break;
            }
        }
        sb.Append($"{buf}[{len}] = 0; {buf}; }})");
        return sb.ToString();
    }

    /// <summary>A function from an enum value to the name of its member (null for a value that is none of them).</summary>
    private string EnumNameFunction(INamedTypeSymbol en)
    {
        var name = $"ES_name_{TypeName(en)}";
        var key = "ES_zenum_" + name;
        if (!_spans.ContainsKey(key))
        {
            var sb = new StringBuilder($"static __attribute__((unused)) const char *{name}({CType(en, en.DeclaringSyntaxReferences.First().GetSyntax())} v)\n{{\n    switch (v)\n    {{\n");
            var seen = new HashSet<string>();
            foreach (var m in en.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst))
                if (seen.Add(Convert.ToString(m.ConstantValue, CultureInfo.InvariantCulture)!))
                    sb.Append($"        case {FieldName(m)}: return \"{m.Name}\";\n");
            sb.Append("        default: return NULL;\n    }\n}\n");
            _spans[key] = sb.ToString();
        }
        return name;
    }

    private string DefaultValue(ExpressionSyntax e)
    {
        var type = Model(e).GetTypeInfo(e).Type!;
        return type.TypeKind == TypeKind.Struct && type.SpecialType == SpecialType.None || IsInterfaceType(type) || type.TypeKind == TypeKind.Delegate
            ? $"({CType(type, e)}){{0}}"
            : "0";
    }

    private string ObjectCreation(ObjectCreationExpressionSyntax oc)
    {
        var type = Model(oc).GetTypeInfo(oc).Type!;
        if (IsSpan(type))
        {
            // new Span<T>(T* pointer, int length): a view over memory from an arena or the OS.
            var spanArgs = oc.ArgumentList?.Arguments ?? default;
            if (spanArgs.Count != 2 || Model(oc).GetTypeInfo(spanArgs[0].Expression).Type is not IPointerTypeSymbol)
                return Unsupported(oc, "Span constructor other than (pointer, length)");
            var spanType = (INamedTypeSymbol)type;
            var elemPtr = CType(spanType.TypeArguments[0], oc) + " *";
            return $"(({SpanType(spanType, oc)}){{ ({elemPtr}){Expr(spanArgs[0].Expression)}, {Expr(spanArgs[1].Expression, top: true)} }})";
        }
        if (IsHeapClass(type))
        {
            _usesAlloc = true;
            string create;
            if (Model(oc).GetSymbolInfo(oc).Symbol is IMethodSymbol { IsImplicitlyDeclared: false } cctor)
            {
                if (cctor.Parameters.Any(p => p.RefKind != RefKind.None)) return Unsupported(oc, "ref/out constructor argument");
                create = $"{FuncName(cctor)}({string.Join(", ", (oc.ArgumentList?.Arguments ?? default).Select(a => Argument(a, cctor)))})";
            }
            else if ((oc.ArgumentList?.Arguments.Count ?? 0) > 0) return Unsupported(oc, "constructor call");
            else
            {
                var cls = (INamedTypeSymbol)type;
                var alloc = IsPoly(cls) ? $"{TypeName(cls)}_alloc()" : $"ES_NEW({TypeName(cls)})";
                var implicitInits = ImplicitInits(cls, "ES_n", includeSelf: true);
                create = implicitInits.Length == 0 ? alloc : $"({{ {TypeName(cls)} *ES_n = {alloc}; {implicitInits} ES_n; }})";
            }
            if (oc.Initializer == null) return create;
            var temp = $"ES_o{_objectTemps++}";
            var sets = new StringBuilder();
            foreach (var item in oc.Initializer.Expressions)
            {
                if (item is AssignmentExpressionSyntax { Left: IdentifierNameSyntax field } a && IsInitializable(Model(a).GetSymbolInfo(field).Symbol))
                    sets.Append($"{temp}->{MemberPath(type, Model(a).GetSymbolInfo(field).Symbol!.ContainingType, field.Identifier.Text)} = {Expr(a.Right, top: true)}; ");
                else if (item is AssignmentExpressionSyntax { Left: IdentifierNameSyntax prop } pa && pa.IsKind(SyntaxKind.SimpleAssignmentExpression)
                         && Model(pa).GetSymbolInfo(prop).Symbol is IPropertySymbol { IsStatic: false, IsIndexer: false, SetMethod: { } setter })
                    // A property with a setter body (new Text { Frame = 3 }): its setter runs on the new object, in initializer order.
                    sets.Append($"{InstanceCall(setter, temp, new[] { SetterValue((IPropertySymbol)Model(pa).GetSymbolInfo(prop).Symbol!, pa.Right) })}; ");
                else
                    Unsupported(item, "object initializer element");
            }
            return $"({{ {TypeName((INamedTypeSymbol)type)} *{temp} = {create}; {sets}{temp}; }})";
        }
        if (type.TypeKind == TypeKind.Struct && Model(oc).GetSymbolInfo(oc).Symbol is IMethodSymbol { IsImplicitlyDeclared: false } ctor)
        {
            if (oc.Initializer != null) return Unsupported(oc, "constructor call combined with an object initializer");
            if (ctor.Parameters.Any(p => p.RefKind != RefKind.None)) return Unsupported(oc, "ref/out constructor argument");
            var ctorArgs = string.Join(", ", (oc.ArgumentList?.Arguments ?? default).Select(a => Argument(a, ctor)));
            return $"{FuncName(ctor)}({ctorArgs})";
        }
        if (type.TypeKind != TypeKind.Struct || (oc.ArgumentList?.Arguments.Count ?? 0) > 0)
        {
            Unsupported(oc, "constructor call");
            return "0";
        }

        var cname = CType(type, oc);
        if (oc.Initializer == null) return $"({cname}){{0}}";

        var inits = new List<string>();
        foreach (var item in oc.Initializer.Expressions)
        {
            if (item is AssignmentExpressionSyntax { Left: IdentifierNameSyntax field } a &&
                IsInitializable(Model(a).GetSymbolInfo(field).Symbol))
                inits.Add($".{field.Identifier.Text} = {Expr(a.Right, top: true)}");
            else
                Unsupported(item, "object initializer element");
        }
        return $"({cname}){{ {string.Join(", ", inits)} }}";
    }

    private string Name(IdentifierNameSyntax id)
    {
        var sym = Model(id).GetSymbolInfo(id).Symbol;
        switch (sym)
        {
            case IParameterSymbol { RefKind: not RefKind.None } rp:
                return $"(*{rp.Name})";
            case ILocalSymbol { RefKind: not RefKind.None } rl:
                return $"(*{rl.Name})";   // a ref local is a pointer to the variable it refers to
            case IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction } group:
                return MethodGroup(group, id);   // a method group converted to a delegate
            case IRangeVariableSymbol:
                return id.Identifier.Text;   // a name bound by a LINQ query, which is a local of the loop it becomes
            case ILocalSymbol or IParameterSymbol:
                return _captures.TryGetValue(sym, out var captured) ? captured : id.Identifier.Text;
            case IFieldSymbol f:
                return FieldRef(f, id);
            case IPropertySymbol p:
                return PropertyRead(p, null, id);
            default:
                Unsupported(id, $"identifier '{id.Identifier.Text}'");
                return id.Identifier.Text;
        }
    }

    private string FieldRef(IFieldSymbol f, SyntaxNode at)
    {
        if (f.ContainingType.TypeKind == TypeKind.Enum) return FieldName(f);
        if (f.RefKind != RefKind.None) return Unsupported(at, "ref field");
        if (f.IsConst && f.ConstantValue != null)
            return ConstantLiteral(f.ConstantValue, f.Type);
        if (f.IsStatic) return FieldName(f);
        switch (_this)
        {
            case ThisKind.Pointer: return $"{ThisName}->{MemberPath(_thisType, f.ContainingType, f.Name)}";
            case ThisKind.Value: return $"{ThisName}.{f.Name}";
        }
        Unsupported(at, "instance field access outside a struct method");
        return f.Name;
    }

    private string MemberAccess(MemberAccessExpressionSyntax ma)
    {
        var sym = Model(ma).GetSymbolInfo(ma).Symbol;
        if (sym is IPropertySymbol { Name: "Length" })
        {
            var recvType = Model(ma).GetTypeInfo(ma.Expression).Type;
            if (recvType is IArrayTypeSymbol && RequireSimple(ma.Expression, "Length"))
            {
                _usesLen = true;
                return $"ES_LEN({Expr(ma.Expression)})";
            }
            if (IsSpan(recvType))
                return $"{Expr(ma.Expression)}.length";
            // A string is a NUL-terminated byte string in C: Length counts bytes (the same as characters for ASCII).
            if (recvType?.SpecialType == SpecialType.System_String)
            {
                _usesStrlen = true;
                return $"es_strlen({Expr(ma.Expression)})";
            }
        }
        if (sym is IPropertySymbol spanProp && IsSpan(spanProp.ContainingType))
        {
            if (spanProp is { IsStatic: true, Name: "Empty" }) return $"(({SpanType(spanProp.ContainingType, ma)}){{ NULL, 0 }})";
            if (spanProp is { IsStatic: false, Name: "IsEmpty" }) return $"({Expr(ma.Expression)}.length == 0)";
        }
        if (sym is IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary } group)
            return MethodGroup(group, ma);   // a method group converted to a delegate
        if (sym is IPropertySymbol prop)
            return PropertyRead(prop, ma.Expression, ma);
        if (sym is IFieldSymbol f)
        {
            if (f.IsStatic || f.IsConst || f.ContainingType.TypeKind == TypeKind.Enum)
                return FieldRef(f, ma);
            var exprType = Model(ma).GetTypeInfo(ma.Expression).Type;
            var op = IsRef(exprType) ? "->" : ".";
            return $"{Expr(ma.Expression)}{op}{(op == "->" ? MemberPath(exprType, f.ContainingType, FieldCName(f)) : FieldCName(f))}";
        }
        Unsupported(ma, $"member access '{ma}'");
        return "0";
    }

    private string ElementAccess(ElementAccessExpressionSyntax ea)
    {
        var recvType = Model(ea).GetTypeInfo(ea.Expression).Type;
        var args = ea.ArgumentList.Arguments;
        if (Model(ea).GetSymbolInfo(ea).Symbol is IPropertySymbol { IsIndexer: true } indexer && recvType is INamedTypeSymbol { TypeKind: TypeKind.Struct } && !IsSpan(recvType))
        {
            var parts = AccessParts(ea);
            return PropertyRead(indexer, parts.recv, ea, parts.indexes);
        }
        if (args.Count != 1 || Model(ea).GetTypeInfo(args[0].Expression).Type?.Name is "Index" or "Range")
        {
            Unsupported(ea, "indexer other than a single integer index");
            return "0";
        }
        var index = Expr(args[0].Expression, top: true);
        var recv = Expr(ea.Expression);
        if (recvType?.SpecialType == SpecialType.System_String)
            return $"((uint8_t){recv}[{index}])";
        if (IsSpan(recvType))
        {
            if (_bounds && RequireSimple(ea.Expression, "bounds-checked index"))
            {
                _usesBounds = true;
                return $"{recv}.ptr[ES_IDX({index}, {recv}.length)]";
            }
            return $"{recv}.ptr[{index}]";
        }
        if (recvType is IArrayTypeSymbol && _bounds && RequireSimple(ea.Expression, "bounds-checked index"))
        {
            _usesBounds = true;
            _usesLen = true;
            return $"{recv}[ES_IDX({index}, ES_LEN({recv}))]";
        }
        if (recvType is IArrayTypeSymbol or IPointerTypeSymbol) return $"{recv}[{index}]";
        Unsupported(ea, "indexer");
        return "0";
    }

    private string SpanSlice(InvocationExpressionSyntax inv, MemberAccessExpressionSyntax recvAccess, IMethodSymbol m)
    {
        var args = inv.ArgumentList.Arguments;
        if (args.Count is < 1 or > 2) return Unsupported(inv, "Slice with these arguments");
        var type = SpanType((INamedTypeSymbol)m.ContainingType, inv);
        if (!IsSimple(recvAccess.Expression) || !IsPure(args[0].Expression))
        {
            // list.AsSpan().Slice(a, b), s.Slice(F()): the span and the start are evaluated once, into temporaries.
            var n = ++_temp;
            var tStart = Expr(args[0].Expression, top: true);
            var tLength = args.Count == 2 ? Expr(args[1].Expression, top: true) : $"_sl{n}.length - _ss{n}";
            return $"({{ {type} _sl{n} = {Expr(recvAccess.Expression, top: true)}; int32_t _ss{n} = {tStart}; ({type}){{ _sl{n}.ptr + _ss{n}, {tLength} }}; }})";
        }
        var recv = Expr(recvAccess.Expression);
        var start = Expr(args[0].Expression);
        var length = args.Count == 2 ? Expr(args[1].Expression, top: true) : $"{recv}.length - {start}";
        return $"(({type}){{ {recv}.ptr + {start}, {length} }})";
    }

    private string Invocation(InvocationExpressionSyntax inv)
    {
        if (Model(inv).GetSymbolInfo(inv).Symbol is not IMethodSymbol m)
        {
            Unsupported(inv, "call");
            return "0";
        }

        if (IsArgsEntry(m))
        {
            Unsupported(inv, "calling the entry point that takes string[] args");
            return "0";
        }

        if (IsLinqCall(inv, out var linqMethod, out var linqReceiver)) return Linq(inv, linqMethod, linqReceiver);

        if (m.ContainingType?.SpecialType == SpecialType.System_String && m.Name == "Equals")
        {
            // string.Equals(a, b) and a.Equals(b): by the characters, like ==. (A null receiver gives false here, not an exception.)
            var eargs = inv.ArgumentList.Arguments;
            var stringArgs = m.Parameters.All(p => p.Type.SpecialType == SpecialType.System_String);
            if (m.IsStatic && eargs.Count == 2 && stringArgs)
                return StrEq(Expr(eargs[0].Expression, top: true), Expr(eargs[1].Expression, top: true));
            if (!m.IsStatic && eargs.Count == 1 && stringArgs && inv.Expression is MemberAccessExpressionSyntax eq)
                return StrEq(Expr(eq.Expression, top: true), Expr(eargs[0].Expression, top: true));
            return Unsupported(inv, "this form of string.Equals (only string.Equals(a, b) and a.Equals(b) on two strings)");
        }

        if (m.Name is "Equals" or "ReferenceEquals" && m.Parameters.All(p => p.Type.SpecialType == SpecialType.System_Object)
            && (m.ContainingType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType || m.IsOverride || m.ContainingType.Locations.Any(l => l.IsInSource)))
            return ObjectEquals(inv, m);

        if ((m.ReturnsByRef || m.ReturnsByRefReadonly) &&
            (m.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction) || m.IsVirtual || m.IsAbstract || m.IsOverride || IsInterfaceType(m.ContainingType)))
            return Unsupported(inv, "call of a ref-returning virtual, abstract, interface or delegate member");

        if (m.ContainingType is { Name: "Heap" } && m.Name.StartsWith("Free") && m.ContainingNamespace?.ToDisplayString() == "ESharp.Platform"
            && inv.ArgumentList.Arguments.Count == 1)
        {
            _usesAlloc = true;
            var freed = inv.ArgumentList.Arguments[0].Expression;
            if (Model(freed).GetSymbolInfo(freed).Symbol is IEventSymbol freedEvent)
                return EventPlace(freed, freedEvent) is { } evPlace ? $"es_event_clear(&({evPlace}))" : "0";
            var target = Expr(freed, top: true);
            if (Model(freed).GetTypeInfo(freed).Type is { TypeKind: TypeKind.Delegate } freedDelegate)
            {
                // A delegate: drop its reference to its heap closure (null env: nothing to free).
                _usesClosures = true;
                var dp = $"_fp{++_temp}";
                return $"({{ {CType(freedDelegate, freed)} *{dp} = &({target}); es_closure_release({dp}->env); *{dp} = ({CType(freedDelegate, freed)}){{0}}; }})";
            }
            if (IsInterfaceType(Model(freed).GetTypeInfo(freed).Type))
                return $"(free({target}.obj), {target} = ({CType(Model(freed).GetTypeInfo(freed).Type!, freed)}){{0}})";
            return $"(free({target}), {target} = NULL)";
        }

        if (m.ContainingType is { Name: "Heap" } && m.Name == "LiveClosures" && m.ContainingNamespace?.ToDisplayString() == "ESharp.Platform")
        {
            _usesClosures = true;
            return "es_closures_live";
        }

        if (m.ContainingType is { Name: "Cast" } && m.ContainingNamespace?.ToDisplayString() == "ESharp.Platform"
            && m.Name is "StringToPtr" or "PtrToString" && inv.ArgumentList.Arguments.Count == 1)
        {
            // Strings are plain byte pointers in C: reinterpret.
            var arg = Expr(inv.ArgumentList.Arguments[0].Expression, top: true);
            return m.Name == "StringToPtr" ? $"((uint8_t *)({arg}))" : $"((const char *)({arg}))";
        }

        if (m.ContainingType is { Name: "Cast" } && m.ContainingNamespace?.ToDisplayString() == "ESharp.Platform"
            && m.Name is "ObjectToPtr" or "PtrToObject" && m.IsGenericMethod && inv.ArgumentList.Arguments.Count == 1)
        {
            // A class reference is a plain pointer in C: reinterpret (for h_Data, user data slots and the like). Interfaces are
            // { obj, vt } pairs and delegates { fn, env }: no single pointer.
            var targ = m.TypeArguments[0];
            if (!IsHeapClass(targ) || targ.SpecialType == SpecialType.System_String)
                return Unsupported(inv, $"Cast.{m.Name} with {targ.ToDisplayString()}: only classes (not interfaces, delegates, arrays or strings) are one pointer");
            var arg = Expr(inv.ArgumentList.Arguments[0].Expression, top: true);
            return m.Name == "ObjectToPtr" ? $"((void *)({arg}))" : $"(({CType(m.TypeArguments[0], inv)})({arg}))";
        }

        if (m.Name == "Ready" && m.IsExtern && inv.ArgumentList.Arguments.Count == 0 && UseBase(m.ContainingType) is { } readyBase)
            return $"({readyBase} != NULL)";

        if (m.Name == "SetBase" && m.IsExtern && inv.ArgumentList.Arguments.Count == 1 && UseBase(m.ContainingType) is { } baseVar)
            return baseVar == "es_CyberGfxBase" ? $"ES_SETBASE_CyberGfxBase({Expr(inv.ArgumentList.Arguments[0].Expression, top: true)})" : $"({baseVar} = (__typeof__({baseVar}))({Expr(inv.ArgumentList.Arguments[0].Expression, top: true)}))";

        if (m.MethodKind == MethodKind.DelegateInvoke)
        {
            // f(x) and f.Invoke(x) call through the function pointer.
            var callee = inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Invoke" } inva ? inva.Expression : inv.Expression;
            if (Model(callee).GetSymbolInfo(callee).Symbol is IEventSymbol raised)
                return EventRaise(raised, callee, inv.ArgumentList, m, inv);
            var d = Expr(callee);
            if (!IsSimple(callee))
            {
                // Pick() (x): evaluate the delegate once, into a temporary of a statement expression.
                var tmp = $"_d{++_temp}";
                var targs = inv.ArgumentList.Arguments.Select(a => Argument(a, m)).Prepend($"{tmp}.env");
                return $"({{ {CType(Model(callee).GetTypeInfo(callee).Type!, callee)} {tmp} = {d}; {tmp}.fn({string.Join(", ", targs)}); }})";
            }
            var dargs = inv.ArgumentList.Arguments.Select(a => Argument(a, m)).Prepend($"{d}.env");
            return $"{d}.fn({string.Join(", ", dargs)})";
        }

        if (m.ContainingType?.SpecialType == SpecialType.System_String && !m.IsStatic
            && m.Name is "IndexOf" or "LastIndexOf" or "Contains" or "StartsWith" or "EndsWith"
            && inv.Expression is MemberAccessExpressionSyntax strAccess)
            return StringSearch(inv, m, strAccess);

        if (m.ContainingType?.SpecialType == SpecialType.System_String && m.Name == "Substring" && !m.IsStatic
            && inv.Expression is MemberAccessExpressionSyntax subAccess && inv.ArgumentList.Arguments.Count is 1 or 2)
        {
            _usesFmt = true;
            var subArgs = inv.ArgumentList.Arguments;
            return $"es_substr({Expr(subAccess.Expression, top: true)}, {Expr(subArgs[0].Expression, top: true)}, {(subArgs.Count == 2 ? Expr(subArgs[1].Expression, top: true) : "-1")})";
        }

        if (IsSpan(m.ContainingType) && m.Name == "Slice" && inv.Expression is MemberAccessExpressionSyntax sliceAccess)
            return SpanSlice(inv, sliceAccess, m);

        if (IsInterfaceType(m.ContainingType) && inv.Expression is MemberAccessExpressionSyntax ifaceAccess)
        {
            if (!IsSimple(ifaceAccess.Expression))
            {
                // Pick().M(x): the interface value is evaluated once, into a temporary of a statement expression.
                var it = $"_i{++_temp}";
                var itArgs = inv.ArgumentList.Arguments.Select(a => Argument(a, m)).Prepend($"{it}.obj");
                return $"({{ {CType(Model(ifaceAccess.Expression).GetTypeInfo(ifaceAccess.Expression).Type!, inv)} {it} = {Expr(ifaceAccess.Expression, top: true)}; {it}.vt->{SlotName(m)}({string.Join(", ", itArgs)}); }})";
            }
            var target = Expr(ifaceAccess.Expression);
            var callArgs = inv.ArgumentList.Arguments.Select(a => Argument(a, m)).Prepend($"{target}.obj");
            return $"{target}.vt->{SlotName(m)}({string.Join(", ", callArgs)})";
        }

        // A method of the .NET library that nothing above maps: E# has no code for it, so it would be an undefined symbol at link time.
        if (!m.OriginalDefinition.Locations.Any(l => l.IsInSource) && !HasAttribute(m, "AmigaLibAttribute") && !HasAttribute(m, "NativeAttribute"))
            return Unsupported(inv, $"call of {m.ContainingType?.ToDisplayString()}.{m.Name} (a .NET library method E# has no code for)");

        string? receiver = null;
        if (!m.IsStatic && m.MethodKind != MethodKind.LocalFunction)
        {
            if (m.ContainingType.TypeKind is not (TypeKind.Struct or TypeKind.Class) || m.MethodKind != MethodKind.Ordinary)
                return Unsupported(inv, "instance method call");
            receiver = ReceiverAddress(inv, m);
            if (receiver == null) return "0";
        }

        if (HasAttribute(m, "NativeAttribute")) _natives.Add(m.Name);
        var lib = GetAttribute(m, "AmigaLibAttribute");
        if (lib != null) UseBase(m.ContainingType);
        if (lib != null && lib.ConstructorArguments[0].Value is not "muimaster")
            _headers.Add(lib.ConstructorArguments[0].Value is "alib" ? "clib/alib_protos.h" : $"proto/{lib.ConstructorArguments[0].Value}.h");

        var argList = inv.ArgumentList.Arguments.Select(a => Argument(a, m));
        if (receiver != null)
            return InstanceCall(m, receiver, argList, (inv.Expression as MemberAccessExpressionSyntax)?.Expression is BaseExpressionSyntax);
        return $"{FuncName(m)}({string.Join(", ", argList)})";
    }

    /// <summary><c>(a, b) = t;</c> and <c>var (x, y) = t;</c>: the right side is evaluated once, then each part is assigned.</summary>
    private string Deconstruction(AssignmentExpressionSyntax a)
    {
        var rightInfo = Model(a).GetTypeInfo(a.Right);
        var rightType = (a.Right is TupleExpressionSyntax ? rightInfo.ConvertedType : rightInfo.Type) as INamedTypeSymbol;
        if (rightType is not { IsTupleType: true }) return Unsupported(a, "deconstruction of anything but a tuple");
        var targets = new List<string?>();
        void Collect(SyntaxNode left)
        {
            switch (left)
            {
                case TupleExpressionSyntax t:
                    foreach (var arg in t.Arguments) Collect(arg.Expression);
                    break;
                case DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax pd }:
                    foreach (var v in pd.Variables) Collect(v);
                    break;
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax sd }:
                    targets.Add(sd.Identifier.Text);
                    break;
                case SingleVariableDesignationSyntax sv:
                    targets.Add(sv.Identifier.Text);
                    break;
                case DiscardDesignationSyntax or DeclarationExpressionSyntax { Designation: DiscardDesignationSyntax }:
                    targets.Add(null);
                    break;
                case IdentifierNameSyntax { Identifier.Text: "_" } id when Model(id).GetSymbolInfo(id).Symbol is IDiscardSymbol:
                    targets.Add(null);
                    break;
                case ParenthesizedVariableDesignationSyntax nested:
                    foreach (var v in nested.Variables) Collect(v);
                    break;
                case ExpressionSyntax other:
                    targets.Add(Expr(other));
                    break;
            }
        }
        Collect(a.Left);
        if (targets.Count != rightType.TupleElements.Length) return Unsupported(a, "nested deconstruction");
        var tmp = "es_t" + _patternTemp++;
        var sb = new StringBuilder($"({{ {Decl(rightType, tmp, a)} = {Expr(a.Right, top: true)}; ");
        for (var i = 0; i < targets.Count; i++)
            if (targets[i] != null) sb.Append($"{targets[i]} = {tmp}.Item{i + 1}; ");
        sb.Append("(void)0; })");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ patterns

    private int _patternTemp;

    private string IsPattern(IsPatternExpressionSyntax ip)
    {
        var type = Model(ip).GetTypeInfo(ip.Expression).Type;
        if (type == null) return Unsupported(ip, "pattern on an expression without a type");
        if (UsePatternSubjectInPlace(ip.Expression, type))
            return $"({PatternTest(ip.Pattern, Expr(ip.Expression), type)})";
        var tmp = "es_p" + _patternTemp++;
        return $"({{ {Decl(type, tmp, ip)} = {Expr(ip.Expression, top: true)}; {PatternTest(ip.Pattern, tmp, type)}; }})";
    }

    private string SwitchExpression(SwitchExpressionSyntax sw)
    {
        var type = Model(sw).GetTypeInfo(sw.GoverningExpression).Type;
        var result = Model(sw).GetTypeInfo(sw).ConvertedType ?? Model(sw).GetTypeInfo(sw).Type;
        if (type == null || result == null) return Unsupported(sw, "switch expression without a type");
        var id = _patternTemp++;
        string subject = "es_sw" + id, value = "es_sv" + id;
        var sb = new StringBuilder();
        if (type is IArrayTypeSymbol && UsePatternSubjectInPlace(sw.GoverningExpression, type)) sb.Append("({ "); // a C array: used in place
        else sb.Append($"({{ {Decl(type, subject, sw)} = {Expr(sw.GoverningExpression, top: true)}; ");
        if (type is IArrayTypeSymbol && UsePatternSubjectInPlace(sw.GoverningExpression, type)) subject = Expr(sw.GoverningExpression);
        sb.Append($"{Decl(result, value, sw)}; ");
        var first = true;
        // Pattern variables (one declaration per name, also for the arms' when clauses).
        foreach (var d in PatternLocalDecls(sw.Arms.SelectMany(a => a.WhenClause != null ? new SyntaxNode[] { a.Pattern, a.WhenClause } : new SyntaxNode[] { a.Pattern })))
            sb.Append(d + " ");
        foreach (var arm in sw.Arms)
        {
            var test = PatternTest(arm.Pattern, subject, type);
            if (arm.WhenClause != null) test = $"({test}) && ({Expr(arm.WhenClause.Condition, top: true)})";
            sb.Append($"{(first ? "" : "else ")}if ({Cond(test)}) {value} = {Expr(arm.Expression, top: true)}; ");
            first = false;
        }
        sb.Append($"else __builtin_trap(); {value}; }})");
        return sb.ToString();
    }

    /// <summary>A C condition that is true when <paramref name="subject"/> (of <paramref name="type"/>) matches the pattern.</summary>
    private string PatternTest(PatternSyntax pattern, string subject, ITypeSymbol type)
    {
        switch (pattern)
        {
            case ParenthesizedPatternSyntax p:
                return PatternTest(p.Pattern, subject, type);
            case DiscardPatternSyntax:
                return "1";
            case ConstantPatternSyntax c:
                return ConstantTest(c.Expression, c, subject, type);
            case RelationalPatternSyntax r:
                return $"({subject} {r.OperatorToken.Text} {Expr(r.Expression)})";
            case BinaryPatternSyntax b:
                return $"({PatternTest(b.Left, subject, type)} {(b.IsKind(SyntaxKind.AndPattern) ? "&&" : "||")} {PatternTest(b.Right, subject, type)})";
            case UnaryPatternSyntax u when u.IsKind(SyntaxKind.NotPattern):
                return $"(!{PatternTest(u.Pattern, subject, type)})";
            case VarPatternSyntax v:
                return BindDesignation(v.Designation, subject, type, v);
            case DeclarationPatternSyntax d:
            {
                if (Model(d).GetTypeInfo(d.Type).Type is INamedTypeSymbol dt && CanTypeTest(type, dt) && !SymbolEqualityComparer.Default.Equals(dt, type))
                    return $"({TypeIs(subject, type, dt, d)} && {Bind(d.Designation, TypeAs(subject, type, dt), d)})";
                // 'x is T t' with x already a T: still false for null.
                return NotNullTest(subject, type) is { } dn ? $"({dn} && {Bind(d.Designation, subject, d)})" : Bind(d.Designation, subject, d);
            }
            case TypePatternSyntax t:
            {
                var tt = Model(t).GetTypeInfo(t.Type).Type;
                if (tt != null && SymbolEqualityComparer.Default.Equals(tt, type)) return NotNullTest(subject, type) ?? "1";
                if (tt is INamedTypeSymbol tc && CanTypeTest(type, tc)) return $"({TypeIs(subject, type, tc, t)})";
                return Unsupported(t, "type test");
            }
            case RecursivePatternSyntax rec:
                return RecursivePatternTest(rec, subject, type);
            case ListPatternSyntax lp:
                return ListPatternTest(lp, subject, type);
        }
        return Unsupported(pattern, pattern.Kind().ToString());
    }

    /// <summary>
    /// A C condition: <paramref name="subject"/> (a simple C expression holding a class reference of static type <paramref name="from"/>) is not null
    /// and is an object of class <paramref name="target"/> or a class derived from it.
    /// </summary>
    private string ClassIs(string subject, INamedTypeSymbol from, INamedTypeSymbol target, SyntaxNode at)
    {
        if (IsBaseOrSame(from, target)) return $"{subject} != NULL";                      // an upcast always succeeds for a non-null object
        if (!IsBaseOrSame(target, from)) return "0";                                      // unrelated classes: never
        if (!IsPoly(target)) return Unsupported(at, "type test on a class without virtual members or subclasses");
        return $"({subject} != NULL && es_is((const struct ES_Type *){subject}->{VtPath(from)}, {TypeIdOf(target)}))";
    }

    private string Bind(VariableDesignationSyntax? designation, string subject, SyntaxNode at)
    {
        if (designation is SingleVariableDesignationSyntax single)
            return $"(({single.Identifier.Text} = {subject}), 1)";
        return "1";
    }

    // ------------------------------------------------------------------ lambdas and method groups

    private readonly Dictionary<ISymbol, string> _captures = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<SyntaxNode, (List<ISymbol> Locals, bool UsesThis)> _captureCache = new();

    private static string EnvName(IMethodSymbol lambda) => FuncName(lambda) + "_env";

    /// <summary>The locals and parameters of the enclosing code that a lambda uses, and whether it uses 'this' (explicitly or through an instance member).</summary>
    private (List<ISymbol> Locals, bool UsesThis) FindCaptures(SyntaxNode lam)
    {
        if (_captureCache.TryGetValue(lam, out var cached)) return cached;
        var model = Model(lam);
        var locals = new List<ISymbol>();
        var usesThis = false;
        foreach (var n in lam.DescendantNodes())
        {
            if (n is ThisExpressionSyntax or BaseExpressionSyntax) { usesThis = true; continue; }
            if (n is not IdentifierNameSyntax id) continue;
            // 'c.Suit': the name after a dot belongs to the thing before it; 'x:' in a call and 'X = 1' in an initializer name a parameter or member.
            if (n.Parent is MemberAccessExpressionSyntax dot && dot.Name == n) continue;
            if (n.Parent is NameColonSyntax) continue;
            if (n.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax { Parent: ObjectCreationExpressionSyntax } } init && init.Left == n) continue;
            switch (model.GetSymbolInfo(id).Symbol)
            {
                case ILocalSymbol or IParameterSymbol { IsThis: false }:
                    var sym = model.GetSymbolInfo(id).Symbol!;
                    if (!sym.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == lam.SyntaxTree && lam.Span.Contains(r.Span)) &&
                        !locals.Contains(sym, SymbolEqualityComparer.Default))
                        locals.Add(sym);
                    break;
                case IFieldSymbol { IsStatic: false, IsConst: false } or IPropertySymbol { IsStatic: false } or IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary }:
                    usesThis = true;
                    break;
            }
        }
        return _captureCache[lam] = (locals, usesThis);
    }

    /// <summary>
    /// A lambda is a delegate value: its function and an environment. With nothing captured the environment is null; otherwise it is a struct
    /// of pointers to the captured locals (and 'this'), built on the stack of the method that creates the lambda. So a capturing lambda
    /// is only valid while that method runs; it can be passed down to other methods, but not returned or stored.
    /// </summary>
    private string Lambda(LambdaExpressionSyntax lam)
    {
        if (Model(lam).GetSymbolInfo(lam).Symbol is not IMethodSymbol m) return Unsupported(lam, "lambda");
        var del = Model(lam).GetTypeInfo(lam).ConvertedType;
        if (del?.TypeKind != TypeKind.Delegate) return Unsupported(lam, "lambda that is not converted to a delegate type");
        var dt = CType(del, lam);
        var (locals, usesThis) = FindCaptures(lam);
        if (locals.Count == 0 && !usesThis) return $"(({dt}){{ {FuncName(m)}, NULL }})";
        if (IsHeapLambda(lam)) return HeapLambda(lam, m, dt);

        SyntaxNode? p = lam.Parent;
        while (p is ParenthesizedExpressionSyntax or CastExpressionSyntax or ConditionalExpressionSyntax) p = p.Parent;
        var allowed = p is ArgumentSyntax || p is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } } };
        if (!allowed)
            return Unsupported(lam, "lambda that captures a local or 'this' here: a borrowed (stack) closure must be an argument or a local variable's initializer, and a stored (heap) closure must be stored in a field, a static, an array element, a property or an event, returned, passed to an [Owned] parameter, or held in a local that is one of these or freed with Heap.Free");

        var fields = new List<string>();
        var inits = new List<string>();
        for (var i = 0; i < locals.Count; i++)
        {
            ITypeSymbol type = locals[i] is ILocalSymbol l ? l.Type : ((IParameterSymbol)locals[i]).Type;
            if (type is IArrayTypeSymbol) return Unsupported(lam, $"lambda that captures the array '{locals[i].Name}'; pass it as a Span<T>");
            fields.Add(Decl(type, $"*c{i}_{locals[i].Name}", lam) + ";");
            inits.Add("&" + (_captures.TryGetValue(locals[i], out var outer) ? outer : locals[i].Name));
        }
        if (usesThis)
        {
            if (_this != ThisKind.Pointer) return Unsupported(lam, "lambda that uses 'this' here");
            fields.Add($"{TypeName(m.ContainingType)} *{ThisName};");
            inits.Add(ThisName);
        }
        var env = EnvName(m);
        _spans[env] = $"struct {env} {{ {string.Join(" ", fields)} }};\n";
        return $"(({dt}){{ {FuncName(m)}, &(struct {env}){{ {string.Join(", ", inits)} }} }})";
    }

    /// <summary>A static method used as a delegate: a small function that takes the (unused) environment first and calls the method.</summary>
    private string MethodGroup(IMethodSymbol m, ExpressionSyntax at)
    {
        var del = Model(at).GetTypeInfo(at).ConvertedType as INamedTypeSymbol;
        var invoke = del?.DelegateInvokeMethod;
        if (del == null || invoke == null) return FuncName(m);   // not a delegate conversion
        if (HasAttribute(m, "AmigaLibAttribute") || m.IsExtern) return Unsupported(at, "a library function as a delegate; wrap it in a lambda");
        var wrapper = FuncName(m) + "__dg";
        var key = "ES_zwrap_" + wrapper;
        if (!_spans.ContainsKey(key))
        {
            var ps = invoke.Parameters.Select((p, i) => p.RefKind == RefKind.None ? Decl(p.Type, $"a{i}", at) : Decl(p.Type, $"*a{i}", at)).Prepend("void *_env");
            var args = string.Join(", ", invoke.Parameters.Select((_, i) => $"a{i}"));
            _spans[key] = $"{Prototype(m)};\nstatic {Decl(invoke.ReturnType, $"{wrapper}({string.Join(", ", ps)})", at)} {{ {(invoke.ReturnsVoid ? "" : "return ")}{FuncName(m)}({args}); }}\n";
        }
        return $"(({CType(del, at)}){{ {wrapper}, NULL }})";
    }

    /// <summary>Local functions become C functions of their own, so they may not use the locals, parameters or 'this' of the method around them.</summary>
    private void CheckNoCapture(SyntaxNode lf, string what)
    {
        var model = Model(lf);
        foreach (var n in lf.DescendantNodes())
        {
            ISymbol? sym = n switch
            {
                IdentifierNameSyntax id => model.GetSymbolInfo(id).Symbol,
                ThisExpressionSyntax => model.GetEnclosingSymbol(n.SpanStart),
                _ => null,
            };
            if (n is ThisExpressionSyntax) { Unsupported(n, $"{what} that uses 'this'"); continue; }
            // 'c.Suit': the name after a dot belongs to the thing before it, not to an implicit 'this'.
            if (n.Parent is MemberAccessExpressionSyntax dot && dot.Name == n) continue;
            var captured = sym switch
            {
                ILocalSymbol or IParameterSymbol => !sym.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == lf.SyntaxTree && lf.Span.Contains(r.Span)),
                IFieldSymbol { IsStatic: false, IsConst: false } or IPropertySymbol { IsStatic: false } => true,
                IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary } => true,
                _ => false,
            };
            if (captured) Unsupported(n, $"{what} that captures '{n}'; pass it as a parameter or make it a static method");
        }
    }

    /// <summary>One call argument; ref/out/in parameters take the address of the argument.</summary>
    private string Argument(ArgumentSyntax a, IMethodSymbol m)
    {
        IParameterSymbol? param = null;
        if (a.NameColon != null)
            param = m.Parameters.FirstOrDefault(p => p.Name == a.NameColon.Name.Identifier.Text);
        else if (a.Parent is ArgumentListSyntax list)
        {
            var index = list.Arguments.IndexOf(a);
            param = index < m.Parameters.Length ? m.Parameters[index] : m.Parameters.LastOrDefault(p => p.IsParams);
        }
        if (param == null || param.RefKind == RefKind.None)
        {
            var plain = Expr(a.Expression, top: true);
            return IsHandoverParameter(param) && a.RefKindKeyword.IsKind(SyntaxKind.None)
                ? HandOver(a.Expression, plain, param!.Type, $"the [Owned] parameter '{param.Name}'") : plain;
        }

        var e = Strip(a.Expression);
        switch (e)
        {
            case DeclarationExpressionSyntax decl when decl.Designation is SingleVariableDesignationSyntax single:
                return "&" + single.Identifier.Text;
            case DeclarationExpressionSyntax:
            case IdentifierNameSyntax { Identifier.Text: "_" } when Model(e).GetSymbolInfo(e).Symbol is IDiscardSymbol:
                return "&" + DiscardName(e);
        }
        if (Model(e).GetTypeInfo(e).Type is { } && IsAddressable(e))
            return "&" + Expr(e);
        if (param.RefKind is RefKind.In or RefKind.RefReadOnlyParameter)
            return $"&(({CType(param.Type, a)}[1]){{ {Expr(e, top: true)} }})[0]"; // a temporary, as C# allows for 'in'
        return Unsupported(a, "ref/out argument that is not a variable, field or array element");
    }

    private bool IsAddressable(ExpressionSyntax e)
    {
        switch (e)
        {
            case IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax:
                var sym = Model(e).GetSymbolInfo(e).Symbol;
                if (sym is IPropertySymbol { IsIndexer: true, ContainingType.Name: "Span" } spanIndexer && IsSpan(spanIndexer.ContainingType)) return true;   // s.ptr[i]
                if (sym is IPropertySymbol prop) return IsAutoProperty(prop);
                if (sym is IFieldSymbol { IsConst: true }) return false;
                return sym is ILocalSymbol or IParameterSymbol or IFieldSymbol || (sym == null);
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression }:
                return true;
            case InvocationExpressionSyntax inv:
                return ReturnsRef(inv);   // emitted as (*call), which is a C lvalue
        }
        return false;
    }

    /// <summary>A ref local or ref/out/in parameter (held as a pointer in C), or null.</summary>
    private static ISymbol? IsRefVariable(ISymbol? s) =>
        s is ILocalSymbol { RefKind: not RefKind.None } or IParameterSymbol { RefKind: not RefKind.None } ? s : null;

    /// <summary>True when <paramref name="inv"/> calls a method that returns by reference (its C function returns a pointer).</summary>
    private bool ReturnsRef(InvocationExpressionSyntax inv) =>
        Model(inv).GetSymbolInfo(inv).Symbol is IMethodSymbol { MethodKind: not MethodKind.DelegateInvoke } m && (m.ReturnsByRef || m.ReturnsByRefReadonly);

    /// <summary>The C address of the place a <c>ref</c> expression refers to (<c>ref x</c>, <c>ref a[i]</c>, <c>ref F()</c>, <c>c ? ref a : ref b</c>).</summary>
    private string RefAddress(ExpressionSyntax e)
    {
        e = Strip(e);
        switch (e)
        {
            case ConditionalExpressionSyntax { WhenTrue: RefExpressionSyntax t, WhenFalse: RefExpressionSyntax f } c:
                return $"({Expr(c.Condition)} ? {RefAddress(t.Expression)} : {RefAddress(f.Expression)})";
            case InvocationExpressionSyntax inv when ReturnsRef(inv):
                return Invocation(inv);   // already the pointer
            case IdentifierNameSyntax id when IsRefVariable(Model(id).GetSymbolInfo(id).Symbol) is { } rv:
                return rv.Name;            // already the pointer
            case ElementAccessExpressionSyntax ea when Model(ea).GetSymbolInfo(ea).Symbol is IPropertySymbol { IsIndexer: true, ContainingType.Name: "ReadOnlySpan" } ro && IsSpan(ro.ContainingType):
                // ref readonly to a read-only span element: C# keeps it read-only, so the C const can be dropped.
                return $"(({CType(ro.Type, ea)} *)&{Expr(ea)})";
        }
        if (Model(e).GetTypeInfo(e).Type is { } && IsAddressable(e)) return "&" + Expr(e);
        return Unsupported(e, "ref to this kind of expression (only variables, fields, array and span elements, and ref-returning calls)");
    }

    private static string DiscardName(SyntaxNode e) => "es_discard_" + e.SpanStart;

    /// <summary>The C pointer passed as the hidden first argument of a struct instance method.</summary>
    private string? ReceiverAddress(InvocationExpressionSyntax inv, IMethodSymbol m) =>
        ReceiverOf((inv.Expression as MemberAccessExpressionSyntax)?.Expression, inv, m.Name, "instance method call");

    /// <summary>The address of the object an instance member is used on; <paramref name="recv"/> null means the current 'this'.</summary>
    private string? ReceiverOf(ExpressionSyntax? recv, SyntaxNode at, string name, string what)
    {
        if (recv == null)
        {
            // Unqualified use: the receiver is the current 'this'.
            return _this switch
            {
                ThisKind.Pointer => ThisName,
                ThisKind.Value => "&" + ThisName,
                _ => Unsupported(at, what + " outside a struct method"),
            };
        }

        recv = Strip(recv);
        if (recv is ThisExpressionSyntax or BaseExpressionSyntax) return _this == ThisKind.Pointer ? ThisName : "&" + ThisName;
        if (IsRef(Model(at).GetTypeInfo(recv).Type)) return Expr(recv);   // a pointer or a class reference is already the address

        var core = recv;
        if (core is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax &&
            Model(at).GetSymbolInfo(core).Symbol is not IMethodSymbol && !(Model(at).GetSymbolInfo(core).Symbol is IPropertySymbol cp && !IsAutoProperty(cp)))
            return "&" + Expr(core);
        if (core is InvocationExpressionSyntax refCall && ReturnsRef(refCall)) return Invocation(refCall);   // F().M(): the returned pointer
        // span[i].M(): the element itself (a Span indexer returns a reference, so M may change it in place).
        if (core is ElementAccessExpressionSyntax spanElem && Model(at).GetSymbolInfo(spanElem).Symbol is IPropertySymbol { IsIndexer: true, ContainingType.Name: "Span" } si && IsSpan(si.ContainingType))
            return "&" + Expr(spanElem);

        // A value with no place of its own (Make().M(), list[i].M(), a ReadOnlySpan element): C# works on a copy, and so does C, in a
        // compound literal that lives until the end of the enclosing block.
        var tempType = Model(at).GetTypeInfo(recv).Type;
        if (tempType is INamedTypeSymbol { TypeKind: TypeKind.Struct } && !IsSpan(tempType))
            return $"&(({CType(tempType, recv)}[1]){{ {Expr(recv, top: true)} }})[0]";

        return Unsupported(recv, $"use of '{name}' on a temporary value; assign it to a local first");
    }

    // ------------------------------------------------------------------ properties

    private static bool IsInitializable(ISymbol? s) =>
        s is IFieldSymbol || s is IPropertySymbol p && IsAutoProperty(p);

    private string PropertyRead(IPropertySymbol p, ExpressionSyntax? recv, SyntaxNode at,
        IReadOnlyList<ExpressionSyntax>? indexes = null)
    {
        if (IsAutoProperty(p))
        {
            if (p.IsStatic) return $"{TypeName(p.ContainingType)}_{p.Name}";
            if (recv == null || recv is ThisExpressionSyntax)
            {
                switch (_this)
                {
                    case ThisKind.Pointer: return $"{ThisName}->{MemberPath(_thisType, p.ContainingType, p.Name)}";
                    case ThisKind.Value: return $"{ThisName}.{p.Name}";
                }
                return Unsupported(at, "instance property access outside a struct method");
            }
            var recvType = Model(at).GetTypeInfo(recv).Type;
            var op = IsRef(recvType) ? "->" : ".";
            return $"{Expr(recv)}{op}{(op == "->" ? MemberPath(recvType, p.ContainingType, p.Name) : p.Name)}";
        }
        if (p.ReturnsByRef || p.ReturnsByRefReadonly) return Unsupported(at, $"ref-returning property or indexer '{p.Name}'");
        if (p.GetMethod == null) return Unsupported(at, $"property '{p.Name}' without a getter");
        if (!p.OriginalDefinition.Locations.Any(l => l.IsInSource))
            return Unsupported(at, $"property {p.ContainingType.ToDisplayString()}.{p.Name} (a .NET library member E# has no code for)");
        if (p.IsStatic) return $"{FuncName(p.GetMethod)}()";
        var receiver = ReceiverOf(recv, at, p.Name, "instance property access");
        if (receiver == null) return "0";
        var readArgs = new List<string>();
        if (indexes != null) readArgs.AddRange(indexes.Select(i => Expr(i, top: true)));
        return InstanceCall(p.GetMethod, receiver, readArgs, recv is BaseExpressionSyntax);
    }

    /// <summary>The receiver and index arguments of a property or indexer access.</summary>
    private static (ExpressionSyntax? recv, IReadOnlyList<ExpressionSyntax> indexes) AccessParts(ExpressionSyntax target) => target switch
    {
        MemberAccessExpressionSyntax ma => (ma.Expression, System.Array.Empty<ExpressionSyntax>()),
        ElementAccessExpressionSyntax ea => (ea.Expression, ea.ArgumentList.Arguments.Select(a => a.Expression).ToList()),
        _ => (null, System.Array.Empty<ExpressionSyntax>()),
    };

    /// <summary>The non-auto property an assignment or ++/-- writes to, or null for fields, locals and auto-properties.</summary>
    private IPropertySymbol? WrittenProperty(ExpressionSyntax target)
    {
        var sym = Model(target).GetSymbolInfo(target).Symbol;
        // Span<T> and array indexers are plain C indexing, not accessor calls.
        return sym is IPropertySymbol p && !IsAutoProperty(p) && !(p.IsIndexer && IsSpan(p.ContainingType)) ? p : null;
    }

    /// <summary>Drops the parentheses that wrap a whole condition, so clang's -Wparentheses-equality stays quiet on <c>if ((a == b))</c>. A statement expression <c>({ ... })</c> keeps them.</summary>
    private static string Cond(string c)
    {
        while (c.Length >= 2 && c[0] == '(' && c[^1] == ')' && c[1] != '{' && WholeGroup(c)) c = c.Substring(1, c.Length - 2);
        // An assignment used as a condition keeps one pair: the C idiom that tells gcc/clang -Wparentheses it is meant.
        return TopLevelAssignment(c) ? "(" + c + ")" : c;
    }

    /// <summary>True when <paramref name="c"/> has an assignment operator (=, +=, ...) outside any parentheses, brackets or literals.</summary>
    private static bool TopLevelAssignment(string c)
    {
        var depth = 0; var inStr = false; var inChr = false;
        for (var k = 0; k < c.Length; k++)
        {
            var ch = c[k];
            if (inStr || inChr) { if (ch == '\\') k++; else if (inStr && ch == '"') inStr = false; else if (inChr && ch == '\'') inChr = false; continue; }
            if (ch == '"') inStr = true; else if (ch == '\'') inChr = true;
            else if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') depth--;
            else if (ch == '=' && depth == 0)
            {
                var prev = k > 0 ? c[k - 1] : ' ';
                var next = k + 1 < c.Length ? c[k + 1] : ' ';
                if (next == '=') { k++; continue; }                       // ==
                var shift = k >= 2 && (c[k - 2] == '<' || c[k - 2] == '>') && c[k - 2] == prev;   // <<=, >>=
                if (!shift && prev is '!' or '<' or '>' or '=') continue;  // !=, <=, >=, second '=' of ==
                return true;                                               // =, +=, -=, <<=, ...
            }
        }
        return false;
    }

    private static bool WholeGroup(string c)
    {
        var depth = 0; var inStr = false; var inChr = false;
        for (var k = 0; k < c.Length; k++)
        {
            var ch = c[k];
            if (inStr || inChr) { if (ch == '\\') k++; else if (inStr && ch == '"') inStr = false; else if (inChr && ch == '\'') inChr = false; continue; }
            if (ch == '"') inStr = true; else if (ch == '\'') inChr = true;
            else if (ch == '(') depth++;
            else if (ch == ')' && --depth == 0 && k < c.Length - 1) return false;
        }
        return true;
    }

    private static bool IsStatementContext(SyntaxNode n) => n.Parent is ExpressionStatementSyntax or ForStatementSyntax;

    /// <summary>Property write as a call of the setter. Only allowed as a statement (the value of the expression is not kept).</summary>
    private string PropertyWrite(IPropertySymbol p, ExpressionSyntax target, SyntaxNode whole, Func<string, string> newValue,
        bool reads = true)
    {
        if (p.SetMethod == null) return Unsupported(whole, $"assignment to property '{p.Name}' without a setter");
        if (!IsStatementContext(whole)) return Unsupported(whole, "property assignment used as a value; make it a statement");
        var (recv, indexes) = AccessParts(target);
        if (reads && recv != null && (!IsPure(recv) || !indexes.All(IsPure)))
        {
            // Evaluate the receiver and the index arguments once into temporaries, then call the getter and the setter.
            if (p.IsStatic || p.GetMethod == null)
                return Unsupported(recv, "compound assignment or ++/-- on a property of a complex expression; assign it to a local first");
            var rcv = ReceiverOf(recv, whole, p.Name, "instance property access");
            if (rcv == null) return "0";
            var n = _patternTemp++;
            var sb = new StringBuilder($"({{ __typeof__({rcv}) es_r{n} = {rcv}; ");
            var names = new List<string> { $"es_r{n}" };
            for (var j = 0; j < indexes.Count; j++)
            {
                var ix = Expr(indexes[j], top: true);
                sb.Append($"__typeof__({ix}) es_i{n}_{j} = {ix}; ");
                names.Add($"es_i{n}_{j}");
            }
            var list = string.Join(", ", names);
            var rest = names.Skip(1).ToList();
            var current0 = InstanceCall(p.GetMethod, names[0], rest, recv is BaseExpressionSyntax);
            sb.Append($"{InstanceCall(p.SetMethod, names[0], rest.Append(newValue(current0)), recv is BaseExpressionSyntax)}; (void)0; }})");
            return sb.ToString();
        }
        var current = reads ? PropertyRead(p, recv, target, indexes) : "";
        var value = newValue(current);
        if (p.IsStatic) return $"{FuncName(p.SetMethod)}({value})";
        var receiver = ReceiverOf(recv, whole, p.Name, "instance property access");
        if (receiver == null) return "0";
        var writeArgs = new List<string>();
        writeArgs.AddRange(indexes.Select(i => Expr(i, top: true)));
        writeArgs.Add(value);
        return InstanceCall(p.SetMethod, receiver, writeArgs, recv is BaseExpressionSyntax);
    }

    /// <summary>Names, literals, member accesses and element accesses of those: safe to evaluate twice.</summary>
    private static bool IsPure(ExpressionSyntax e) => e switch
    {
        ParenthesizedExpressionSyntax p => IsPure(p.Expression),
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } nf => IsPure(nf.Operand),
        IdentifierNameSyntax or ThisExpressionSyntax or LiteralExpressionSyntax => true,
        MemberAccessExpressionSyntax ma => IsPure(ma.Expression),
        ElementAccessExpressionSyntax ea => IsPure(ea.Expression) && ea.ArgumentList.Arguments.All(a => IsPure(a.Expression)),
        _ => false,
    };

    /// <summary>The value given to a property's setter: a delegate takes a reference of its own unless it is handed over (issue #110).</summary>
    private string SetterValue(IPropertySymbol p, ExpressionSyntax value)
    {
        var plain = Expr(value, top: true);
        return IsHandoverSetter(p) ? HandOver(value, plain, p.Type, $"the setter of '{p.Name}'") : plain;
    }

    private string PropertyAssignment(IPropertySymbol p, AssignmentExpressionSyntax a)
    {
        if (a.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return PropertyWrite(p, a.Left, a, _ => SetterValue(p, a.Right), reads: false);
        var token = a.OperatorToken.Text;
        var op = token.Substring(0, token.Length - 1);
        if (op == "?" ) return Unsupported(a, "??=");
        var user = Model(a).GetSymbolInfo(a).Symbol as IMethodSymbol;
        return PropertyWrite(p, a.Left, a, cur => user is { MethodKind: MethodKind.UserDefinedOperator }
            ? $"{FuncName(user)}({cur}, {Expr(a.Right, top: true)})"
            : $"({cur} {op} {Expr(a.Right)})");
    }

    private string PropertyStep(IPropertySymbol p, ExpressionSyntax operand, SyntaxNode whole, bool increment)
    {
        if (p.Type.SpecialType == SpecialType.None || p.Type.SpecialType == SpecialType.System_Boolean)
            return Unsupported(whole, "++/-- on a property of this type");
        return PropertyWrite(p, operand, whole, cur => $"({cur} {(increment ? "+" : "-")} 1)");
    }

    private string Literal(LiteralExpressionSyntax lit)
    {
        var constant = Model(lit).GetConstantValue(lit);
        if (lit.IsKind(SyntaxKind.NullLiteralExpression)) return "NULL";
        if (!constant.HasValue)
        {
            Unsupported(lit, "literal");
            return "0";
        }
        var type = Model(lit).GetTypeInfo(lit).ConvertedType ?? Model(lit).GetTypeInfo(lit).Type!;
        return ConstantLiteral(constant.Value!, type);
    }

    private string ConstantLiteral(object value, ITypeSymbol type)
    {
        switch (value)
        {
            case bool b: return b ? "true" : "false";
            case string s: return CString(s);
            case char c: return ((int)c).ToString(CultureInfo.InvariantCulture);
            case int i when i == int.MinValue: return "(-2147483647 - 1)";
            case int i: return i.ToString(CultureInfo.InvariantCulture);
            case uint u: return u.ToString(CultureInfo.InvariantCulture) + "u";
            case long l when l == long.MinValue: return "(-9223372036854775807LL - 1)";
            case long l: return l.ToString(CultureInfo.InvariantCulture) + "LL";
            case ulong ul: return ul.ToString(CultureInfo.InvariantCulture) + "ULL";
            case byte or sbyte or short or ushort:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
            case float f: return f.ToString("R", CultureInfo.InvariantCulture) + "f";
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        }
    }

    /// <summary>C string literal. Characters above 0x7F are written as Latin-1 octal bytes, as the Amiga expects.</summary>
    private static string CString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\000"); break;
                case '?': sb.Append("\\?"); break;
                default:
                    if (ch >= 0x20 && ch < 0x7F) sb.Append(ch);
                    else sb.Append('\\').Append(Convert.ToString(ch & 0xFF, 8).PadLeft(3, '0'));
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // ------------------------------------------------------------------ output helpers

    private void Line(string text)
    {
        if (text.Length == 0) { _out.Append('\n'); return; }
        _out.Append(' ', _indent * 4).Append(text).Append('\n');
    }

    private void LineDirective(Location location)
    {
        var tree = location.SourceTree;
        if (tree == null || !_user.Contains(tree)) return;
        var span = location.GetMappedLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var file = span.Path;
        if (file == _lastLineFile && line == _lastLine) return;
        _lastLineFile = file;
        _lastLine = line;
        _out.Append("#line ").Append(line).Append(" \"").Append(file.Replace("\\", "/")).Append("\"\n");
    }
}
