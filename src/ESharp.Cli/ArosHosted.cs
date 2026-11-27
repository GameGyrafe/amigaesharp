using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ESharp.Compiler;

namespace ESharp.Cli;

/// <summary>
/// Runs a program built for the aros or aros-i386 target on hosted AROS (a Linux process that boots AROS) inside the
/// esharp-aros-hosted image (docker/aros-hosted in the repository; esharp-aros-hosted-i386 for aros-i386), headless under Xvfb.
/// The same steps as tools/aros-hosted/aros-hosted.sh, but self-contained so that an installed tool needs no checkout: esharp
/// writes the program, a few S:Startup-Sequence lines and a small bash script into a run directory, mounts it as /esrun and runs
/// the script in the image. The script copies the image's pristine AROS tree, adds the lines after its "FailAt 21", boots AROS,
/// waits for the done marker and copies the program's output, its return code (the shell's RC) and AROSBootstrap's debug log
/// (where a crash shows up as a "Trap" line) back into the run directory.
/// </summary>
public static class ArosHosted
{
    public const string ImageX86_64 = "esharp-aros-hosted";
    public const string ImageI386 = "esharp-aros-hosted-i386";

    /// <summary>The hosted AROS build an aros target runs on: x86_64 or i386 (the image's AROS_HOSTED_ARCH).</summary>
    /// <summary>
    /// The docker run arguments. <paramref name="user"/> is the uid:gid for --user, so the files the container writes
    /// into the run folder are ours; null leaves it out (Windows: no getuid, and Docker Desktop maps the bind mount itself).
    /// </summary>
    internal static List<string> DockerArgs(string runDir, string image, string arch, string name, int secs, string? user)
    {
        var args = new List<string> { "run", "--rm" };
        if (user != null) { args.Add("--user"); args.Add(user); }
        args.AddRange(new[] { "-e", "HOME=/tmp", "-v", $"{runDir}:/esrun", image,
            "bash", "/esrun/run.sh", arch, name, secs.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return args;
    }

    public static string Arch(TargetInfo target) => target == Targets.ArosI386 ? "i386" : "x86_64";

    /// <summary>The runtime image for a target unless --aros-image names one.</summary>
    public static string DefaultImage(TargetInfo target) => target == Targets.ArosI386 ? ImageI386 : ImageX86_64;

    /// <summary>
    /// The lines that go after "FailAt 21" in S:Startup-Sequence: no return code stops the sequence before the markers, the
    /// program's output goes to a file, then the shell's RC and a done marker. AROS's shell sets RC after every command.
    /// </summary>
    public static string StartupLines(string program, string? programArgs = null) =>
        "FailAt 1000000\n" +
        $"SYS:{program} >SYS:{program}.out{(string.IsNullOrWhiteSpace(programArgs) ? "" : " " + programArgs.Trim())}\n" +
        "Echo >SYS:esharp-rc.txt \"RC $RC\"\n" +
        "FailAt 21\n" +
        "Echo >SYS:done.txt done\n";

    /// <summary>The return code from the "RC n" line the run scripts write (m68k, AROS), or null when there is none.</summary>
    public static int? ParseReturnCode(string? text)
    {
        if (text == null) return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("RC ", StringComparison.Ordinal) && int.TryParse(line[3..].Trim(), System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out var rc))
                return rc;
        }
        return null;
    }

    /// <summary>Lines of AROSBootstrap's debug output that report a CPU trap (a crash of the program or of AROS).</summary>
    public static int CountTraps(string? bootLog) =>
        bootLog == null ? 0 : bootLog.Split('\n').Count(l => l.Contains("Trap", StringComparison.Ordinal));

    /// <summary>The script run in the image: $1 the arch it must be, $2 the program name, $3 seconds to wait for the done marker.</summary>
    public const string Script = """
        #!/bin/bash
        # E# hosted AROS run harness (esharp test --target aros|aros-i386). Written by esharp into its run directory, which is
        # mounted as /esrun; runs inside an esharp-aros-hosted image. The image's AROS tree is copied, never changed.
        set -u
        want=$1; prog=$2; secs=$3
        have=${AROS_HOSTED_ARCH:-x86_64}
        if [ -z "${AROS_HOSTED_COPY:-}" ] || [ ! -x "$AROS_HOSTED_COPY/boot/linux/AROSBootstrap" ]; then
          echo "this image has no hosted AROS tree (AROS_HOSTED_COPY): build it from docker/aros-hosted"; exit 3
        fi
        [ "$have" = "$want" ] || { echo "this image runs hosted AROS $have, not $want"; exit 3; }
        A=$(mktemp -d); trap 'rm -rf "$A"' EXIT
        cp -a "$AROS_HOSTED_COPY/." "$A/" && cd "$A" || exit 4
        cp /esrun/prog "./$prog" && chmod +x "./$prog" || exit 4
        if [ -d /esrun/libs ]; then cp /esrun/libs/*.library ./Libs/ || exit 4; fi
        python3 -I - /esrun/startup.txt <<'P' || exit 4
        import sys
        p = 'S/Startup-Sequence'; t = open(p).read(); add = open(sys.argv[1]).read()
        if "FailAt 21\n" not in t: sys.exit("no FailAt 21 line in S:Startup-Sequence")
        open(p, 'w').write(t.replace("FailAt 21\n", "FailAt 21\n" + add, 1))
        P
        rm -f done.txt esharp-rc.txt "$prog.out"
        Xvfb :9 -screen 0 1024x768x24 -nolisten tcp >/dev/null 2>&1 & XP=$!
        sleep 2
        DISPLAY=:9 timeout $((secs + 10)) ./boot/linux/AROSBootstrap > boot.log 2>&1 & BP=$!
        for i in $(seq "$secs"); do
          [ -f done.txt ] && break
          kill -0 $BP 2>/dev/null || break
          sleep 1
        done
        [ -f done.txt ] && sleep 1
        kill $BP $XP 2>/dev/null; wait 2>/dev/null
        cp boot.log /esrun/boot.log
        [ -f "$prog.out" ] && cp "$prog.out" /esrun/out.txt
        [ -f esharp-rc.txt ] && cp esharp-rc.txt /esrun/rc.txt
        [ -f done.txt ] && touch /esrun/done
        exit 0
        """;

    /// <summary>One run: Exit is non-zero when the harness itself failed (no image, no done marker in time).</summary>
    public sealed record Result(int Exit, string Output, int? ReturnCode, int Traps, bool Finished, string RunDir);

    public static Result Run(string exe, TargetInfo target, TimeSpan timeout, string image, string? programArgs = null)
    {
        var name = Path.GetFileName(exe);
        var runDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe))!, "run");
        if (Os4Vm.ProgramArgsError(programArgs) is { } argsError) return Fail(argsError, runDir);
        if (Directory.Exists(runDir)) Directory.Delete(runDir, recursive: true);
        Directory.CreateDirectory(runDir);
        File.Copy(exe, Path.Combine(runDir, "prog"));
        // E# Amiga libraries the program uses (issue #154; the build puts them next to it): into the tree's Libs drawer (LIBS:).
        var libraries = FsUae.LibrariesBeside(exe);
        if (libraries.Count > 0)
        {
            Directory.CreateDirectory(Path.Combine(runDir, "libs"));
            foreach (var lib in libraries) File.Copy(lib, Path.Combine(runDir, "libs", Path.GetFileName(lib)));
        }
        File.WriteAllText(Path.Combine(runDir, "startup.txt"), StartupLines(name, programArgs));
        File.WriteAllText(Path.Combine(runDir, "run.sh"), Script.Replace("\r\n", "\n") + "\n");

        var secs = Math.Max(10, (int)timeout.TotalSeconds);
        var psi = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in DockerArgs(runDir, image, Arch(target), name, secs, OperatingSystem.IsWindows() ? null : Os4Vm.UserSpec()))
            psi.ArgumentList.Add(a);
        string said;
        int exit;
        try
        {
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var outText = p.StandardOutput.ReadToEndAsync();
            // the copy of the tree and the Xvfb start come on top of the wait for the program
            if (!p.WaitForExit(TimeSpan.FromSeconds(secs + 180)))
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit();
                return Fail($"docker run of {image} did not end within {secs + 180}s", runDir);
            }
            exit = p.ExitCode;
            said = (outText.Result + err.Result).Trim();
        }
        catch (System.ComponentModel.Win32Exception) { return Fail("docker not found: hosted AROS runs in the esharp-aros-hosted image", runDir); }

        if (exit == 125)
            return Fail($"docker could not run image '{image}' (exit code 125): {said}. Build it from docker/aros-hosted in the E# repository " +
                        $"(https://github.com/GameGyrafe/amigaesharp): docker build -t {ImageX86_64} docker/aros-hosted, or with --build-arg AROS_ARCH=i386 -t {ImageI386}", runDir);
        if (exit != 0) return Fail($"the hosted AROS run in {image} failed (exit code {exit}): {said}", runDir);

        string? Read(string f) { var path = Path.Combine(runDir, f); return File.Exists(path) ? File.ReadAllText(path) : null; }
        var output = Read("out.txt") ?? "";
        var finished = File.Exists(Path.Combine(runDir, "done"));
        var traps = CountTraps(Read("boot.log"));
        var rc = ParseReturnCode(Read("rc.txt"));
        if (!finished)
        {
            foreach (var l in (Read("boot.log") ?? "").Split('\n').Where(l => l.Contains("Trap", StringComparison.Ordinal)).Take(5))
                Console.Error.WriteLine("  " + l.TrimEnd('\r'));
            Console.Error.WriteLine($"esharp: the program did not finish within {secs}s on hosted AROS ({traps} trap line(s); AROS's debug output is {Path.Combine(runDir, "boot.log")})");
            return new Result(1, output, rc, traps, false, runDir);
        }
        return new Result(0, output, rc, traps, true, runDir);
    }

    private static Result Fail(string message, string runDir)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return new Result(1, "", null, 0, false, runDir);
    }
}
