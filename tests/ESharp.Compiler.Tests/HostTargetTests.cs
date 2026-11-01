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
        "smallest step keeps its sign",
    };
}
