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
    /// <summary>
    /// The temporary host project. <paramref name="hostSide"/> is either an E# source tree (a ProjectReference to
    /// ESharp.Target.Host) or a directory with the packed host assemblies (references to the DLLs), see
    /// <see cref="SourceTree.HostSide"/>.
    /// </summary>
    public static string ProjectText(string hostSide, IEnumerable<string> sources)
    {
        string refs;
        if (SourceTree.IsHostLibs(hostSide))
            refs = string.Concat(new[] { "ESharp.Runtime", "ESharp.Platform", "ESharp.Target.Host" }.Select(a =>
                $"    <Reference Include=\"{a}\"><HintPath>{SecurityElement.Escape(Path.GetFullPath(Path.Combine(hostSide, a + ".dll")))}</HintPath></Reference>\n"));
        else
            refs = "    <ProjectReference Include=\"" + SecurityElement.Escape(Path.Combine(hostSide, "src", "ESharp.Target.Host", "ESharp.Target.Host.csproj")) + "\" />\n";
        return "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n" +
            "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n" +
            "    <DefineConstants>$(DefineConstants);HOST</DefineConstants>\n    <AssemblyName>eshosttest</AssemblyName>\n  </PropertyGroup>\n" +
            "  <ItemGroup>\n" + refs + "  </ItemGroup>\n" +
            "  <ItemGroup>\n" + string.Concat(sources.Select(s => "    <Compile Include=\"" + SecurityElement.Escape(Path.GetFullPath(s)) + "\" />\n")) + "  </ItemGroup>\n" +
            "</Project>\n";
    }

    /// <returns>exit code of the program (or of the build, with BuildFailed set), and its stdout.</returns>
    public static (int Exit, string Output, bool BuildFailed) Run(string workDir, string hostSide, IEnumerable<string> sources, TimeSpan timeout)
    {
        Directory.CreateDirectory(workDir);
        var proj = Path.Combine(workDir, "eshosttest.csproj");
        File.WriteAllText(proj, ProjectText(hostSide, sources));

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
