using System.Text;

namespace ESharp.Compiler;

/// <summary><c>Trace.Where</c>: where in the program a call is, written to the serial trace in a form <c>esharp addr</c> takes.</summary>
internal sealed partial class CEmitter
{
    /// <summary>
    /// The C of <c>es_trace_where(label)</c> (the [Native] behind <c>Trace.Where</c>). It is a macro, so the address it reports is in
    /// the calling function: <c>es_trace_where_at</c> (never inlined) takes its return address, steps back into the call
    /// instruction, and turns that into what the target's crash reports give:
    /// <list type="bullet">
    /// <item>m68k: <c>hunk N offset 0x…</c>, found by walking the program's segment list (the CLI's cli_Module, else the
    /// process's pr_SegList entry 3) for the segment that holds the address.</item>
    /// <item>AROS: <c>segment N .name offset 0x…</c> from debug.library's DecodeLocationA (the numbers its crash report prints).</item>
    /// <item>AmigaOS 4: <c>_start+0x…</c> (the newlib start-up code; esharp addr finds the symbol in the exe).</item>
    /// <item>Elsewhere (MorphOS): the label and a note that there is no code offset yet.</item>
    /// </list>
    /// Needs es_rawputchar and es_rawioinit from the serial trace block before it.
    /// </summary>
    private static void EmitTraceWhere(StringBuilder r)
    {
        r.AppendLine("/* Trace.Where: the address of the call, as the target's crash reports give it (esharp addr maps it to the C# line). */");
        // The empty asm after the call keeps it out of tail position (a jump would report the caller's caller).
        r.AppendLine("#define es_trace_where(l) __extension__ ({ es_trace_where_at(l); __asm__ __volatile__(\"\"); })");
        // Inside the call instruction (the return address minus the shortest call), so the line is the call's line.
        r.AppendLine("#if defined(__mc68000__)");
        r.AppendLine("#define ES_TRACE_CALL_PC ((const char *)__builtin_return_address(0) - 2)");
        r.AppendLine("#elif defined(__PPC__) || defined(__powerpc__)");
        r.AppendLine("#define ES_TRACE_CALL_PC ((const char *)__builtin_return_address(0) - 4)");
        r.AppendLine("#else");
        r.AppendLine("#define ES_TRACE_CALL_PC ((const char *)__builtin_return_address(0) - 1)");
        r.AppendLine("#endif");
        r.AppendLine("static __attribute__((unused)) void es_trace_where_text(const char *s) { while (*s) es_rawputchar((unsigned char)*s++); }");
        r.AppendLine("static __attribute__((unused)) void es_trace_where_hex(unsigned long v)");
        r.AppendLine("{");
        r.AppendLine("    char b[2 * sizeof(unsigned long) + 3];");
        r.AppendLine("    int i = (int)sizeof(b) - 1;");
        r.AppendLine("    b[i] = 0;");
        r.AppendLine("    do { b[--i] = \"0123456789abcdef\"[v & 15]; v >>= 4; } while (v != 0 && i > 2);");
        r.AppendLine("    b[--i] = 'x';");
        r.AppendLine("    b[--i] = '0';");
        r.AppendLine("    es_trace_where_text(b + i);");
        r.AppendLine("}");
        r.AppendLine("static __attribute__((unused)) void es_trace_where_dec(unsigned long v)");
        r.AppendLine("{");
        r.AppendLine("    char b[24];");
        r.AppendLine("    int i = (int)sizeof(b) - 1;");
        r.AppendLine("    b[i] = 0;");
        r.AppendLine("    do { b[--i] = (char)('0' + v % 10); v /= 10; } while (v != 0 && i > 0);");
        r.AppendLine("    es_trace_where_text(b + i);");
        r.AppendLine("}");
        r.AppendLine("#if defined(__mc68000__)");
        r.AppendLine("#include <proto/exec.h>");
        r.AppendLine("#include <dos/dosextens.h>");
        r.AppendLine("static __attribute__((noinline)) void es_trace_where_at(const char *label)");
        r.AppendLine("{");
        r.AppendLine("    const char *pc = ES_TRACE_CALL_PC;");
        r.AppendLine("    struct Process *me = (struct Process *)FindTask(NULL);");
        r.AppendLine("    BPTR seg = 0;");
        r.AppendLine("    long hunk = -1, n = 0;");
        r.AppendLine("    unsigned long offset = 0;");
        r.AppendLine("    if (me->pr_Task.tc_Node.ln_Type == NT_PROCESS)");
        r.AppendLine("    {");
        r.AppendLine("        if (me->pr_CLI) seg = ((struct CommandLineInterface *)BADDR(me->pr_CLI))->cli_Module;");
        r.AppendLine("        if (!seg && me->pr_SegList) seg = ((BPTR *)BADDR(me->pr_SegList))[3];");
        r.AppendLine("    }");
        r.AppendLine("    for (; seg != 0 && n < 1000; n++)");
        r.AppendLine("    {");
        r.AppendLine("        ULONG *p = (ULONG *)BADDR(seg);   /* [-1] size of the allocation, [0] next segment, then the hunk's bytes */");
        r.AppendLine("        const char *start = (const char *)(p + 1), *end = (const char *)p - 4 + p[-1];");
        r.AppendLine("        if ((const char *)pc >= start && (const char *)pc < end) { hunk = n; offset = (unsigned long)((const char *)pc - start); break; }");
        r.AppendLine("        seg = (BPTR)p[0];");
        r.AppendLine("    }");
        r.AppendLine("    es_rawioinit();");
        r.AppendLine("    es_trace_where_text(label);");
        r.AppendLine("    if (hunk < 0) { es_trace_where_text(\": not in the program's segments, address \"); es_trace_where_hex((unsigned long)pc); }");
        r.AppendLine("    else { es_trace_where_text(\": hunk \"); es_trace_where_dec((unsigned long)hunk); es_trace_where_text(\" offset \"); es_trace_where_hex(offset); }");
        r.AppendLine("    es_rawputchar('\\n');");
        r.AppendLine("}");
        r.AppendLine("#elif defined(__AROS__)");
        r.AppendLine("#include <proto/exec.h>");
        r.AppendLine("#include <proto/debug.h>");
        r.AppendLine("#include <libraries/debug.h>");
        r.AppendLine("#include <utility/tagitem.h>");
        r.AppendLine("static __attribute__((noinline)) void es_trace_where_at(const char *label)");
        r.AppendLine("{");
        r.AppendLine("    const char *pc = ES_TRACE_CALL_PC;");
        r.AppendLine("    struct Library *DebugBase = OpenLibrary((CONST_STRPTR)\"debug.library\", 0);   /* local: no auto-open */");
        r.AppendLine("    IPTR number = 0;");
        r.AppendLine("    STRPTR name = NULL;");
        r.AppendLine("    APTR start = NULL;");
        r.AppendLine("    int found = 0;");
        r.AppendLine("    if (DebugBase)");
        r.AppendLine("    {");
        r.AppendLine("        struct TagItem tags[] = { { DL_SegmentNumber, (IPTR)&number }, { DL_SegmentName, (IPTR)&name }, { DL_SegmentStart, (IPTR)&start }, { TAG_DONE, 0 } };");
        r.AppendLine("        found = DecodeLocationA((APTR)pc, tags) && start != NULL;");
        r.AppendLine("        CloseLibrary(DebugBase);");
        r.AppendLine("    }");
        r.AppendLine("    es_rawioinit();");
        r.AppendLine("    es_trace_where_text(label);");
        r.AppendLine("    if (!found) { es_trace_where_text(\": not found by debug.library, address \"); es_trace_where_hex((unsigned long)(IPTR)pc); }");
        r.AppendLine("    else");
        r.AppendLine("    {");
        r.AppendLine("        es_trace_where_text(\": segment \");");
        r.AppendLine("        es_trace_where_dec((unsigned long)number);");
        r.AppendLine("        if (name) { es_rawputchar(' '); es_trace_where_text((const char *)name); }");
        r.AppendLine("        es_trace_where_text(\" offset \");");
        r.AppendLine("        es_trace_where_hex((unsigned long)((const char *)pc - (const char *)start));");
        r.AppendLine("    }");
        r.AppendLine("    es_rawputchar('\\n');");
        r.AppendLine("}");
        r.AppendLine("#elif defined(__amigaos4__)");
        r.AppendLine("extern void _start(void);   /* newlib's start-up code, in .text */");
        r.AppendLine("static __attribute__((noinline)) void es_trace_where_at(const char *label)");
        r.AppendLine("{");
        r.AppendLine("    const char *pc = ES_TRACE_CALL_PC;");
        r.AppendLine("    es_rawioinit();");
        r.AppendLine("    es_trace_where_text(label);");
        r.AppendLine("    if ((const char *)pc >= (const char *)_start) { es_trace_where_text(\": _start+\"); es_trace_where_hex((unsigned long)((const char *)pc - (const char *)_start)); }");
        r.AppendLine("    else { es_trace_where_text(\": address \"); es_trace_where_hex((unsigned long)pc); }");
        r.AppendLine("    es_rawputchar('\\n');");
        r.AppendLine("}");
        r.AppendLine("#else");
        r.AppendLine("static __attribute__((noinline)) void es_trace_where_at(const char *label)");
        r.AppendLine("{");
        r.AppendLine("    const char *pc = ES_TRACE_CALL_PC;");
        r.AppendLine("    (void)pc;");
        r.AppendLine("    es_rawioinit();");
        r.AppendLine("    es_trace_where_text(label);");
        r.AppendLine("    es_trace_where_text(\": (no code offset on this target yet)\\n\");");
        r.AppendLine("}");
        r.AppendLine("#endif");
        r.AppendLine();
    }
}
