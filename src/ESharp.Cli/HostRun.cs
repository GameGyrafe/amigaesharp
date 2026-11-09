using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;

namespace ESharp.Cli;

/// <summary>Builds and runs a project's .cs files under plain dotnet with HOST defined (the test host).</summary>
public static class HostRun
{
    public static string? FindRepo()
    {
        var env = Environment.GetEnvironmentVariable("ESHARP_REPO");
        foreach (var start in new[] { env, AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var d = start == null ? null : new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "src", "ESharp.Target.Host", "ESharp.Target.Host.csproj")))
                    return d.FullName;
        }
        return null;
    }

    public static string ProjectText(string repo, IEnumerable<string> sources) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <Nullable>enable</Nullable>\n    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n" +
        "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n" +
        "    <DefineConstants>$(DefineConstants);HOST</DefineConstants>\n    <AssemblyName>eshosttest</AssemblyName>\n  </PropertyGroup>\n" +
        "  <ItemGroup>\n    <ProjectReference Include=\"" + SecurityElement.Escape(Path.Combine(repo, "src", "ESharp.Target.Host", "ESharp.Target.Host.csproj")) + "\" />\n  </ItemGroup>\n" +
        "  <ItemGroup>\n" + string.Concat(sources.Select(s => "    <Compile Include=\"" + SecurityElement.Escape(Path.GetFullPath(s)) + "\" />\n")) + "  </ItemGroup>\n" +
        "</Project>\n";

    /// <returns>exit code of the program (or of the build, with BuildFailed set), and its stdout.</returns>
    public static (int Exit, string Output, bool BuildFailed) Run(string workDir, string repo, IEnumerable<string> sources, TimeSpan timeout)
    {
        Directory.CreateDirectory(workDir);
        var proj = Path.Combine(workDir, "eshosttest.csproj");
        File.WriteAllText(proj, ProjectText(repo, sources));

        var build = Exec("dotnet", new[] { "build", proj, "-c", "Release", "-nologo", "-v", "q", "-o", Path.Combine(workDir, "out") }, TimeSpan.FromMinutes(5));
        if (build.Exit != 0)
        {
            Console.Error.WriteLine(build.Out);
            return (build.Exit, "", true);
        }
        var run = Exec("dotnet", new[] { Path.Combine(workDir, "out", "eshosttest.dll") }, timeout);
        return (run.Exit, run.Out, false);
    }

    private static (int Exit, string Out) Exec(string file, IEnumerable<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var outTask = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(timeout))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            return (124, outTask.Result + "\n(timed out)\n");
        }
        var text = outTask.Result;
        if (p.ExitCode != 0) text += err.Result;
        return (p.ExitCode, text);
    }
}
