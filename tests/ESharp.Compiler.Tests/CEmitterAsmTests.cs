using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>[InlineAsm] and [Interrupt] methods (CEmitter.Asm.cs).</summary>
public class CEmitterAsmTests
{
    private static CompileOptions Defines(params string[] d) => new() { Defines = d };

    private static readonly CompileOptions M68k = Defines("AMIGA", "M68K", "AMIGAOS3");
    private static readonly CompileOptions Ppc = Defines("AMIGA", "PPC", "AMIGAOS4");

    private const string AsmMembers = @"
[InlineAsm(""move.l %[a],%[result]\nadd.l %[b],%[result]"", Ppc = ""add %[result],%[a],%[b]"")]
static int Add(int a, int b) { return a + b; }
[InlineAsm(""move.l %[p],%[result]\naddq.l #4,%[result]"")]
static unsafe int* Next(int* p) { return p + 1; }
[InlineAsm(""move.l %[v],d1\nmove.l d1,(%[p])"", Clobbers = ""d1"")]
static unsafe void Store(int* p, int v) { *p = v; }
static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }
";

    private const string AsmMain = @"Check(Add(40, 2) == 42, ""add""); int[] cells = new int[2]; unsafe { fixed (int* q = cells) { Check(Next(q) == q + 1, ""next""); Store(q, 7); Check(q[0] == 7, ""store""); } }";

    [Fact]
    public void InlineAsm_on_m68k_binds_result_and_parameters_as_named_operands()
    {
        var c = CompileOk(Program(AsmMembers, AsmMain), M68k);
        Assert.Contains("#if defined(__mc68000__)", c);
        Assert.Contains("__asm__ __volatile__(\"move.l %[a],%[result]\\n\\tadd.l %[b],%[result]\"", c);
        Assert.Contains(": [result] \"=&d\"(es_asm_result)", c);
        Assert.Contains(": [a] \"d\"(a), [b] \"d\"(b)", c);
        Assert.Contains("[result] \"=&a\"(es_asm_result)", c);   // a pointer result is an address register
        Assert.Contains("[p] \"a\"(p), [v] \"d\"(v)", c);
        Assert.Contains(": \"d1\", \"cc\", \"memory\");", c);
        Assert.DoesNotContain("__PPC__", c);
    }

    [Fact]
    public void InlineAsm_falls_back_to_the_CSharp_body_where_the_assembly_does_not_run()
    {
        // The host gcc is no 68k: the #else branch (the C# body) runs, so the same checks pass.
        var c = CompileOk(Program(AsmMembers, AsmMain), M68k);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("ok add\nok next\nok store\n", output);
    }

    [Fact]
    public void InlineAsm_on_PowerPC_uses_the_Ppc_code_only_where_given()
    {
        var c = CompileOk(Program(AsmMembers, AsmMain), Ppc);
        Assert.Contains("#if defined(__PPC__)", c);
        Assert.Contains("__asm__ __volatile__(\"add %[result],%[a],%[b]\"", c);
        Assert.Contains("[result] \"=&r\"(es_asm_result)", c);
        Assert.DoesNotContain("addq.l", c);   // Next and Store have no PowerPC code: their C# bodies only
        Assert.DoesNotContain("__mc68000__", c);
    }

    [Fact]
    public void InlineAsm_without_assembly_for_the_target_emits_only_the_body()
    {
        var c = CompileOk(Program(AsmMembers, AsmMain), Defines("AMIGA", "X86_64", "AROS"));
        Assert.DoesNotContain("__asm__ __volatile__", c);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("ok add\nok next\nok store\n", output);
    }

    [Theory]
    [InlineData("[InlineAsm(\"nop\")] static extern void F();", "without a C# body")]
    [InlineData("} public class Q { [InlineAsm(\"nop\")] public void F() { }", "static, non-generic")]
    [InlineData("[InlineAsm(\"nop\")] static long F(long x) { return x; }", "parameter 'x'")]
    [InlineData("[InlineAsm(\"nop\")] static void F(ref int x) { }", "parameter 'x'")]
    [InlineData("[InlineAsm(\"nop\")] static int F(int result) { return result; }", "other than 'result'")]
    [InlineData("[InlineAsm(\"nop\")] static bool F() { return true; }", "result of type bool")]
    public void Bad_InlineAsm_methods_are_ES0099(string member, string message)
    {
        var r = Compile(Program("#pragma warning disable CS0626\n" + member), M68k);
        Assert.Contains("ES0099", ErrorIds(r));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.GetMessage().Contains(message));
    }

    private const string InterruptMembers = @"
static volatile int Ticks;
[Interrupt] static void Tick(void* data) { Ticks++; }
[Interrupt] static int Bump(void* data) { *(int*)data += 1; return *(int*)data; }
[Interrupt] static void Plain() { Ticks += 10; }
";

    private const string InterruptMain = "delegate*<void*, void> t = &Tick; delegate*<void*, int> b = &Bump; delegate*<void> p = &Plain; Dos.PutStr(t != null && b != null && p != null ? \"addresses\\n\" : \"none\\n\");";

    private static string InterruptProgram() => Prelude + $@"
public static unsafe class P
{{
    [EntryPoint]
    public static void Main() {{ {InterruptMain} }}
    {InterruptMembers}
}}";

    [Fact]
    public void Interrupt_on_m68k_gets_an_exec_server_entry()
    {
        var c = CompileOk(InterruptProgram(), M68k);
        Assert.Contains("static volatile int32_t Es_P_Ticks;", c);
        Assert.Contains("int32_t Es_P_Tick_isr(void *es_data)", c);
        Assert.Contains("#define Es_P_Tick_ISR ((void *)Es_P_Tick_entry)", c);
        Assert.Contains("void *t = Es_P_Tick_ISR;", c);
        // The entry: is_Data from a1 onto the stack for the C function, the Z flag from its result.
        Assert.Contains("_Es_P_Tick_entry:\\n\\tmove.l a1,-(sp)\\n\\tjsr _Es_P_Tick_isr\\n\\taddq.l #4,sp\\n\\ttst.l d0\\n\\trts", c);
        Assert.Contains("return (int32_t)Es_P_Bump((void *)es_data);", c);
    }

    [Fact]
    public void Interrupt_server_functions_pass_is_Data_and_return_zero_for_void()
    {
        // Elsewhere (AROS, the host gcc) the address is the C server itself: call it as exec would, with is_Data.
        var c = CompileOk(InterruptProgram(), Defines("AMIGA", "X86_64", "AROS"));
        Assert.Contains("#define Es_P_Tick_ISR ((void *)Es_P_Tick_isr)", c);
        if (!HasGcc) return;
        c = c.Replace("int main(void)", "static int es_main(void)") + @"
#include <stdio.h>
int main(void)
{
    int32_t box = 5;
    int32_t (*server)(void *) = (int32_t (*)(void *))Es_P_Tick_ISR;
    int32_t r1 = server(NULL), r2 = server(NULL);
    int32_t r3 = ((int32_t (*)(void *))Es_P_Bump_ISR)(&box);
    int32_t r4 = ((int32_t (*)(void *))Es_P_Plain_ISR)(&box);
    printf(""%d %d %d %d %d %d\n"", (int)r1, (int)r2, (int)Es_P_Ticks, (int)r3, (int)box, (int)r4);
    return es_main();
}
";
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.Equal("0 0 12 6 6 0\naddresses\n", output);
    }

    [Theory]
    [InlineData("[Interrupt] static void F(void* d) { }", "AMIGA,PPC,AMIGAOS4", "PowerPC")]
    [InlineData("[Interrupt] static void F(void* d) { }", "SDL", "sdl")]
    [InlineData("[Interrupt] static void F(int d) { }", "AMIGA,M68K", "one pointer")]
    [InlineData("[Interrupt] static long F() { return 0; }", "AMIGA,M68K", "void")]
    [InlineData("} public class Q { [Interrupt] public void F() { }", "AMIGA,M68K", "static")]
    [InlineData("static void F() { } static void G() { delegate*<void> f = &F; }", "AMIGA,M68K", "only [Interrupt] methods")]
    public void Bad_Interrupt_methods_are_ES0099(string member, string defines, string message)
    {
        var src = Prelude + "public static unsafe class P { [EntryPoint] public static void Main() { } " + member + " }";
        var r = Compile(src, Defines(defines.Split(',')));
        Assert.Contains(r.Diagnostics, d => d.Id == "ES0099" && d.GetMessage().Contains(message));
    }

    [Fact]
    public void Interrupts_platform_class_adds_and_removes_a_vertb_server()
    {
        var c = CompileOk(Prelude + @"
public static unsafe class P
{
    [Interrupt] static void Tick(void* data) { }
    [EntryPoint] public static void Main() { int id = Interrupts.AddVBlank(&Tick, null); Interrupts.RemoveVBlank(id); }
}", M68k);
        Assert.Contains("AddIntServer(", c);
        Assert.Contains("RemIntServer(", c);
        Assert.Contains("Es_ESharp_Platform_Interrupts_RemoveAll();", c[c.IndexOf("int main(void)", System.StringComparison.Ordinal)..]);   // [AtExit]
    }
}
