using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ESharp.Analyzers;
using Microsoft.CodeAnalysis;

namespace ESharp.Compiler;

/// <summary>
/// The C of an E# Amiga shared library (EsOutputType AmigaLibrary, issue #154), and the program side's [LibraryCall] vector calls.
/// <para>
/// Per-opener data: every variable of the E# data (static fields, properties and events of all classes, the platform's included, and the
/// runtime's state: the text ring, the closure counters, the library bases opened with SetBase, the fatal-error hook) is a member of one
/// block, the opener's data, instead of a C global. Two structs make the block: <c>struct es_rt</c> (the runtime's, defined right after the
/// includes, so the runtime's helpers can use it) and <c>struct es_user</c> (the program's static fields, after the types they use), at
/// <c>ES_RT_SIZE</c> in the block. Each name becomes a macro for its member through <c>es_d</c>, the opener's block, so the rest of the
/// C is the same as a program's. On m68k <c>es_d</c> is a global register variable in a4 (no other code in the file uses a4); each library
/// function's entry saves the caller's a4, points it at the data block of the opener whose base it was called with (a6), and restores it.
/// So two tasks in two openers run at the same time with their own data, and the library never needs a global "current opener".
/// </para>
/// <para>
/// Open copies the library base (its jump table and head) into a new block that also holds the opener's data, zeroed, then runs the static
/// initializers and constructors in it (on the caller's task, under exec's Forbid); the copy is what OpenLibrary returns. Close runs the
/// [AtExit] methods, frees the opener's memory pool (every object the E# code allocated) and the copy, and drops the open count of the base
/// in exec's library list; an expunge that was asked for while the library was open happens then. SysBase and the bases of the ROM
/// libraries the code calls (dos, graphics, intuition) are shared, opened once at the library's init and closed at expunge.
/// </para>
/// </summary>
internal sealed partial class CEmitter
{
    /// <summary>An Amiga library build: the functions to export, in vector order.</summary>
    public AmigaLibraryPlan? AmigaLibrary { get; init; }

    private bool LibMode => AmigaLibrary != null;

    /// <summary>Stands for the runtime's per-opener struct until the whole preamble (and so every runtime variable) is known.</summary>
    private const string LibRtMarker = "/* ES_LIB_RT */\n";

    private readonly List<string> _libRt = new();                   // member declarations of struct es_rt
    private readonly List<string> _libUser = new();                 // member declarations of struct es_user
    private readonly List<(string Name, string Template)> _libInit = new();   // a static's initial value: the template that is copied in

    /// <summary>The C function that frees what es_new gave: the opener's pool in a library, else the C library's free.</summary>
    private string FreeName => LibMode ? "es_lib_free" : "free";

    private static readonly Regex FunctionPointerName = new(@"\(\s*\*\s*(\w+)\s*\)", RegexOptions.CultureInvariant);
    private static readonly Regex VariableName = new(@"(\w+)\s*(\[[^\]]*\]\s*)*$", RegexOptions.CultureInvariant);

    /// <summary>The variable a C declaration declares (<c>char es_fmt_buf[8][128]</c>: es_fmt_buf; <c>void (*hook)(void)</c>: hook).</summary>
    internal static string DeclaredName(string declaration)
    {
        var d = declaration.Trim().TrimEnd(';').Trim();
        var eq = d.IndexOf('=');
        if (eq >= 0) d = d[..eq].Trim();
        var fp = FunctionPointerName.Match(d);
        if (fp.Success) return fp.Groups[1].Value;
        var m = VariableName.Match(d);
        return m.Success ? m.Groups[1].Value : d;
    }

    /// <summary>Library mode: a runtime variable goes into the opener's data; nothing is written where it stood.</summary>
    private string LibRt(string declaration)
    {
        _libRt.Add(declaration.Trim().TrimEnd(';'));
        return "";
    }

    /// <summary>The block that replaces <see cref="LibRtMarker"/>: struct es_rt, the opener's data pointer and a macro for each member.</summary>
    private string LibRtBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine("/* E# .library (issue #154): the data of one opener. Every E# variable is a member of the opener's data block, reached through es_d;");
        sb.AppendLine("   the runtime's part comes first, the program's static fields (struct es_user) at ES_RT_SIZE. */");
        sb.AppendLine("struct es_rt");
        sb.AppendLine("{");
        foreach (var d in _libRt) sb.AppendLine("    " + d + ";");
        sb.AppendLine("    void *es_heap;   /* the opener's memory: everything es_new gave, freed when the opener closes */");
        sb.AppendLine("};");
        sb.AppendLine("#define ES_RT_SIZE ((sizeof(struct es_rt) + 7) & ~(size_t)7)");
        sb.AppendLine("#if defined(__mc68000__)");
        sb.AppendLine("/* a4 holds the opener's data while library code runs: each function's entry sets it from the base in a6, and restores the caller's. */");
        sb.AppendLine("register struct es_rt *es_d __asm__(\"a4\");");
        sb.AppendLine("#else");
        sb.AppendLine("static struct es_rt *es_d;   /* not m68k (the host tests): the opener whose data the code uses now */");
        sb.AppendLine("#endif");
        foreach (var d in _libRt)
        {
            var name = DeclaredName(d);
            sb.AppendLine($"#define {name} (es_d->{name})");
        }
        sb.AppendLine("static __attribute__((unused)) void *es_lib_alloc(size_t n);");
        sb.AppendLine("static __attribute__((unused)) void es_lib_free(void *p);");
        sb.AppendLine("static __attribute__((unused)) void es_lib_fatal(void);");
        sb.AppendLine("static int es_lib_heap_init(void);");
        sb.AppendLine("static void es_lib_heap_fini(void);");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Library mode: a static field, property or event of the E# data. Its initial value, a C constant, is copied in when an opener opens.</summary>
    private void LibStatic(string member, string name, string? template)
    {
        _libUser.Add(member);
        if (template != null) _libInit.Add((name, template));
    }

    /// <summary>struct es_user, after the types: the program's static fields, and a macro for each.</summary>
    private void EmitLibraryUserData()
    {
        Line("/* The static fields, properties and events of one opener (in its data block, at ES_RT_SIZE). */");
        Line("struct es_user");
        Line("{");
        foreach (var m in _libUser) Line("    " + m + ";");
        Line("    char es_end;");
        Line("};");
        Line("#define ES_USER ((struct es_user *)((char *)es_d + ES_RT_SIZE))");
        Line("#define ES_LIB_DATA_SIZE (ES_RT_SIZE + sizeof(struct es_user))");
        foreach (var m in _libUser)
        {
            var name = DeclaredName(m);
            Line($"#define {name} (ES_USER->{name})");
        }
        foreach (var (_, template) in _libInit) Line("static " + template + ";");
        Line("");
    }

    /// <summary>What an E# .library cannot hold yet (ES0134); run after the body is emitted.</summary>
    private void CheckLibraryFeatures(IEnumerable<IFieldSymbol> fields)
    {
        void Report(Location at, string what, string why) => _diagnostics.Add(Diagnostic.Create(Descriptors.LibraryUnsupported, at, what, why));
        foreach (var f in fields.Where(f => HasAttribute(f, "ChipRamAttribute")))
            Report(f.Locations.FirstOrDefault() ?? Location.None, $"chip RAM data ([ChipRam] {f.ContainingType.Name}.{f.Name})", "each opener has its own copy of the E# data, and that copy is not in chip RAM");
        foreach (var m in _interrupts)
            Report(m.Locations.FirstOrDefault() ?? Location.None, $"an [Interrupt] server ({m.Name})", "an interrupt does not run with an opener's data");
        foreach (var m in _hookEntries)
            Report(m.Locations.FirstOrDefault() ?? Location.None, $"a [HookEntry] method ({m.Name}; also BOOPSI hooks and MUI dispatchers)", "the system calls a hook without an opener's data");
        if (UsesArgs) Report(Location.None, "Args", "a library has no command line");
        foreach (var h in _headers.Where(h => h.StartsWith("proto/", StringComparison.Ordinal) || h.StartsWith("clib/", StringComparison.Ordinal)))
        {
            var lib = h.StartsWith("clib/", StringComparison.Ordinal) ? "alib" : h["proto/".Length..^".h".Length];
            if (lib is "exec" or "dos" or "graphics" or "intuition" || _baseLibs.ContainsValue(lib)) continue;
            Report(Location.None, lib == "alib" ? "amiga.lib functions (DoMethod, ...)" : $"{lib}.library calls",
                lib == "alib" ? "a .library links no link library but libgcc" : "a .library opens exec, dos, graphics and intuition itself, and others only through a class with [LibraryBase] (SetBase)");
        }
    }

    /// <summary>The ROM libraries a library's code calls through the usual base names: opened at the library's init, shared by every opener.</summary>
    private IEnumerable<(string Base, string Library)> SharedBases()
    {
        if (_headers.Contains("proto/dos.h")) yield return ("DOSBase", "dos.library");
        if (_headers.Contains("proto/graphics.h")) yield return ("GfxBase", "graphics.library");
        if (_headers.Contains("proto/intuition.h")) yield return ("IntuitionBase", "intuition.library");
    }

    /// <summary>The opener's data (set up, torn down), a C entry for each library function, and on m68k the library itself.</summary>
    private void EmitAmigaLibraryGlue()
    {
        var plan = AmigaLibrary!;
        var o = plan.Options;
        Line("/* ---- The opener's data: set up when an opener opens, torn down when it closes. */");
        Line("static __attribute__((unused)) int es_lib_data_init(void *es_data)");
        Line("{");
        _indent++;
        Line("struct es_rt *es_saved = es_d;");
        Line("es_d = (struct es_rt *)es_data;");
        Line("int es_ok = es_lib_heap_init();");
        Line("if (es_ok)");
        Line("{");
        _indent++;
        foreach (var (name, _) in _libInit) Line($"__builtin_memcpy(&{name}, &{name}__init, sizeof {name}__init);");
        // The static initializers that are not C constants, then the static constructors (in a program: at the start of main).
        EmitDeferredStatics();
        foreach (var cctor in _staticConstructors) Line(FuncName(cctor) + "();");
        _indent--;
        Line("}");
        Line("es_d = es_saved;");
        Line("return es_ok;");
        _indent--;
        Line("}");
        Line("");
        Line("static __attribute__((unused)) void es_lib_data_fini(void *es_data)");
        Line("{");
        _indent++;
        Line("struct es_rt *es_saved = es_d;");
        Line("es_d = (struct es_rt *)es_data;");
        // [AtExit] methods: in a program they run after the entry point returns; in a library, when the opener closes.
        foreach (var m in _atExit) Line(FuncName(m) + "();");
        Line("es_lib_heap_fini();");
        Line("es_d = es_saved;");
        _indent--;
        Line("}");
        Line("");
        Line("/* Each library function with the opener's data: called by its vector's entry (m68k) and by the host tests. */");
        foreach (var (m, _, _) in plan.Functions)
        {
            var node = m.DeclaringSyntaxReferences.First().GetSyntax();
            var ret = m.ReturnsVoid ? "void" : CType(m.ReturnType, node);
            var pars = string.Concat(m.Parameters.Select(p => ", " + Decl(p.Type, p.Name, node)));
            var args = string.Join(", ", m.Parameters.Select(p => p.Name));
            Line($"static __attribute__((unused)) {(ret.EndsWith("*") ? ret : ret + " ")}es_lib_call_{m.Name}(void *es_data{pars})");
            Line("{");
            _indent++;
            Line("struct es_rt *es_saved = es_d;");
            Line("es_d = (struct es_rt *)es_data;");
            if (m.ReturnsVoid) Line($"{FuncName(m)}({args});");
            else Line($"{(ret.EndsWith("*") ? ret : ret + " ")}es_result = {FuncName(m)}({args});");
            Line("es_d = es_saved;");
            if (!m.ReturnsVoid) Line("return es_result;");
            _indent--;
            Line("}");
        }
        Line("");

        string CString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        var bases = SharedBases().ToList();
        foreach (var l in new[]
        {
            "#if defined(__mc68000__)",
            "/* ---- The library: RomTag (at the start of the file, below), init, open, close, expunge, the vectors' entries. Our own code: no C startup is linked. */",
            "#include <exec/types.h>",
            "#include <exec/libraries.h>",
            "#include <exec/execbase.h>",
            "#include <exec/memory.h>",
            "#include <proto/exec.h>",
            "struct ExecBase *SysBase;",
        }) Line(l);
        foreach (var (b, _) in bases) Line($"__typeof__({b}) {b};   /* shared by every opener */");
        foreach (var l in new[]
        {
            "/* The library base: exec's struct Library, then ours. An opener's copy has its data block right after it. */",
            "struct es_lib_head { struct Library lib; struct es_lib_head *master; uint32_t seglist; uint32_t size; };   /* size: an opener's allocation (lib_PosSize is a UWORD) */",
            "#define ES_LIB_DATA(b) ((void *)((char *)(b) + sizeof(struct es_lib_head)))",
            $"static const char es_lib_name[] __attribute__((used)) = {CString(o.Name)};",
            $"static const char es_lib_id[] __attribute__((used)) = {CString(o.IdString + "\r\n")};",
            $"static const char es_lib_version[] __attribute__((used)) = {CString("$VER: " + o.IdString)};",
            "static void es_lib_close_bases(void)",
            "{",
        }) Line(l);
        foreach (var (b, _) in bases) Line($"    if ({b} != NULL) {{ CloseLibrary((struct Library *){b}); {b} = NULL; }}");
        Line("}");
        Line("static int es_lib_open_bases(void)");
        Line("{");
        foreach (var (b, lib) in bases)
            Line($"    if (({b} = (__typeof__({b}))OpenLibrary((CONST_STRPTR){CString(lib)}, 39)) == NULL) {{ es_lib_close_bases(); return 0; }}");
        Line("    return 1;");
        Line("}");
        foreach (var l in new[]
        {
            "static void es_lib_free_base(struct es_lib_head *b)",
            "{",
            "    FreeMem((char *)b - b->lib.lib_NegSize, b->size != 0 ? b->size : (ULONG)b->lib.lib_NegSize + b->lib.lib_PosSize);",
            "}",
            "/* InitResident (from the RomTag's init table, after MakeLibrary): d0 the new base, a0 the segment list, a6 SysBase. */",
            "static __attribute__((used)) struct Library *es_lib_init(struct es_lib_head *base __asm(\"d0\"), uint32_t seglist __asm(\"a0\"), struct ExecBase *sys __asm(\"a6\"))",
            "{",
            "    SysBase = sys;",
            "    base->lib.lib_Node.ln_Type = NT_LIBRARY;",
            "    base->lib.lib_Node.ln_Name = (char *)es_lib_name;",
            "    base->lib.lib_Flags = LIBF_SUMUSED | LIBF_CHANGED;",
            $"    base->lib.lib_Version = {o.Version};",
            $"    base->lib.lib_Revision = {o.Revision};",
            "    base->lib.lib_IdString = (APTR)es_lib_id;",
            "    base->master = base;",
            "    base->seglist = seglist;",
            "    if (!es_lib_open_bases()) { es_lib_free_base(base); return NULL; }",
            "    return &base->lib;",
            "}",
            "/* Expunge (a6: the base in exec's list): only when nobody has it open; else when the last opener closes. */",
            "static uint32_t es_lib_expunge(struct es_lib_head *base __asm(\"a6\"))",
            "{",
            "    base = base->master;",
            "    if (base->lib.lib_OpenCnt != 0) { base->lib.lib_Flags |= LIBF_DELEXP; return 0; }",
            "    uint32_t seglist = base->seglist;",
            "    Remove(&base->lib.lib_Node);",
            "    es_lib_close_bases();",
            "    es_lib_free_base(base);",
            "    return seglist;",
            "}",
            "/* Open (a6: the base in exec's list, under Forbid): a copy of the base for this opener, with its own data block. */",
            "static struct Library *es_lib_open(struct es_lib_head *base __asm(\"a6\"), uint32_t version __asm(\"d0\"))",
            "{",
            "    (void)version;   /* exec has checked it against lib_Version */",
            "    ULONG neg = base->lib.lib_NegSize, pos = sizeof(struct es_lib_head) + ES_LIB_DATA_SIZE;",
            "    char *mem = (char *)AllocMem(neg + pos, MEMF_PUBLIC | MEMF_CLEAR);",
            "    if (mem == NULL) return NULL;",
            "    CopyMem((char *)base - neg, mem, neg + sizeof(struct es_lib_head));   /* the jump table and the head */",
            "    CacheClearU();   /* the jump table is code */",
            "    struct es_lib_head *copy = (struct es_lib_head *)(mem + neg);",
            "    copy->lib.lib_PosSize = (UWORD)(pos < 65535 ? pos : 65535);",
            "    copy->size = neg + pos;",
            "    copy->lib.lib_OpenCnt = 1;",
            "    copy->master = base;",
            "    if (!es_lib_data_init(ES_LIB_DATA(copy))) { FreeMem(mem, neg + pos); return NULL; }   /* no memory pool: nothing else was made */",
            "    base->lib.lib_OpenCnt++;",
            "    base->lib.lib_Flags &= ~LIBF_DELEXP;",
            "    return &copy->lib;",
            "}",
            "/* Close (a6: the opener's copy): its data goes, then the copy; the last close does a pending expunge. */",
            "static uint32_t es_lib_close(struct es_lib_head *copy __asm(\"a6\"))",
            "{",
            "    struct es_lib_head *base = copy->master;",
            "    if (copy == base) return 0;   /* not an opener's copy */",
            "    es_lib_data_fini(ES_LIB_DATA(copy));",
            "    es_lib_free_base(copy);",
            "    if (base->lib.lib_OpenCnt > 0) base->lib.lib_OpenCnt--;",
            "    if (base->lib.lib_OpenCnt == 0 && (base->lib.lib_Flags & LIBF_DELEXP)) return es_lib_expunge(base);",
            "    return 0;",
            "}",
            "static uint32_t es_lib_reserved(void) { return 0; }",
        }) Line(l);
        Line("/* The vectors' entries: arguments in the registers of the .fd file, the opener's base in a6. */");
        foreach (var (m, regs, offset) in plan.Functions)
        {
            var node = m.DeclaringSyntaxReferences.First().GetSyntax();
            var pars = string.Concat(m.Parameters.Select((p, i) => $"uintptr_t {p.Name} __asm(\"{regs[i]}\"), "));
            var args = string.Concat(m.Parameters.Select(p => $", ({CType(p.Type, node)}){p.Name}"));
            var call = $"es_lib_call_{m.Name}(ES_LIB_DATA(es_base){args})";
            Line($"static {(m.ReturnsVoid ? "void" : "uintptr_t")} es_lib_entry_{m.Name}({pars}struct es_lib_head *es_base __asm(\"a6\"))   /* {offset.ToString(CultureInfo.InvariantCulture)} */");
            Line(m.ReturnsVoid ? $"{{ {call}; }}" : $"{{ return (uintptr_t){call}; }}");
        }
        Line("static const APTR es_lib_vectors[] =");
        Line("{");
        Line("    (APTR)es_lib_open, (APTR)es_lib_close, (APTR)es_lib_expunge, (APTR)es_lib_reserved,");
        foreach (var (m, _, _) in plan.Functions) Line($"    (APTR)es_lib_entry_{m.Name},");
        Line("    (APTR)-1,");
        Line("};");
        foreach (var l in new[]
        {
            "/* RTF_AUTOINIT: InitResident calls MakeLibrary with the base size, the vectors and no init struct, then es_lib_init. */",
            "static const uint32_t es_lib_inittab[4] __attribute__((used)) = { sizeof(struct es_lib_head), (uint32_t)es_lib_vectors, 0, (uint32_t)es_lib_init };",
            "/* The opener's memory: an exec pool, so closing frees all of it at once. */",
            "static __attribute__((unused)) int es_lib_heap_init(void) { es_d->es_heap = CreatePool(MEMF_PUBLIC | MEMF_CLEAR, 4096, 1024); return es_d->es_heap != NULL; }",
            "static __attribute__((unused)) void es_lib_heap_fini(void) { if (es_d->es_heap != NULL) { DeletePool(es_d->es_heap); es_d->es_heap = NULL; } }",
            "static __attribute__((unused)) void *es_lib_alloc(size_t n)",
            "{",
            "    uint32_t *p = (uint32_t *)AllocPooled(es_d->es_heap, (ULONG)n + 8);",
            "    if (p == NULL) return NULL;",
            "    p[0] = (uint32_t)n + 8;",
            "    __builtin_memset(p + 2, 0, n);",
            "    return p + 2;",
            "}",
            "static __attribute__((unused)) void es_lib_free(void *q)",
            "{",
            "    if (q == NULL) return;",
            "    uint32_t *p = (uint32_t *)q - 2;",
            "    FreePooled(es_d->es_heap, p, p[0]);",
            "}",
            "/* A fatal error of the E# runtime (out of memory, an index out of range with --debug) in library code: there is no program to end, so",
            "   a recoverable alert, then the calling task waits for ever. */",
            "static __attribute__((unused)) void es_lib_fatal(void) { Alert(0x000100E5UL); Wait(0); for (;;) { } }",
            "/* What gcc may call for a struct copy or clear (no C library is linked). */",
            "void *memcpy(void *d, const void *s, size_t n) { char *a = (char *)d; const char *b = (const char *)s; while (n--) *a++ = *b++; return d; }",
            "void *memmove(void *d, const void *s, size_t n)",
            "{",
            "    char *a = (char *)d; const char *b = (const char *)s;",
            "    if (a < b) while (n--) *a++ = *b++; else { a += n; b += n; while (n--) *--a = *--b; }",
            "    return d;",
            "}",
            "void *memset(void *d, int c, size_t n) { char *a = (char *)d; while (n--) *a++ = (char)c; return d; }",
            "int memcmp(const void *x, const void *y, size_t n) { const unsigned char *a = x, *b = y; for (; n--; a++, b++) if (*a != *b) return *a - *b; return 0; }",
            "#else",
            "/* Not m68k (the host tests): the opener's memory is a list of calloc blocks, freed when the opener closes. */",
            "#include <stdlib.h>",
            "struct es_lib_block { struct es_lib_block *next, *prev; uint64_t align; };",
            "static __attribute__((unused)) int es_lib_heap_init(void) { es_d->es_heap = NULL; return 1; }",
            "static __attribute__((unused)) void *es_lib_alloc(size_t n)",
            "{",
            "    struct es_lib_block *b = (struct es_lib_block *)calloc(1, sizeof *b + n);",
            "    if (b == NULL) return NULL;",
            "    b->next = (struct es_lib_block *)es_d->es_heap;",
            "    if (b->next != NULL) b->next->prev = b;",
            "    es_d->es_heap = b;",
            "    return b + 1;",
            "}",
            "static __attribute__((unused)) void es_lib_free(void *q)",
            "{",
            "    if (q == NULL) return;",
            "    struct es_lib_block *b = (struct es_lib_block *)q - 1;",
            "    if (b->prev != NULL) b->prev->next = b->next; else es_d->es_heap = b->next;",
            "    if (b->next != NULL) b->next->prev = b->prev;",
            "    free(b);",
            "}",
            "static __attribute__((unused)) void es_lib_heap_fini(void)",
            "{",
            "    struct es_lib_block *b = (struct es_lib_block *)es_d->es_heap;",
            "    while (b != NULL) { struct es_lib_block *n = b->next; free(b); b = n; }",
            "    es_d->es_heap = NULL;",
            "}",
            "static __attribute__((unused)) void es_lib_fatal(void) { abort(); }",
            "#endif",
        }) Line(l);
    }

    /// <summary>The start of a library's C: on m68k the first code of the file returns -1 (a library run as a command), and the RomTag follows at once.</summary>
    private string LibraryRomTag()
    {
        var o = AmigaLibrary!.Options;
        // ramlib (Kickstart 3.1) found no RomTag 470 bytes into the hunk, but finds one right after the first two instructions; top-level asm comes
        // before the functions in gcc's output, and this file is the first object of the link (no C startup), so the hunk starts with it.
        return string.Join("\n",
            "#if defined(__mc68000__)",
            "__asm__(\".text\\n\"",
            "        \"\\t.globl _es_lib_start\\n\"",
            "        \"_es_lib_start:\\n\"",
            "        \"\\tmoveq #-1,d0\\n\"",
            "        \"\\trts\\n\"",
            "        \"_es_lib_romtag:\\n\"",
            "        \"\\t.word 0x4afc\\n\"                 /* RTC_MATCHWORD */",
            "        \"\\t.long _es_lib_romtag\\n\"         /* rt_MatchTag */",
            "        \"\\t.long _es_lib_romtag_end\\n\"     /* rt_EndSkip */",
            $"        \"\\t.byte 0x80,{o.Version},9,0\\n\"   /* RTF_AUTOINIT, rt_Version, NT_LIBRARY, rt_Pri */",
            "        \"\\t.long _es_lib_name\\n\"",
            "        \"\\t.long _es_lib_id\\n\"",
            "        \"\\t.long _es_lib_inittab\\n\"",
            "        \"_es_lib_romtag_end:\\n\");",
            "#endif",
            "");
    }

    // ------------------------------------------------------------------ program side: [LibraryCall]

    /// <summary>A [LibraryCall] extern (the bindings of an E# .library): a C function that calls the vector with the base from its first argument.</summary>
    private void EmitLibraryCall(IMethodSymbol m)
    {
        var node = m.DeclaringSyntaxReferences.First().GetSyntax();
        var a = GetAttribute(m, "LibraryCallAttribute")!;
        var display = m.ContainingType.Name + "." + m.Name;
        void Bad(string why) => _diagnostics.Add(Diagnostic.Create(Descriptors.LibraryCallForm, m.Locations[0], display, why));
        if (a.ConstructorArguments.Length != 2 || a.ConstructorArguments[0].Value is not int offset || a.ConstructorArguments[1].Value is not string regText)
        {
            Bad("the attribute needs a vector offset and a register list");
            return;
        }
        if (!m.IsStatic || !m.IsExtern) { Bad("it must be static extern (the call is its body)"); return; }
        if (offset > AmigaLibraries.FirstOffset || offset % 6 != 0) { Bad($"the vector offset {offset} must be -30 or less, in steps of 6"); return; }
        if (m.Parameters.Length == 0 || m.Parameters[0].Type is not IPointerTypeSymbol || m.Parameters.Any(p => p.RefKind != RefKind.None))
        {
            Bad("its first parameter must be the library base (a pointer), and no parameter may be ref, out or in");
            return;
        }
        var regs = regText.Length == 0 ? Array.Empty<string>() : regText.Split(',').Select(r => r.Trim().ToLowerInvariant()).ToArray();
        if (regs.Length != m.Parameters.Length - 1) { Bad($"it names {regs.Length} registers for {m.Parameters.Length - 1} arguments after the base"); return; }
        var valid = new HashSet<string>(Enumerable.Range(0, 8).Select(i => "d" + i).Concat(Enumerable.Range(0, 6).Select(i => "a" + i)));
        if (regs.FirstOrDefault(r => !valid.Contains(r)) is { } badReg) { Bad($"'{badReg}' is not an argument register (d0-d7, a0-a5)"); return; }
        if (regs.Distinct().Count() != regs.Length) { Bad("a register is named twice"); return; }
        foreach (var p in m.Parameters.Skip(1).Append(null))
        {
            var t = p?.Type ?? (m.ReturnsVoid ? null : m.ReturnType);
            if (t == null) continue;
            if (!(t is IPointerTypeSymbol || t.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte
                    or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_IntPtr
                    or SpecialType.System_UIntPtr or SpecialType.System_Char))
            {
                Bad($"{(p == null ? "the result" : "parameter '" + p.Name + "'")} has type '{t.ToDisplayString()}'; a register holds numbers up to 32 bits, bool, string and pointers");
                return;
            }
        }
        if (!IsDefined("M68K"))
        {
            Unsupported(node, $"[LibraryCall] '{display}' on this target (vector calls of an E# .library are m68k only for now)");
            return;
        }
        Line(Prototype(m));
        Line("{");
        _indent++;
        Line($"register void *es_a6 __asm(\"a6\") = (void *){m.Parameters[0].Name};");
        var scratch = new[] { "d0", "d1", "a0", "a1" };
        for (var i = 0; i < regs.Length; i++) Line($"register uintptr_t es_{regs[i]} __asm(\"{regs[i]}\") = (uintptr_t)({m.Parameters[i + 1].Name});");
        foreach (var s in scratch.Where(s => !regs.Contains(s))) Line($"register uintptr_t es_{s} __asm(\"{s}\");");
        var outputs = scratch.Select(s => (regs.Contains(s) ? "\"+r\"" : "\"=r\"") + $"(es_{s})");
        var inputs = regs.Where(r => !scratch.Contains(r)).Select(r => $"\"r\"(es_{r})").Prepend("\"r\"(es_a6)");
        Line($"__asm volatile(\"jsr {offset.ToString(CultureInfo.InvariantCulture)}(%%a6)\" : {string.Join(", ", outputs)} : {string.Join(", ", inputs)} : \"memory\", \"cc\");");
        if (!m.ReturnsVoid) Line($"return ({CType(m.ReturnType, node)})es_d0;");
        else Line("(void)es_d0;");
        _indent--;
        Line("}");
        Line("");
    }
}
