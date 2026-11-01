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
}
