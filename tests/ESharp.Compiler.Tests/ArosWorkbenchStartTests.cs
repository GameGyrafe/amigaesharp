using System.Collections.Generic;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Issue #107: every AROS program brings its own Workbench (Wanderer) start entry, because the x86-64 toolchain's startup waits for the
/// WBStartup message at a pr_MsgPort offset that newer AROS no longer has. The entry is built here with the host gcc against stand-in
/// AROS headers: the process port sits 48 bytes after the compiled offset (as on hosted x86-64 AROS), with a port of another task at the
/// compiled offset that must be passed over.
/// </summary>
public class ArosWorkbenchStartTests
{
    private const string ShowArgs = @"
public static class P
{
    [EntryPoint]
    public static void Main()
    {
        for (int i = 0; i < Args.Count; i++) { Dos.PutStr(Args.Get(i)); Dos.PutStr(""\n""); }
    }
}";

    private const string NoArgs = @"
public static class P
{
    [EntryPoint]
    public static void Main() { Dos.PutStr(""hello\n""); }
}";

    // Stand-ins for the AROS headers the entry and the Workbench Args code include: exec and dos calls that log what they are asked to do,
    // a Process whose real port is 48 bytes after offsetof(struct Process, pr_MsgPort), and the PROGRAM_ENTRIES set as one function pointer.
    private const string FakeAros = @"#pragma once
#include <stdio.h>
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdlib.h>
typedef intptr_t BPTR; typedef const unsigned char *CONST_STRPTR;
#define BNULL ((BPTR)0)
#define NT_MSGPORT 4
#define SIGB_DOS 8
struct ExecBase { int x; };
struct Library { int x; };
struct Node { struct Node *ln_Succ, *ln_Pred; char *ln_Name; unsigned char ln_Type; signed char ln_Pri; };
struct List { struct Node *lh_Head, *lh_Tail, *lh_TailPred; };
struct MsgPort { struct Node mp_Node; unsigned char mp_Flags; unsigned char mp_SigBit; void *mp_SigTask; struct List mp_MsgList; };
struct Message { struct Node mn_Node; struct MsgPort *mn_ReplyPort; unsigned short mn_Length; };
struct Task { struct Node tc_Node; char tc_Rest[64]; };
struct Process { struct Task pr_Task; struct MsgPort pr_MsgPort; char pr_Rest[256]; };
struct WBArg { BPTR wa_Lock; char *wa_Name; };
struct WBStartup { struct Message sm_Message; struct MsgPort *sm_Process; BPTR sm_Segment; long sm_NumArgs; char *sm_ToolWindow; struct WBArg *sm_ArgList; };
struct DiskObject { char **do_ToolTypes; };
int __argc; char **__argv;
union { struct Process p; char raw[sizeof(struct Process) + 128]; } es_t_proc;
struct WBArg es_t_arg = { 77, ""prog"" };
struct WBStartup es_t_wb = { .sm_NumArgs = 1, .sm_ArgList = &es_t_arg };
int es_t_wb_start, es_t_open, es_t_dobs, es_t_dirs;
BPTR es_t_dir = 5;
int es_t_program_main(int argc, char **argv);
static inline void *Cli(void) { return es_t_wb_start ? NULL : (void *)&es_t_proc; }
static inline struct Task *FindTask(const char *n) { (void)n; return &es_t_proc.p.pr_Task; }
static inline struct MsgPort *es_t_real_port(void) { return (struct MsgPort *)(es_t_proc.raw + offsetof(struct Process, pr_MsgPort) + 48); }
static inline void WaitPort(struct MsgPort *p) { printf(""WaitPort %s\n"", p == es_t_real_port() ? ""real"" : ""WRONG""); }
static inline struct Message *GetMsg(struct MsgPort *p) { return p == es_t_real_port() ? &es_t_wb.sm_Message : NULL; }
static inline BPTR DupLock(BPTR l) { es_t_dirs++; return l + 1000; }
static inline void UnLock(BPTR l) { es_t_dirs--; printf(""UnLock %ld\n"", (long)l); }
static inline BPTR CurrentDir(BPTR l) { BPTR o = es_t_dir; es_t_dir = l; return o; }
static inline void Forbid(void) { printf(""Forbid\n""); }
static inline void ReplyMsg(struct Message *m) { printf(""ReplyMsg %s dir %ld\n"", m == &es_t_wb.sm_Message ? ""wb"" : ""WRONG"", (long)es_t_dir); }
static inline struct Library *OpenLibrary(CONST_STRPTR n, unsigned v) { (void)n; (void)v; es_t_open++; static struct Library l; return &l; }
static inline void CloseLibrary(struct Library *l) { (void)l; es_t_open--; }
char *es_t_tt[] = { ""SEED=42"", ""AUTO"", NULL };
static inline struct DiskObject *GetDiskObject(CONST_STRPTR n) { (void)n; es_t_dobs++; static struct DiskObject d; d.do_ToolTypes = es_t_tt; return &d; }
static inline void FreeDiskObject(struct DiskObject *d) { (void)d; es_t_dobs--; }
static inline void ___startup_entries_next(struct ExecBase *b)
{
    (void)b;
    printf(""main argc %d dir %ld\n"", __argc, (long)es_t_dir);
    es_t_program_main(__argc, __argv);
}
#define __startup_entries_next() ___startup_entries_next(SysBase)
#define ADD2SET(symbol, set, pri) void (*es_t_entry)(struct ExecBase *) = symbol; int es_t_pri = pri;
";

    private const string Driver = @"
int main(int argc, char **argv)
{
    (void)argc;
    es_t_wb_start = ES_T_WB;
    /* a port of another task at the compiled offset: not ours */
    struct MsgPort *decoy = (struct MsgPort *)(es_t_proc.raw + offsetof(struct Process, pr_MsgPort));
    decoy->mp_Node.ln_Type = NT_MSGPORT; decoy->mp_SigBit = SIGB_DOS; decoy->mp_SigTask = (void *)&es_t_proc.raw[1];
    struct MsgPort *real = es_t_real_port();
    real->mp_Node.ln_Type = NT_MSGPORT; real->mp_SigBit = SIGB_DOS; real->mp_SigTask = &es_t_proc.p.pr_Task;
    __argc = 2; __argv = argv;
    static struct ExecBase sys;
    es_t_entry(&sys);
    printf(""pri %d open %d diskobjects %d dirs %d dir %ld\n"", es_t_pri, es_t_open, es_t_dobs, es_t_dirs, (long)es_t_dir);
    return 0;
}
";

    private static Dictionary<string, string> Headers()
    {
        var h = new Dictionary<string, string> { ["es_t_aros.h"] = FakeAros };
        foreach (var name in new[] { "aros/startup.h", "dos/dosextens.h", "workbench/startup.h", "workbench/workbench.h", "proto/icon.h" })
            h[name] = "#include <es_t_aros.h>\n";
        return h;
    }

    private static string Test(string c, bool workbench) =>
        "#define __AROS__ 1\n#define ES_T_WB " + (workbench ? "1" : "0") + "\n#include <es_t_aros.h>\n" +
        c.Replace("int main(int argc, char **argv)", "int es_t_program_main(int argc, char **argv)")
         .Replace("int main(void)", "int es_t_program_main(int argc, char **argv)") + Driver;

    [Fact]
    public void Every_AROS_program_has_its_own_Workbench_start_entry_inside_the_AROS_branch_and_sdl_has_none()
    {
        foreach (var src in new[] { ShowArgs, NoArgs })
        foreach (var t in new[] { Targets.Aros, Targets.ArosI386, Targets.M68k, Targets.Os4, Targets.MorphOs })
        {
            var c = CompileOk(Prelude + src, t.ToOptions());
            Assert.Contains("ADD2SET(es_aros_wbstart, PROGRAM_ENTRIES, -50)", c);
            Assert.Contains("int __nowbsupport = 1;", c);
            // Every Amiga target has the same C, but only the AROS compilers see the entry: it is inside the AROS branch.
            var at = c.IndexOf("int __nowbsupport", System.StringComparison.Ordinal);
            Assert.True(c.LastIndexOf("#if defined(__AROS__)", at, System.StringComparison.Ordinal) > c.LastIndexOf("#endif", at, System.StringComparison.Ordinal));
        }
        Assert.DoesNotContain("es_aros_wbstart", CompileOk(Prelude + NoArgs, Targets.Sdl.ToOptions()));
    }

    [Fact]
    public void A_Wanderer_start_finds_the_real_process_port_reads_the_icon_and_replies_after_the_program()
    {
        var c = CompileOk(Prelude + ShowArgs, Targets.Aros.ToOptions());
        if (!HasGcc) return;
        // The port of this task is found past the compiled offset; argc is 0 with argv the message, so Args come from the tool types;
        // the program runs in the icon's directory (a copy of its lock), which is given back before the reply under Forbid.
        Assert.Equal((0,
            "WaitPort real\nmain argc 0 dir 1077\nSEED=42\nAUTO\nUnLock 1077\nForbid\nReplyMsg wb dir 5\n" +
            "pri -50 open 0 diskobjects 0 dirs 0 dir 5\n"),
            RunWithHostGcc(Test(c, workbench: true), Headers(), "SEED=7"));
    }

    [Fact]
    public void A_Shell_start_waits_for_nothing_and_keeps_the_command_line()
    {
        var c = CompileOk(Prelude + ShowArgs, Targets.Aros.ToOptions());
        if (!HasGcc) return;
        Assert.Equal((0, "main argc 2 dir 5\nSEED=7\npri -50 open 0 diskobjects 0 dirs 0 dir 5\n"),
            RunWithHostGcc(Test(c, workbench: false), Headers(), "SEED=7"));
    }

    [Fact]
    public void A_program_without_Args_is_started_from_Wanderer_too()
    {
        var c = CompileOk(Prelude + NoArgs, Targets.Aros.ToOptions());
        if (!HasGcc) return;
        Assert.Equal((0, "WaitPort real\nmain argc 0 dir 1077\nhello\nUnLock 1077\nForbid\nReplyMsg wb dir 5\npri -50 open 0 diskobjects 0 dirs 0 dir 5\n"),
            RunWithHostGcc(Test(c, workbench: true), Headers()));
    }
}
