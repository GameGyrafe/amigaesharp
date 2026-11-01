using ESharp.Target.Host;

namespace ESharp.Compiler.Tests;

/// <summary>Runs the hello sample under plain dotnet: the same source the Amiga build compiles.</summary>
public class HostTargetTests
{
    [Fact]
    public void Hello_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        Hello.Program.Main();
        Assert.Equal("Hello from E#!\n", console.Output);
    }

    [Fact]
    public void Phase2_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        Phase2.Program.Main();
        Assert.Equal(Phase2Expected.Output, console.Output);
    }

    [Fact]
    public void Memory_sample_runs_on_the_host_without_leaks()
    {
        using var mem = HostMem.Capture();
        using var console = HostConsole.Capture();
        MemorySample.Program.Main();
        Assert.Equal(MemoryExpected.Output, console.Output);
        Assert.Equal(0, mem.LiveBlocks);
        Assert.Equal(0u, mem.ChipUsed);
    }

    [Fact]
    public void Fix_multiply_matches_a_64_bit_reference()
    {
        var rng = new Random(1234);
        var edges = new[] { 0, 1, -1, 0xFFFF, 0x10000, -0x10000, 0x7FFF0000, int.MinValue, int.MaxValue, 0x8000, -0x8000 };
        var values = edges.Concat(Enumerable.Range(0, 400).Select(_ => rng.Next(int.MinValue, int.MaxValue))).ToArray();
        foreach (var a in values)
        foreach (var b in values.Take(120))
        {
            var expected = unchecked((int)(((long)a * b) >> 16));
            var actual = (ESharp.Fix.FromRaw(a) * ESharp.Fix.FromRaw(b)).Raw;
            Assert.True(expected == actual, $"{a} * {b}: expected {expected}, got {actual}");
        }
    }
}

/// <summary>What samples/phase2 prints: one "ok" line per check. Shared by the host, C and Amiga runs.</summary>
internal static class Phase2Expected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "static array length", "array converts to ReadOnlySpan", "fill and foreach over a static array",
        "span to ReadOnlySpan", "Slice(start, length)", "Slice(start)", "writes through a slice", "sieve",
        "fixed buffer in a struct", "3 * 0.5 = 1.5", "integer and fraction parts", "3 / 0.5 = 6",
        "-2 * 1.5 = -3", "negation", "ToInt rounds towards negative infinity", "comparisons",
        "compound assignment", "multiply by int", "divide by int", "2.5 * 2.5 = 6.25",
        "smallest step keeps its sign", "-1.5 * -2.5 = 3.75", "1.5 * -2.5 = -3.75", "100.25 * -200.5",
        "constructor sets fields", "instance method calling another", "method mutates its receiver",
        "method on an array element", "constructor with early return", "constructor overloads",
        "method on a by-value parameter", "inline array in an instance method",
    };
}

/// <summary>What samples/memory prints.</summary>
internal static class MemoryExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "chip allocation succeeds", "chip memory is zeroed", "chip memory keeps what is written",
        "allocation reduces free chip RAM", "chip block is in the chip address range", "free returns the chip RAM",
        "fast allocation works and is zeroed", "zero-size allocation returns null", "arena reports its size",
        "arena aligns allocations", "arena tracks what is left", "arena returns null when full",
        "typed allocation with sizeof", "typed arena memory is usable and zeroed", "span over arena memory",
        "reset reuses the arena", "destroy releases the arena", "an impossible arena fails cleanly", "chip arena",
    };
}
