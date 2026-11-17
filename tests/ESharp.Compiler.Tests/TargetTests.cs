using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

public class TargetTests
{
    [Theory]
    [InlineData("m68k", "M68K", "AMIGAOS3")]
    [InlineData("os4", "PPC", "AMIGAOS4")]
    [InlineData("morphos", "PPC", "MORPHOS")]
    public void Every_amiga_target_defines_AMIGA_plus_its_cpu_and_os(string name, string cpu, string os)
    {
        var t = Targets.Find(name)!;
        Assert.Contains("AMIGA", t.Defines);
        Assert.Contains(cpu, t.Defines);
        Assert.Contains(os, t.Defines);
        Assert.Equal(cpu == "M68K", t.IsM68k);
    }

    [Theory]
    [InlineData("aros", "X86_64", "I386")]
    [InlineData("aros-i386", "I386", "X86_64")]
    public void Both_aros_targets_define_AMIGA_AROS_and_only_their_own_cpu(string name, string cpu, string other)
    {
        var t = Targets.Find(name)!;
        Assert.Contains("AMIGA", t.Defines);
        Assert.Contains("AROS", t.Defines);
        Assert.Contains(cpu, t.Defines);
        Assert.DoesNotContain(other, t.Defines);
        Assert.False(t.IsM68k);
        Assert.Equal(name == "aros" ? "x86_64-aros-gcc" : "i386-aros-gcc", t.Compiler);
    }

    /// <summary>The struct offsets E# reads directly come from Layout.g.cs's table for the target's CPU: i386 has its own measured table.</summary>
    [Fact]
    public void Each_aros_target_reads_its_own_layout_table()
    {
        const string src = @"
public static class P
{
    [EntryPoint]
    public static int Main() { return ESharp.Platform.Layout.WindowUserPort; }
}";
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ESharp.Platform", "Layout.g.cs")).Replace("\r\n", "\n");
        int Value(string section)
        {
            int at = layout.IndexOf(section + "\n", StringComparison.Ordinal);
            var m = System.Text.RegularExpressions.Regex.Match(layout.Substring(at), @"WindowUserPort = (\d+);");
            return int.Parse(m.Groups[1].Value);
        }
        Assert.Contains($"return {Value("#if X86_64")};", CompileOk(Prelude + src, Targets.Aros.ToOptions()));
        Assert.Contains($"return {Value("#elif I386")};", CompileOk(Prelude + src, Targets.ArosI386.ToOptions()));
        Assert.Contains($"return {Value("#else")};", CompileOk(Prelude + src, Targets.M68k.ToOptions()));
    }

    [Fact]
    public void Unknown_targets_are_not_found() => Assert.Null(Targets.Find("amiga1000"));

    private const string CpuSplit = @"
public static class P
{
    [EntryPoint]
    public static int Main()
    {
#if M68K
        return 68;
#elif PPC
        return 603;
#else
        return 0;
#endif
    }
}";

    [Theory]
    [InlineData("m68k", "return 68;")]
    [InlineData("os4", "return 603;")]
    [InlineData("morphos", "return 603;")]
    public void Source_can_branch_on_cpu(string target, string expected)
    {
        var c = CompileOk(Prelude + CpuSplit, Targets.Find(target)!.ToOptions());
        Assert.Contains(expected, c);
    }

    private const string ChipData = @"
public static class P
{
    [ChipRam] public static int Buffer;
    [EntryPoint] public static void Main() { }
}";

    [Fact]
    public void ChipRam_is_applied_on_m68k_only()
    {
        Assert.Contains("__chip", CompileOk(Prelude + ChipData, Targets.M68k.ToOptions()));
        Assert.DoesNotContain("__chip", CompileOk(Prelude + ChipData, Targets.Os4.ToOptions()));
        Assert.DoesNotContain("__chip", CompileOk(Prelude + ChipData, Targets.MorphOs.ToOptions()));
    }

    [Fact]
    public void Hello_emits_identical_C_for_every_amiga_target_except_the_cpu_specific_parts()
    {
        const string src = "public static class P { [EntryPoint] public static void Main() { Dos.PutStr(\"x\"); } }";
        var outputs = Targets.All.Where(t => t.IsAmiga).Select(t => CompileOk(Prelude + src, t.ToOptions())).Distinct().ToList();
        Assert.Single(outputs);
    }
}
