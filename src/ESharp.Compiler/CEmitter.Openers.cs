using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ESharp.Compiler;

/// <summary>
/// sdl: per-opener data for the E# Amiga libraries a program compiles in (issue #154). On sdl a program that names a library project in
/// esharp.include compiles the library's code into itself and calls it through the binding, whose struct carries
/// <c>[LibraryBinding(name, types)]</c>. As on the Amiga, each opener has its own copy of the library's data:
/// <list type="bullet">
/// <item>The static fields, properties and events of the library's types (and their nested types) are members of
/// <c>struct es_ol_N</c>, one per library, and each C name is a macro for its member through <c>es_ol_N_d</c>, the opener whose
/// call is running. Outside a call it points at a zeroed block of its own (<c>es_ol_N_none</c>), which no library code should see.</item>
/// <item><c>SharedLibrary.Open</c> (es_ol_open) allocates an opener's block, copies the constant initial values in and runs the
/// types' other static initializers and static constructors in it (not at the start of main); <c>Close</c> (es_ol_close) runs the
/// types' [AtExit] methods in it (not at the end of main), frees every object the library's code made for that opener, and the block.</item>
/// <item>The binding's <c>SharedLibrary.Enter</c>/<c>Leave</c> around each call point <c>es_ol_N_d</c> at the opener's block and
/// <c>es_ol_heap</c> at its list of objects: <c>es_new</c> then gives each object a small header that links it into that list, and
/// <c>Heap.Free</c> unlinks it. Objects made outside a call are not in any list.</item>
/// </list>
/// The sdl program is one thread, so the one pointer per library is enough with any number of openers alive: every call sets it from
/// its own opener. Enter and Leave nest (a stack of the previous pointers and object list), so a call made from inside another opener's call
/// puts that opener back at its Leave (issue #178). The runtime's own state (the text ring, the closure counters) stays the program's, as do the platform's statics.
/// When the program links precompiled package binaries (whose objects are freed with the C library's free), objects are not given
/// the header: the static data is still per opener, but an opener's objects are then not freed at its Close.
/// </summary>
internal sealed partial class CEmitter
{
    private sealed class OpenerLibrary
    {
        public required string Name { get; init; }
        public required int Index { get; init; }
        public required HashSet<string> Types { get; init; }
        public List<string> Members { get; } = new();
        public List<(string Name, string Template)> Init { get; } = new();
        public List<IMethodSymbol> Constructors { get; } = new();
        public List<IMethodSymbol> AtExit { get; } = new();
        public string Struct => $"es_ol_{Index.ToString(CultureInfo.InvariantCulture)}";
    }

    private readonly List<OpenerLibrary> _openerLibraries = new();

    /// <summary>The program keeps per-opener data for some library compiled into it (sdl only).</summary>
    private bool OpenerMode => _openerLibraries.Count > 0;

    /// <summary>Objects made during a library call belong to its opener (not with precompiled package binaries, see the class summary).</summary>
    private bool OpenerHeap => OpenerMode && !Split.Active;

    /// <summary>The bindings with [LibraryBinding] among the program's types, on sdl (a program build, not a library or a package's binary).</summary>
    private void FindOpenerLibraries(IEnumerable<ITypeSymbol> types)
    {
        if (!IsDefined("SDL") || IsDefined("AMIGA") || LibMode || Split.Library) return;
        foreach (var t in types.OfType<INamedTypeSymbol>())
        {
            if (GetAttribute(t, "LibraryBindingAttribute") is not { ConstructorArguments: [{ Value: string name }, { Value: string list }] }) continue;
            if (_openerLibraries.Any(l => l.Name == name)) continue;
            var set = new HashSet<string>(list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
            if (set.Count > 0) _openerLibraries.Add(new OpenerLibrary { Name = name, Index = _openerLibraries.Count, Types = set });
        }
    }

    /// <summary>The library whose per-opener data holds <paramref name="member"/> (a static of one of its types), or null.</summary>
    private OpenerLibrary? OpenerOf(ISymbol member)
    {
        if (!OpenerMode) return null;
        var t = member.ContainingType;
        if (t == null) return null;
        while (true)
        {
            if (t.IsGenericType) return null;   // a generic type has no statics of its own to keep apart (as on the host)
            if (t.ContainingType == null) break;
            t = t.ContainingType;
        }
        var name = t.OriginalDefinition.ToDisplayString();
        foreach (var l in _openerLibraries)
            if (l.Types.Contains(name)) return l;
        return null;
    }

    /// <summary>A static of a library's type: a member of its opener struct; a constant initial value is copied in when an opener opens.</summary>
    private static void OpenerStatic(OpenerLibrary library, string member, string name, string? template)
    {
        library.Members.Add(member);
        if (template != null) library.Init.Add((name, template));
    }

    /// <summary>After the static fields: each library's opener struct, its "no opener" block, the current pointer and a macro per member.</summary>
    private void EmitOpenerData()
    {
        foreach (var l in _openerLibraries)
        {
            Line($"/* The static data of {l.Name} for one opener (sdl: the library is compiled in; each SharedLibrary.Open has its own). */");
            Line($"struct {l.Struct}");
            Line("{");
            foreach (var m in l.Members) Line("    " + m + ";");
            Line("    void *es_heap;   /* the objects the library's code made for this opener: freed when it closes */");
            Line("};");
            Line($"static struct {l.Struct} {l.Struct}_none;   /* outside a call: no opener */");
            Line($"static struct {l.Struct} *{l.Struct}_d = &{l.Struct}_none;   /* the opener whose call is running */");
            foreach (var m in l.Members)
            {
                var name = DeclaredName(m);
                Line($"#define {name} ({l.Struct}_d->{name})");
            }
            foreach (var (_, template) in l.Init) Line("static " + template + ";");
            Line("");
        }
    }

    /// <summary>
    /// Before main: takes the libraries' static constructors and [AtExit] methods out of main's lists, and writes each library's opener set-up
    /// and tear-down and the natives of the sdl SharedLibrary (es_ol_open, es_ol_slot, es_ol_close, es_ol_enter, es_ol_leave).
    /// </summary>
    private void EmitOpenerGlue()
    {
        var used = _natives.Any(n => n.StartsWith("es_ol_", StringComparison.Ordinal));
        if (!OpenerMode && !used) return;
        _usesAlloc = true;   // calloc and free (stdlib.h)
        _openerGlue = true;
        foreach (var l in _openerLibraries)
        {
            l.Constructors.AddRange(_staticConstructors.Where(c => OpenerOf(c) == l));
            l.AtExit.AddRange(_atExit.Where(m => OpenerOf(m) == l));
        }
        _staticConstructors.RemoveAll(c => OpenerOf(c) != null);
        _atExit.RemoveAll(m => OpenerOf(m) != null);

        foreach (var l in _openerLibraries)
        {
            var s = l.Struct;
            Line($"/* {l.Name}: a new opener's data, set up as the Amiga library's Open does (constant values, static initializers, static constructors). */");
            Line($"static void *{s}_make(void)");
            Line("{");
            _indent++;
            Line($"struct {s} *es_data = (struct {s} *)calloc(1, sizeof(struct {s}));");
            Line("if (es_data == NULL) return NULL;");
            Line($"struct {s} *es_saved = {s}_d;");
            Line("void **es_saved_heap = es_ol_heap;");
            Line($"{s}_d = es_data;");
            Line(OpenerHeap ? "es_ol_heap = &es_data->es_heap;" : "es_ol_heap = NULL;");
            foreach (var (name, _) in l.Init) Line($"__builtin_memcpy(&{name}, &{name}__init, sizeof {name}__init);");
            EmitDeferredStatics(sym => OpenerOf(sym) == l);
            foreach (var c in l.Constructors) Line(FuncName(c) + "();");
            Line($"{s}_d = es_saved;");
            Line("es_ol_heap = es_saved_heap;");
            Line("return es_data;");
            _indent--;
            Line("}");
            Line($"/* {l.Name}: an opener closes: its [AtExit] methods, then its objects and its data go. */");
            Line($"static void {s}_free(void *es_p)");
            Line("{");
            _indent++;
            Line($"struct {s} *es_data = (struct {s} *)es_p;");
            Line($"struct {s} *es_saved = {s}_d;");
            Line("void **es_saved_heap = es_ol_heap;");
            Line($"{s}_d = es_data;");
            Line(OpenerHeap ? "es_ol_heap = &es_data->es_heap;" : "es_ol_heap = NULL;");
            foreach (var m in l.AtExit) Line(FuncName(m) + "();");
            Line($"{s}_d = es_saved;");
            Line("es_ol_heap = es_saved_heap;");
            Line("es_ol_heap_fini(&es_data->es_heap);");
            Line("free(es_data);");
            _indent--;
            Line("}");
        }
        string Cases(Func<OpenerLibrary, string> body) =>
            string.Concat(_openerLibraries.Select(l => $" case {l.Index.ToString(CultureInfo.InvariantCulture)}: {body(l)} break;"));
        foreach (var l in new[]
        {
            "/* SharedLibrary on sdl: an opener is the library's number, the SharedLibrary slot and its data. */",
            "#define ES_OL_DEPTH 256   /* the deepest nesting of library calls */",
            "struct es_ol_opener { int32_t lib; int32_t slot; void *data; };",
            "static __attribute__((unused)) int es_ol_same(const char *a, const char *b) { while (*a != 0 && *a == *b) { a++; b++; } return *a == *b; }",
            "static __attribute__((unused)) void *es_ol_open(const char *name, int32_t slot)",
            "{",
            "    struct es_ol_opener *o = (struct es_ol_opener *)calloc(1, sizeof *o);",
            "    if (o == NULL || name == NULL) { free(o); return NULL; }",
            "    o->lib = -1;",
            "    o->slot = slot;",
        }) Line(l);
        foreach (var l in _openerLibraries)
            Line($"    if (o->lib < 0 && es_ol_same(name, {LibCString(l.Name)})) {{ o->lib = {l.Index.ToString(CultureInfo.InvariantCulture)}; o->data = {l.Struct}_make(); if (o->data == NULL) {{ free(o); return NULL; }} }}");
        foreach (var l in new[]
        {
            "    return o;",
            "}",
            "static __attribute__((unused)) int32_t es_ol_slot(void *p) { return p == NULL ? -1 : ((struct es_ol_opener *)p)->slot; }",
            "static __attribute__((unused)) void es_ol_close(void *p)",
            "{",
            "    struct es_ol_opener *o = (struct es_ol_opener *)p;",
            "    if (o == NULL) return;",
            $"    switch (o->lib) {{{Cases(l => $"{l.Struct}_free(o->data);")} default: break; }}",
            "    free(o);",
            "}",
            "/* Enter and Leave nest: a library call can call another opener's function (directly or through a callback into the program), so Enter",
            "   pushes the library's current opener and the running heap list, and Leave puts them back (not the \"no opener\" state). */",
            "struct es_ol_saved { int32_t lib; void *d; void **heap; };",
            "static struct es_ol_saved es_ol_stack[ES_OL_DEPTH];",
            "static int es_ol_depth;",
            "static __attribute__((unused)) void es_ol_enter(void *p)",
            "{",
            "    struct es_ol_opener *o = (struct es_ol_opener *)p;",
            "    if (o == NULL) return;",
            "    if (es_ol_depth >= ES_OL_DEPTH) { es_errstr(\"E# library calls nested too deeply\\n\"); es_fatal(); }",
            "    struct es_ol_saved *s = &es_ol_stack[es_ol_depth++];",
            "    s->lib = o->lib;",
            "    s->heap = es_ol_heap;",
            "    s->d = NULL;",
            $"    switch (o->lib) {{{Cases(l => $"s->d = {l.Struct}_d; {l.Struct}_d = (struct {l.Struct} *)o->data; es_ol_heap = {(OpenerHeap ? $"&{l.Struct}_d->es_heap" : "NULL")};")} default: break; }}",
            "}",
            "static __attribute__((unused)) void es_ol_leave(void *p)",
            "{",
            "    struct es_ol_opener *o = (struct es_ol_opener *)p;",
            "    if (o == NULL || es_ol_depth <= 0) return;",
            "    struct es_ol_saved *s = &es_ol_stack[--es_ol_depth];",
            $"    switch (s->lib) {{{Cases(l => $"{l.Struct}_d = (struct {l.Struct} *)s->d;")} default: break; }}",
            "    es_ol_heap = s->heap;",
            "}",
            "",
        }) Line(l);
    }

    /// <summary>Set when <see cref="EmitOpenerGlue"/> wrote the natives: the preamble declares them and the heap helpers.</summary>
    private bool _openerGlue;

    /// <summary>The preamble's part, after es_new: the objects of an opener, and the prototypes of the natives defined before main.</summary>
    private string OpenerPreamble()
    {
        if (!_openerGlue) return "";
        var lines = new List<string>();
        if (!OpenerHeap)
        {
            lines.Add("static void **es_ol_heap;   /* not used: objects are not kept per opener in this build */");
            lines.Add("static __attribute__((unused)) void es_ol_heap_fini(void **head) { (void)head; }");
        }
        lines.AddRange(new[]
        {
            "static __attribute__((unused)) void *es_ol_open(const char *name, int32_t slot);",
            "static __attribute__((unused)) int32_t es_ol_slot(void *p);",
            "static __attribute__((unused)) void es_ol_close(void *p);",
            "static __attribute__((unused)) void es_ol_enter(void *p);",
            "static __attribute__((unused)) void es_ol_leave(void *p);",
            "",
        });
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>es_new and its free when objects belong to openers: a header links each object made during a library call into its opener's list.</summary>
    private static readonly string[] OpenerHeapLines =
    {
        "/* sdl with E# libraries compiled in: an object made during a library call is in its opener's list (freed when the opener closes). */",
        "struct es_ol_block { struct es_ol_block *next, *prev; void **owner; void *pad; };",
        "static void **es_ol_heap;   /* the running opener's list, or NULL outside a library call */",
        "static void *es_new(size_t n)",
        "{",
        "    struct es_ol_block *b = (struct es_ol_block *)calloc(1, sizeof *b + n);",
        "    if (b == NULL) { es_errstr(\"out of memory\\n\"); es_fatal(); }",
        "    if (es_ol_heap != NULL)",
        "    {",
        "        b->owner = es_ol_heap;",
        "        b->next = (struct es_ol_block *)*es_ol_heap;",
        "        if (b->next != NULL) b->next->prev = b;",
        "        *es_ol_heap = b;",
        "    }",
        "    return b + 1;",
        "}",
        "static __attribute__((unused)) void es_ol_free(void *q)",
        "{",
        "    if (q == NULL) return;",
        "    struct es_ol_block *b = (struct es_ol_block *)q - 1;",
        "    if (b->owner != NULL)",
        "    {",
        "        if (b->prev != NULL) b->prev->next = b->next; else *b->owner = b->next;",
        "        if (b->next != NULL) b->next->prev = b->prev;",
        "    }",
        "    free(b);",
        "}",
        "static __attribute__((unused)) void es_ol_heap_fini(void **head)",
        "{",
        "    struct es_ol_block *b = (struct es_ol_block *)*head;",
        "    while (b != NULL) { struct es_ol_block *n = b->next; free(b); b = n; }",
        "    *head = NULL;",
        "}",
    };
}
