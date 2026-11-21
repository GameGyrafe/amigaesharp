using System.Collections.Generic;
using ESharp.Cli;
using ESharp.Compiler;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Local toolchains (issue #122): where `esharp build` finds a target's compiler without Docker (--toolchain,
/// ESHARP_TOOLCHAIN_&lt;TARGET&gt;, ESHARP_TOOLCHAIN, PATH), the AROS sysroot, the version check, the error messages, and the
/// command line a local build passes (the same as a Docker build's), with a fake compiler.
/// </summary>
public class ToolchainTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "esharp-tc-" + Guid.NewGuid().ToString("N"));

    public ToolchainTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>A toolchain folder with an empty bin/&lt;compiler&gt; (enough for the lookup).</summary>
    private string FakeToolchain(string name, string compiler, string sub = "bin")
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(dir, sub));
        File.WriteAllText(Path.Combine(dir, sub, Exe(compiler)), "");
        return dir;
    }

    private Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var empty = Path.Combine(_dir, "empty-path");
        Directory.CreateDirectory(empty);
        var d = vars.ToDictionary(v => v.Name, v => v.Value);
        return n => d.TryGetValue(n, out var v) ? v : n == "PATH" ? empty : null;
    }

    [Fact]
    public void Variable_names_follow_the_target_name()
    {
        Assert.Equal("ESHARP_TOOLCHAIN_M68K", Toolchain.EnvFor(Targets.M68k));
        Assert.Equal("ESHARP_TOOLCHAIN_AROS_I386", Toolchain.EnvFor(Targets.ArosI386));
        Assert.Equal("ESHARP_SYSROOT_AROS", Toolchain.SysrootEnvFor(Targets.Aros));
        Assert.Equal("/opt/m68k-amigaos", Toolchain.ImageDir(Targets.M68k));
    }

    [Fact]
    public void The_option_finds_the_compiler_in_bin()
    {
        var tc = FakeToolchain("m68k", "m68k-amigaos-gcc");
        Assert.Null(Toolchain.Resolve(Targets.M68k, tc, null, Env(), out var c));
        Assert.Equal(Path.Combine(tc, "bin", Exe("m68k-amigaos-gcc")), c!.Compiler);
        Assert.Equal("--toolchain", c.Source);
        Assert.Null(c.Sysroot);
    }

    [Fact]
    public void Option_beats_the_target_variable_which_beats_ESHARP_TOOLCHAIN()
    {
        var a = FakeToolchain("a", "m68k-amigaos-gcc");
        var b = FakeToolchain("b", "m68k-amigaos-gcc");
        var c = FakeToolchain("c", "m68k-amigaos-gcc");
        var env = Env(("ESHARP_TOOLCHAIN_M68K", b), ("ESHARP_TOOLCHAIN", c));
        Toolchain.Resolve(Targets.M68k, a, null, env, out var r1);
        Assert.StartsWith(a, r1!.Compiler);
        Toolchain.Resolve(Targets.M68k, null, null, env, out var r2);
        Assert.StartsWith(b, r2!.Compiler);
        Assert.Equal("ESHARP_TOOLCHAIN_M68K", r2.Source);
        Toolchain.Resolve(Targets.M68k, "", null, Env(("ESHARP_TOOLCHAIN", c)), out var r3);   // empty option: not given (VS Code tasks)
        Assert.StartsWith(c, r3!.Compiler);
        Assert.Equal("ESHARP_TOOLCHAIN", r3.Source);
    }

    [Fact]
    public void ESHARP_TOOLCHAIN_is_for_Amiga_targets_only()
    {
        var c = FakeToolchain("c", "gcc");
        var error = Toolchain.Resolve(Targets.Sdl, null, null, Env(("ESHARP_TOOLCHAIN", c)), out _);
        Assert.NotNull(error);
        Assert.Contains("gcc not found on PATH", error);
        Assert.Null(Toolchain.Resolve(Targets.Sdl, null, null, Env(("ESHARP_TOOLCHAIN_SDL", c)), out var sdl));
        Assert.StartsWith(c, sdl!.Compiler);
    }

    [Fact]
    public void A_compiler_on_PATH_is_used_when_nothing_is_set()
    {
        var bin = Path.Combine(FakeToolchain("p", "ppc-amigaos-gcc"), "bin");
        Assert.Null(Toolchain.Resolve(Targets.Os4, null, null, Env(("PATH", bin)), out var c));
        Assert.Equal("PATH", c!.Source);
        Assert.Equal(Path.Combine(bin, Exe("ppc-amigaos-gcc")), c.Compiler);
    }

    [Fact]
    public void Errors_say_what_is_missing_and_how_to_fix_it()
    {
        var missing = Path.Combine(_dir, "nope");
        var e1 = Toolchain.Resolve(Targets.M68k, missing, null, Env(), out _)!;
        Assert.Contains(missing, e1);
        Assert.Contains("does not exist", e1);
        Assert.Contains("/opt/m68k-amigaos", e1);

        var wrong = FakeToolchain("wrong", "ppc-amigaos-gcc");
        var e2 = Toolchain.Resolve(Targets.M68k, null, null, Env(("ESHARP_TOOLCHAIN_M68K", wrong)), out _)!;
        Assert.Contains("no " + Exe("m68k-amigaos-gcc") + " in " + Path.Combine(wrong, "bin"), e2);
        Assert.Contains("ESHARP_TOOLCHAIN_M68K", e2);
        Assert.Contains("--docker", e2);

        var e3 = Toolchain.Resolve(Targets.MorphOs, null, null, Env(), out _)!;
        Assert.Contains("ppc-morphos-gcc not found on PATH", e3);
        Assert.Contains("--docker", e3);
        Assert.Contains("--toolchain <dir>", e3);
        Assert.Contains("ESHARP_TOOLCHAIN_MORPHOS", e3);
        Assert.Contains("--emit-only", e3);
    }

    [Fact]
    public void Aros_sysroot_comes_from_the_toolchain_folder_or_the_option()
    {
        var x64 = FakeToolchain("x64", "x86_64-aros-gcc");
        Assert.Null(Toolchain.Resolve(Targets.Aros, x64, null, Env(), out var a));
        Assert.Equal(x64, a!.Sysroot);

        // aros-i386: the image keeps the compilers at the top of /opt/i386-aros and the SDK in its sdk folder.
        var i386 = FakeToolchain("i386", "i386-aros-gcc", sub: "");
        var noSdk = Toolchain.Resolve(Targets.ArosI386, i386, null, Env(), out _)!;
        Assert.Contains(Path.Combine(i386, "sdk"), noSdk);
        Assert.Contains("--sysroot", noSdk);
        Assert.Contains("ESHARP_SYSROOT_AROS_I386", noSdk);
        Directory.CreateDirectory(Path.Combine(i386, "sdk"));
        Assert.Null(Toolchain.Resolve(Targets.ArosI386, i386, null, Env(), out var b));
        Assert.Equal(Path.Combine(i386, Exe("i386-aros-gcc")), b!.Compiler);
        Assert.Equal(Path.Combine(i386, "sdk"), b.Sysroot);

        var other = Path.Combine(_dir, "sdk2");
        Directory.CreateDirectory(other);
        Toolchain.Resolve(Targets.Aros, x64, other, Env(), out var c);
        Assert.Equal(other, c!.Sysroot);
        Toolchain.Resolve(Targets.Aros, x64, null, Env(("ESHARP_SYSROOT_AROS", other)), out var d);
        Assert.Equal(other, d!.Sysroot);
        Assert.Contains("does not exist", Toolchain.Resolve(Targets.Aros, x64, Path.Combine(_dir, "none"), Env(), out _));

        // On PATH: the image's path, as before.
        Toolchain.Resolve(Targets.Aros, null, null, Env(("PATH", Path.Combine(x64, "bin"))), out var e);
        Assert.Equal("/opt/x86_64-aros", e!.Sysroot);
    }

    [Fact]
    public void The_version_must_be_the_pinned_images()
    {
        Assert.Null(Toolchain.VersionProblem(Targets.M68k, "gcc", "6.5.0b\n"));
        var wrong = Toolchain.VersionProblem(Targets.M68k, "/x/bin/m68k-amigaos-gcc", "13.2.0")!;
        Assert.Contains("13.2.0", wrong);
        Assert.Contains("6.5.0b", wrong);
        Assert.Contains("--any-cc-version", wrong);
        Assert.Contains("did not report a version", Toolchain.VersionProblem(Targets.Os4, "cc", null));
        Assert.Null(Toolchain.VersionProblem(Targets.Sdl, "gcc", "15.2.0"));
        Assert.Equal(new[] { "6.5.0b", "11.5.0", "15.2.0", "9.1.0", "6.5.0" },
            new[] { Targets.M68k, Targets.Os4, Targets.MorphOs, Targets.Aros, Targets.ArosI386 }.Select(t => t.CompilerVersion));
    }

    /// <summary>
    /// A fake gcc (a shell script) that prints a version for -dumpversion, else records its working directory and arguments and
    /// writes the -o file: shows the command line a local build passes. Unix only (the script needs sh).
    /// </summary>
    private string FakeGcc(string name, string compiler, string version)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        var log = Path.Combine(dir, "calls.txt");
        var script = Path.Combine(dir, "bin", compiler);
        File.WriteAllText(script, $@"#!/bin/sh
if [ ""$1"" = -dumpversion ]; then echo {version}; exit 0; fi
echo ""$(pwd) $*"" >> '{log}'
out=
while [ $# -gt 0 ]; do [ ""$1"" = -o ] && out=$2; shift; done
printf x > ""$out""
");
        File.SetUnixFileMode(script, (UnixFileMode)0b111_101_101);
        return dir;
    }

    private string Project()
    {
        var src = Path.Combine(_dir, "demo");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Program.cs"), ProjectTemplate.ProgramText("demo"));
        return src;
    }

    [Fact]
    public void A_local_build_runs_the_toolchain_compiler_in_the_output_folder_with_the_docker_command_line()
    {
        if (OperatingSystem.IsWindows()) return;
        var tc = FakeGcc("tc", "m68k-amigaos-gcc", "6.5.0b");
        var src = Project();
        var outDir = Path.Combine(_dir, "out");
        Assert.Equal(0, Program.Build(new[] { src, "--toolchain", tc, "-o", outDir }, out var exe));
        Assert.Equal(Path.Combine(outDir, "demo"), exe);
        var calls = File.ReadAllLines(Path.Combine(tc, "calls.txt"));
        Assert.Equal(new[]
        {
            $"{Path.GetFullPath(outDir)} -noixemul -m68020 -O2 -fwrapv -o demo demo.c",
            $"{Path.GetFullPath(outDir)} -noixemul -m68020 -O2 -fwrapv -g -c -o demo.o demo.c",
        }, calls);
    }

    [Fact]
    public void A_local_aros_build_passes_the_toolchain_sysroot_first()
    {
        if (OperatingSystem.IsWindows()) return;
        var tc = FakeGcc("aros", "x86_64-aros-gcc", "9.1.0");
        var outDir = Path.Combine(_dir, "out");
        Assert.Equal(0, Program.Build(new[] { Project(), "--target", "aros", "--toolchain", tc, "-o", outDir }, out _));
        Assert.Equal($"{Path.GetFullPath(outDir)} --sysroot={tc} -O2 -fwrapv -o demo demo.c", File.ReadAllLines(Path.Combine(tc, "calls.txt")).Single());
    }

    [Fact]
    public void Another_compiler_version_stops_the_build_unless_any_cc_version()
    {
        if (OperatingSystem.IsWindows()) return;
        var tc = FakeGcc("new", "m68k-amigaos-gcc", "13.2.0");
        var src = Project();
        Assert.Equal(1, Program.Build(new[] { src, "--toolchain", tc, "-o", Path.Combine(_dir, "o1") }, out var none));
        Assert.Null(none);
        Assert.False(File.Exists(Path.Combine(tc, "calls.txt")));
        Assert.Equal(0, Program.Build(new[] { src, "--toolchain", tc, "--any-cc-version", "-o", Path.Combine(_dir, "o2") }, out var exe));
        Assert.NotNull(exe);
    }

    [Fact]
    public void Docker_and_toolchain_together_are_an_error_and_a_missing_toolchain_fails_cleanly()
    {
        var src = Project();
        Assert.Equal(1, Program.Build(new[] { src, "--docker", "img", "--toolchain", _dir, "-o", Path.Combine(_dir, "o") }, out _));
        Assert.Equal(1, Program.Build(new[] { src, "--toolchain", Path.Combine(_dir, "missing"), "-o", Path.Combine(_dir, "o") }, out var exe));
        Assert.Null(exe);
        Assert.Equal(1, Program.Build(new[] { src, "--toolchain" }, out _));
    }

    [Fact]
    public void Doctor_reports_docker_images_local_toolchains_and_emulators_offline()
    {
        var tc = FakeToolchain("m68k", "m68k-amigaos-gcc");
        var env = Env(("ESHARP_TOOLCHAIN_M68K", tc), ("ESHARP_OS4_VM", Path.Combine(_dir, "novm")));
        var calls = new List<string>();
        var probe = new Doctor.Probe
        {
            Env = env,
            FileExists = _ => false,
            VersionOf = cc => cc.Contains("m68k") ? "6.5.0b" : null,
            Run = (file, args) =>
            {
                calls.Add(file + " " + string.Join(" ", args));
                if (file != "docker") return null;
                if (args[0] == "version") return (0, "29.0.0\n");
                return args[^1] == "esharp-amiga-toolchain" ? (0, "sha256:1") : (1, "");
            },
        };
        var text = string.Join("\n", Doctor.Report(probe, Targets.All));
        Assert.Contains("Docker: server 29.0.0", text);
        Assert.Contains("m68k       docker: esharp-amiga-toolchain present", text);
        Assert.Contains(Path.Combine(tc, "bin", Exe("m68k-amigaos-gcc")) + " 6.5.0b (from ESHARP_TOOLCHAIN_M68K), as in the pinned image", text);
        Assert.Contains("builds here: by Docker and by local toolchain", text);
        Assert.Contains("ppc-morphos-gcc not on PATH; set ESHARP_TOOLCHAIN_MORPHOS", text);
        Assert.Contains("esharp-aros-i386 not present; docker build -t esharp-aros-i386 docker/aros-i386", text);
        Assert.Contains("esharp-aros-hosted not present", text);
        Assert.Contains("FS-UAE (m68k run/test): not found", text);
        Assert.Contains("ESHARP_OS4_VM (", text);
        Assert.Contains("ESHARP_KICKSTART not set", text);
        // Offline and read-only: only docker version and docker image inspect, no pull or build.
        Assert.All(calls.Where(c => c.StartsWith("docker ")), c => Assert.Matches("^docker (version|image inspect) ", c));

        var noDocker = string.Join("\n", Doctor.Report(new Doctor.Probe { Env = env, FileExists = _ => false, VersionOf = _ => "6.5.0b", Run = (_, _) => null }, new[] { Targets.M68k }));
        Assert.Contains("Docker: not found", noDocker);
        Assert.Contains("builds here: by local toolchain", noDocker);
    }

    [Fact]
    public void Doctor_always_exits_0()
    {
        var w = new StringWriter();
        Assert.Equal(0, Doctor.Run(new[] { "--target", "nope" }, w));
        Assert.Contains("unknown target", w.ToString());
        Assert.Equal(0, Doctor.Run(new[] { "--toolchain", _dir }, w));
    }
}
