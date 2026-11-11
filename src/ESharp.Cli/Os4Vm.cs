using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
            error = "set ESHARP_OS4_VM to your AmigaOS 4.1 VM dir (see tools/os4vm/README.md)";
            return null;
        }
        var dir = Path.GetFullPath(setting.StartsWith("~")
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + setting[1..]
            : setting);
        var missing = new[] { "bboot", "Kickstart.zip", "amiga.img" }.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
        if (missing.Count > 0)
        {
            error = $"ESHARP_OS4_VM ({dir}) lacks {string.Join(", ", missing)} (see tools/os4vm/README.md)";
            return null;
        }
        return dir;
    }

    /// <summary>The AmigaDOS script the hook executes: markers around the program, its output piped to the debug port.</summary>
    public static string Autorun(string program, string nonce) =>
        "; E# os4 run harness (esharp run/test --target os4)\n" +
        "FailAt 1000000\n" +
        "Stack 1000000\n" +
        $"{Volume}:estype -t \"ESHARP-BEGIN {nonce}\"\n" +
        "CD RAM:\n" +
        $"{Volume}:{program} | {Volume}:estype\n" +
        $"{Volume}:estype -t \"ESHARP-END {nonce}\"\n";

    /// <summary>What the serial file says so far about one run.</summary>
    public sealed record SerialState(bool Begun, bool Ended, string Output, bool Shot, string Other = "");

    /// <summary>
    /// Reads the serial text: the program's lines are those with the "ES: " prefix after the start marker; kernel and driver
    /// messages share the port and are skipped. A line SHOT (as the m68k harness uses it) asks for the screenshot.
    /// </summary>
    public static SerialState Parse(string serial, string nonce)
    {
        bool begun = false, ended = false, shot = false;
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
            var at = line.IndexOf(LinePrefix, StringComparison.Ordinal);
            if (at < 0) { if (line.Length > 0) other.Append(line).Append('\n'); continue; }
            var text = line[(at + LinePrefix.Length)..];
            if (text == "SHOT") shot = true;
            output.Append(text).Append('\n');
        }
        return new SerialState(begun, ended, output.ToString(), shot, other.ToString());
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

    public static (int Exit, string Output) Run(string exe, TimeSpan timeout, string? docker, string? screenshot, string? serialOut = null, bool echo = true)
    {
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
        var nonce = Guid.NewGuid().ToString("N")[..12];
        File.WriteAllText(Path.Combine(cd, "autorun"), Autorun(name, nonce));
        var image = docker ?? Program.DefaultImage(ESharp.Compiler.Targets.Os4);
        if (!BuildEstype(runDir, cd, docker)) return Fail("could not build the estype helper (needs ppc-amigaos-gcc or --docker)");
        var iso = Path.Combine(runDir, "estest.iso");
        if (!MakeIso(runDir, cd, iso, image)) return Fail("could not make the CD image (needs genisoimage, or docker with " + image + ")");

        // One VM at a time: a lock for esharp runs, and a check for a VM someone started by hand.
        using var lockFile = WaitForLock(timeout);
        if (lockFile == null) return Fail("another esharp os4 run is still using the VM");
        var busy = RunningVms(vm);
        if (busy.Count > 0) return Fail($"the VM is running already (qemu pid {string.Join(", ", busy)}); stop it first (one VM at a time)");

        // The overlay and the monitor socket go to a short path under /tmp: unix socket paths are limited to ~107 bytes.
        var work = Path.Combine(Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath(), "esharp-os4-" + nonce);
        Directory.CreateDirectory(work);
        var serial = Path.Combine(runDir, "serial.log");
        var monitor = Path.Combine(work, "mon.sock");
        Process? qemu = null;
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
            var qemuErr = new StringBuilder();
            qemu.OutputDataReceived += (_, _) => { };
            qemu.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (qemuErr) qemuErr.AppendLine(e.Data); };
            qemu.BeginOutputReadLine();
            qemu.BeginErrorReadLine();

            var deadline = DateTime.UtcNow + timeout;
            var shotTaken = screenshot == null;
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
                    if (Screendump(monitor, Path.Combine(runDir, "shot.ppm"), screenshot!)) Console.Error.WriteLine($"Saved screenshot {screenshot}");
                    else Console.Error.WriteLine("esharp: could not take the screenshot through the QEMU monitor");
                }
                if (state.Ended) break;
            }
            if (!shotTaken) Console.Error.WriteLine("esharp: the program never printed SHOT, so no screenshot was taken");
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
                    : $"the program never started within {timeout.TotalSeconds:0}s: is the test hook installed (tools/os4vm/os4vm.py install-hook)? See {serial}",
                    state.Output);
            }
            return (0, state.Output);
        }
        finally
        {
            if (qemu != null)
            {
                if (!qemu.HasExited)
                {
                    try { MonitorCommand(monitor, "quit"); } catch (SocketException) { } catch (IOException) { }
                    if (!qemu.WaitForExit(10000)) { qemu.Kill(); qemu.WaitForExit(); }
                }
                qemu.Dispose();
            }
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
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
    private static string UserSpec() => $"{getuid()}:{getgid()}";

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
