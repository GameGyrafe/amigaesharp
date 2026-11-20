using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ESharp.Assets;

namespace ESharp.Cli;

/// <summary>
/// Runs a program built for the os4 target in an AmigaOS 4.1 (Pegasos II) QEMU VM, unattended (tools/os4vm/README.md).
/// The program goes on a CD image labelled ESTEST with a script <c>autorun</c> and the small helper <c>estype</c>
/// (tools/os4vm/estype.c). A one-time hook in the VM's S:User-Startup executes ESTEST:autorun at boot; the script runs
/// <c>program | estype</c>, which copies each output line to the kernel debug output (prefixed "ES: "), and QEMU writes that
/// to a serial file on the host. Start and end markers carry a per-run nonce. The VM boots on a throwaway qcow2 overlay of
/// amiga.img, so test runs never change the installed disk. Screenshots use the QEMU monitor's screendump.
/// </summary>
public static class Os4Vm
{
    public const string LinePrefix = "ES: ";
    public const string Volume = "ESTEST";

    /// <summary>The VM directory from ESHARP_OS4_VM, or an error message.</summary>
    public static string? FindVm(string? setting, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(setting))
        {
            error = "set ESHARP_OS4_VM to your AmigaOS 4.1 VM dir (see os4vm/README.md next to esharp.dll, tools/os4vm/README.md in the repository)";
            return null;
        }
        var dir = Path.GetFullPath(setting.StartsWith("~")
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + setting[1..]
            : setting);
        var missing = new[] { "bboot", "Kickstart.zip", "amiga.img" }.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
        if (missing.Count > 0)
        {
            error = $"ESHARP_OS4_VM ({dir}) lacks {string.Join(", ", missing)} (see os4vm/README.md next to esharp.dll, tools/os4vm/README.md in the repository)";
            return null;
        }
        return dir;
    }

    /// <summary>
    /// The AmigaDOS script the hook executes: markers around the run, whose output is piped to the debug port. The run is the
    /// script <see cref="EsRun"/> (ESTEST:esrun), so that the program's return code can be written after it: a pipe hides it.
    /// </summary>
    public static string Autorun(string program, string nonce, string? programArgs = null) =>
        "; E# os4 run harness (esharp run/test --target os4)\n" +
        "FailAt 1000000\n" +
        "Stack 1000000\n" +
        $"{Volume}:estype -t \"ESHARP-BEGIN {nonce}\"\n" +
        "CD RAM:\n" +
        $"Execute {Volume}:esrun | {Volume}:estype\n" +
        $"{Volume}:estype -t \"ESHARP-END {nonce}\"\n";

    /// <summary>
    /// ESTEST:esrun, executed with its output in the pipe: the program, then its return code (the shell's RC) as a marker line
    /// straight to the debug port ("ESHARP-RC nonce n"). FailAt is raised here too, so a FAIL return does not end the script first.
    /// </summary>
    public static string EsRun(string program, string nonce, string? programArgs = null) =>
        "; E# os4 run harness: the program and its return code\n" +
        "FailAt 1000000\n" +
        "Stack 1000000\n" +
        "CD RAM:\n" +
        $"{Volume}:{program}{(string.IsNullOrWhiteSpace(programArgs) ? "" : " " + programArgs.Trim())}\n" +
        $"{Volume}:estype -t \"ESHARP-RC {nonce} $RC\"\n";

    /// <summary>
    /// Why an <c>--args</c> line cannot go into an AmigaDOS script or startup sequence (newlines, redirections, ';' comments, pipes and
    /// '$' variables would change the script itself), or null when it is fine. Words are split at spaces and "quoted text" stays together.
    /// </summary>
    public static string? ProgramArgsError(string? line) =>
        line != null && line.IndexOfAny(new[] { '\n', '\r', '>', '<', ';', '|', '$' }) >= 0
            ? "--args: no newlines, redirections, '|', '$' or ';' in the line" : null;

    /// <summary>What the serial file says so far about one run.</summary>
    public sealed record SerialState(bool Begun, bool Ended, string Output, bool Shot, string Other = "", int? ReturnCode = null);

    /// <summary>
    /// Reads the serial text: the program's lines are those with the "ES: " prefix after the start marker; kernel and driver
    /// messages share the port and are skipped. A line SHOT (as the m68k harness uses it) asks for the screenshot.
    /// </summary>
    public static SerialState Parse(string serial, string nonce)
    {
        bool begun = false, ended = false, shot = false;
        int? rc = null;
        var output = new StringBuilder();
        var other = new StringBuilder();   // kernel debug lines during the run: Trace output, drivers, crash reports
        var lines = serial.Split('\n');
        // The last piece may still be arriving: use only lines whose newline is there.
        foreach (var raw in lines.Take(lines.Length - 1))
        {
            var line = raw.TrimEnd('\r');
            if (!begun)
            {
                if (line.Trim() == "ESHARP-BEGIN " + nonce) begun = true;
                continue;
            }
            if (line.Trim() == "ESHARP-END " + nonce) { ended = true; break; }
            var rcMarker = "ESHARP-RC " + nonce + " ";
            if (line.Trim().StartsWith(rcMarker, StringComparison.Ordinal))
            {
                if (int.TryParse(line.Trim()[rcMarker.Length..], System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out var n)) rc = n;
                continue;
            }
            // The prefix must start the line. The only thing allowed before it is stray control characters (a NUL, a lone CR or
            // an escape left by the kernel's serial output); a "ES: " in the middle of a kernel or driver line is not ours.
            var start = 0;
            while (start < line.Length && line[start] < ' ') start++;
            if (string.CompareOrdinal(line, start, LinePrefix, 0, LinePrefix.Length) != 0)
            { if (line.Length > 0) other.Append(line).Append('\n'); continue; }
            var text = line[(start + LinePrefix.Length)..];
            if (text == "SHOT") shot = true;
            output.Append(text).Append('\n');
        }
        return new SerialState(begun, ended, output.ToString(), shot, other.ToString(), rc);
    }

    public static List<string> QemuArgs(string vm, string overlay, string iso, string serial, string monitor)
    {
        var a = new List<string>();
        var data = Path.Combine(vm, "qemu-data");
        if (Directory.Exists(data)) a.AddRange(new[] { "-L", data });
        a.AddRange(new[]
        {
            "-L", "/usr/share/qemu", "-M", "pegasos2", "-m", "2048",
            "-kernel", Path.Combine(vm, "bboot"), "-initrd", Path.Combine(vm, "Kickstart.zip"),
            "-append", "serial",                        // kernel debug output (estype) to the serial port
            "-vga", "none", "-device", "sm501", "-rtc", "base=localtime",
            "-display", "none", "-serial", "file:" + serial, "-monitor", "unix:" + monitor + ",server,nowait",
            "-drive", $"format=raw,file={iso},index=2,media=cdrom,readonly=on",
            "-drive", $"format=qcow2,file={overlay},index=0,media=disk",
        });
        return a;
    }

    /// <summary>PIDs of running qemu-system-ppc processes that use the VM directory (one VM at a time).</summary>
    public static List<int> RunningVms(string vm)
    {
        var pids = new List<int>();
        if (!Directory.Exists("/proc")) return pids;
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                var args = File.ReadAllText(Path.Combine(dir, "cmdline")).Split('\0');
                if (args.Length == 0 || Path.GetFileName(args[0]) != "qemu-system-ppc") continue;
                string? cwd = null;
                try { cwd = new DirectoryInfo(Path.Combine(dir, "cwd")).LinkTarget; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (cwd == vm || args.Any(x => x.Contains(vm, StringComparison.Ordinal))) pids.Add(pid);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return pids;
    }

    public static (int Exit, string Output) Run(string exe, TimeSpan timeout, string? docker, string? screenshot, string? serialOut = null, bool echo = true, string? input = null, string? programArgs = null, RunInfo? info = null)
    {
        if (ProgramArgsError(programArgs) is { } argsError) return Fail(argsError);
        List<Os4Input.Step>? inputSteps = null;
        if (input != null)
        {
            try { inputSteps = Os4Input.Translate(input); }
            catch (FormatException e) { return Fail(e.Message); }
        }
        var vm = FindVm(Environment.GetEnvironmentVariable("ESHARP_OS4_VM"), out var vmError);
        if (vm == null) return Fail(vmError!);
        foreach (var tool in new[] { "qemu-system-ppc", "qemu-img" })
            if (FindOnPath(tool) == null) return Fail($"{tool} not found on PATH (the os4 harness needs QEMU 10 or later with pegasos2)");

        var name = Path.GetFileName(exe);
        var runDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe))!, "run");
        if (Directory.Exists(runDir)) Directory.Delete(runDir, recursive: true);
        var cd = Path.Combine(runDir, "cd");
        Directory.CreateDirectory(cd);
        File.Copy(exe, Path.Combine(cd, name));
        // The icon `esharp build` wrote next to the program goes on the CD too, so the program shows (and starts by double-click) on Workbench.
        if (File.Exists(exe + ".info")) File.Copy(exe + ".info", Path.Combine(cd, name + ".info"));
        var nonce = Guid.NewGuid().ToString("N")[..12];
        File.WriteAllText(Path.Combine(cd, "autorun"), Autorun(name, nonce, programArgs));
        File.WriteAllText(Path.Combine(cd, "esrun"), EsRun(name, nonce, programArgs));
        var image = docker ?? Program.DefaultImage(ESharp.Compiler.Targets.Os4);
        if (!BuildEstype(runDir, cd, docker)) return Fail("could not build the estype helper (needs ppc-amigaos-gcc or --docker)");
        var iso = Path.Combine(runDir, "estest.iso");
        if (!MakeIso(runDir, cd, iso, image)) return Fail("could not make the CD image (needs genisoimage, or docker with " + image + ")");

        // One VM at a time: a lock for esharp runs, and a check for a VM someone started by hand.
        using var lockFile = WaitForLock(timeout);
        if (lockFile == null) return Fail("another esharp os4 run is still using the VM");
        ReapStale(PidFile);   // a qemu left by an esharp that was killed without cleaning up (it records its pid below)
        var busy = RunningVms(vm);
        if (busy.Count > 0) return Fail($"the VM is running already (qemu pid {string.Join(", ", busy)}); stop it first (one VM at a time)");

        // The overlay and the monitor socket go to a short path under /tmp: unix socket paths are limited to ~107 bytes.
        var work = Path.Combine(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), "esharp-os4-" + nonce);
        Directory.CreateDirectory(work);
        var serial = Path.Combine(runDir, "serial.log");
        var monitor = Path.Combine(work, "mon.sock");
        Process? qemu = null;
        Cleanup? cleanup = null;
        var signals = new List<PosixSignalRegistration>();
        EventHandler? onExit = null;
        try
        {
            var overlay = Path.Combine(work, "overlay.qcow2");
            if (RunTool("qemu-img", "create", "-q", "-f", "qcow2", "-b", Path.Combine(vm, "amiga.img"), "-F", "raw", overlay) != 0)
                return Fail("qemu-img could not create the overlay");
            var psi = new ProcessStartInfo("qemu-system-ppc")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in QemuArgs(vm, overlay, iso, serial, monitor)) psi.ArgumentList.Add(a);
            qemu = Process.Start(psi)!;
            // Whatever stops esharp from here on (SIGTERM/SIGHUP/SIGINT from a gate timeout or kill, a normal exit), the QEMU
            // this run started and its work dir go with it. The pid file lets the next run reap one that still got orphaned.
            cleanup = new Cleanup(qemu, monitor, work);
            try { File.WriteAllText(PidFile, $"{qemu.Id}\n{work}\n"); } catch (IOException) { }
            var c = cleanup;
            foreach (var sig in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGINT })
                signals.Add(PosixSignalRegistration.Create(sig, _ => c.Run(TimeSpan.FromSeconds(3))));
            onExit = (_, _) => c.Run(TimeSpan.FromSeconds(3));
            AppDomain.CurrentDomain.ProcessExit += onExit;
            var qemuErr = new StringBuilder();
            qemu.OutputDataReceived += (_, _) => { };
            qemu.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (qemuErr) qemuErr.AppendLine(e.Data); };
            qemu.BeginOutputReadLine();
            qemu.BeginErrorReadLine();

            var deadline = DateTime.UtcNow + timeout;
            var shotTaken = screenshot == null && inputSteps == null;
            var state = new SerialState(false, false, "", false);
            var printed = 0;
            while (DateTime.UtcNow < deadline && !qemu.HasExited)
            {
                Thread.Sleep(250);
                state = Parse(ReadShared(serial), nonce);
                if (echo && state.Output.Length > printed) { Console.Out.Write(state.Output[printed..]); printed = state.Output.Length; }
                if (state.Shot && !shotTaken)
                {
                    shotTaken = true;
                    Thread.Sleep(1000);   // let the program draw its frame
                    if (inputSteps != null && !Os4Input.Send(monitor, inputSteps)) Console.Error.WriteLine("esharp: could not send the input through the QEMU monitor");
                    if (screenshot == null) { }
                    else if (Screendump(monitor, Path.Combine(runDir, "shot.ppm"), screenshot!)) Console.Error.WriteLine($"Saved screenshot {screenshot}");
                    else Console.Error.WriteLine("esharp: could not take the screenshot through the QEMU monitor");
                }
                if (state.Ended) break;
            }
            if (!shotTaken) Console.Error.WriteLine("esharp: the program never printed SHOT, so no screenshot was taken and no input was sent");
            if (serialOut != null)
            {
                // The non-program lines of the run (Trace output through DebugPrintF, kernel and driver messages).
                File.WriteAllText(serialOut, state.Other);
                Console.Error.WriteLine($"--- serial output ({serialOut}; the whole log is {serial}) ---");
                Console.Error.Write(state.Other);
            }
            if (qemu.HasExited && !state.Ended)
                return Fail($"qemu-system-ppc stopped (exit code {qemu.ExitCode}) before the program finished: {qemuErr.ToString().Trim()}", state.Output);
            if (!state.Ended)
            {
                var tail = ReadShared(serial).Split('\n').TakeLast(30);
                Console.Error.WriteLine($"--- last serial lines ({serial}) ---");
                foreach (var l in tail) Console.Error.WriteLine(l);
                return Fail(state.Begun
                    ? $"program did not finish within {timeout.TotalSeconds:0}s (a crash leaves the Grim Reaper waiting; see {serial})"
                    : $"the program never started within {timeout.TotalSeconds:0}s: is the test hook installed (esharp os4vm install-hook)? See {serial}",
                    state.Output);
            }
            if (info != null) info.ReturnCode = state.ReturnCode;
            else if (state.ReturnCode is { } nonZero and not 0) Console.Error.WriteLine($"esharp: the program returned {nonZero}");
            return (0, state.Output);
        }
        finally
        {
            foreach (var r in signals) r.Dispose();
            if (onExit != null) AppDomain.CurrentDomain.ProcessExit -= onExit;
            cleanup?.Run(TimeSpan.FromSeconds(10));
            try { File.Delete(PidFile); } catch (IOException) { }
            qemu?.Dispose();
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    private static string PidFile => Path.Combine(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), "esharp-os4vm.pid");

    /// <summary>
    /// Stops the QEMU that one run started (only that pid) and removes its work dir. Safe to call from a signal handler, the
    /// exit handler and the finally block: the first call does the work, later ones return.
    /// </summary>
    private sealed class Cleanup
    {
        private readonly Process qemu;
        private readonly string monitor, work;
        private int done;
        public Cleanup(Process qemu, string monitor, string work) { this.qemu = qemu; this.monitor = monitor; this.work = work; }

        public void Run(TimeSpan grace)
        {
            if (Interlocked.Exchange(ref done, 1) != 0) return;
            try
            {
                if (!qemu.HasExited)
                {
                    try { MonitorCommand(monitor, "quit"); } catch (Exception e) when (e is SocketException or IOException) { }
                    if (!qemu.WaitForExit((int)grace.TotalMilliseconds)) { qemu.Kill(); qemu.WaitForExit(); }
                }
            }
            catch (InvalidOperationException) { }
            try { Directory.Delete(work, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The pid file written by a run holds the qemu pid and the work dir. If that qemu is still alive and really is a
    /// qemu-system-ppc using that work dir (so a reused pid is never touched), a previous esharp was killed without cleaning
    /// up: kill that one process and remove the work dir. Returns true when something was reaped.
    /// </summary>
    public static bool ReapStale(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile)) return false;
            var lines = File.ReadAllLines(pidFile);
            var reaped = false;
            if (lines.Length >= 2 && int.TryParse(lines[0], out var pid) && lines[1].Contains("esharp-os4-"))
            {
                try
                {
                    var args = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0');
                    if (Path.GetFileName(args[0]) == "qemu-system-ppc" && args.Any(a => a.Contains(lines[1], StringComparison.Ordinal)))
                    {
                        using var p = Process.GetProcessById(pid);
                        p.Kill();
                        p.WaitForExit(5000);
                        reaped = true;
                    }
                }
                catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { }
                try { Directory.Delete(lines[1], recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            File.Delete(pidFile);
            return reaped;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static (int, string) Fail(string message, string output = "")
    {
        Console.Error.WriteLine($"esharp: {message}");
        return (1, output);
    }

    private static FileStream? WaitForLock(TimeSpan timeout)
    {
        var path = Path.Combine(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), "esharp-os4vm.lock");
        var deadline = DateTime.UtcNow + timeout;
        var said = false;
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            {
                if (DateTime.UtcNow > deadline) return null;
                if (!said) { Console.Error.WriteLine("esharp: waiting for another os4 run to finish..."); said = true; }
                Thread.Sleep(2000);
            }
        }
    }

    /// <summary>estype for the CD: compiled once per output directory from the source embedded in esharp.</summary>
    private static bool BuildEstype(string runDir, string cd, string? docker)
    {
        using var s = typeof(Os4Vm).Assembly.GetManifestResourceStream("estype.c");
        if (s == null) return false;
        using (var f = File.Create(Path.Combine(runDir, "estype.c"))) s.CopyTo(f);
        var outDir = Path.GetDirectoryName(runDir)!;
        var cached = Path.Combine(outDir, "estype");
        var cachedSrc = Path.Combine(outDir, "estype.c");
        if (!File.Exists(cached) || !File.Exists(cachedSrc) || File.ReadAllText(cachedSrc) != File.ReadAllText(Path.Combine(runDir, "estype.c")))
        {
            File.Copy(Path.Combine(runDir, "estype.c"), cachedSrc, overwrite: true);
            int rc = docker == null && FindOnPath("ppc-amigaos-gcc") != null
                ? RunTool("ppc-amigaos-gcc", "-O2", "-o", cached, cachedSrc)
                : RunTool("docker", "run", "--rm", "--user", UserSpec(), "-v", $"{outDir}:/work", "-w", "/work",
                    docker ?? Program.DefaultImage(ESharp.Compiler.Targets.Os4), "ppc-amigaos-gcc", "-O2", "-o", "estype", "estype.c");
            if (rc != 0 || !File.Exists(cached)) { try { File.Delete(cachedSrc); } catch (IOException) { } return false; }
        }
        File.Copy(cached, Path.Combine(cd, "estype"));
        return true;
    }

    private static bool MakeIso(string runDir, string cd, string iso, string image)
    {
        // Rock Ridge permissions become the Amiga protection bits: rwx for everyone, so the files are executable in OS4.
        foreach (var f in Directory.GetFiles(cd))
            File.SetUnixFileMode(f, (UnixFileMode)0b111_101_101);
        var tool = FindOnPath("genisoimage") ?? FindOnPath("xorrisofs") ?? FindOnPath("mkisofs");
        var rc = tool != null
            ? RunTool(tool, "-quiet", "-R", "-V", Volume, "-o", iso, cd)
            : RunTool("docker", "run", "--rm", "--user", UserSpec(), "-v", $"{runDir}:/work", "-w", "/work", image,
                "genisoimage", "-quiet", "-R", "-V", Volume, "-o", Path.GetFileName(iso), "cd");
        return rc == 0 && File.Exists(iso);
    }

    [System.Runtime.InteropServices.DllImport("libc")] private static extern uint getuid();
    [System.Runtime.InteropServices.DllImport("libc")] private static extern uint getgid();

    /// <summary>docker --user for files the container writes into our directories (owned by us, not root).</summary>
    internal static string UserSpec() => $"{getuid()}:{getgid()}";

    private static int RunTool(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    private static string? FindOnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, tool)).FirstOrDefault(File.Exists);

    private static string ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.Latin1);
            return reader.ReadToEnd();
        }
        catch (IOException) { return ""; }
    }

    private static void MonitorCommand(string socketPath, string command)
    {
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        s.Connect(new UnixDomainSocketEndPoint(socketPath));
        s.ReceiveTimeout = 500;
        var buf = new byte[4096];
        try { s.Receive(buf); } catch (SocketException) { }   // the banner
        s.Send(Encoding.ASCII.GetBytes(command + "\n"));
        Thread.Sleep(300);
        try { while (s.Available > 0 && s.Receive(buf) > 0) { } } catch (SocketException) { }
    }

    /// <summary>QEMU monitor screendump (a PPM of the display) saved as a PNG.</summary>
    private static bool Screendump(string monitor, string ppm, string png)
    {
        try
        {
            if (File.Exists(ppm)) File.Delete(ppm);
            MonitorCommand(monitor, "screendump " + ppm);
            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(250);
                if (!File.Exists(ppm)) continue;
                try
                {
                    File.WriteAllBytes(png, Png.Write(SdlRun.ReadPpm(File.ReadAllBytes(ppm))));
                    return true;
                }
                catch (InvalidDataException) { }   // still being written
            }
            return false;
        }
        catch (Exception e) when (e is SocketException or IOException) { return false; }
    }
}
