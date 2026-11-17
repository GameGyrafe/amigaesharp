using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ESharp.Cli;

/// <summary>
/// Launches a built 68k program in FS-UAE with its output directory mounted as the hard drive
/// (DH0:), booting straight into the program through S:Startup-sequence.
/// </summary>
internal static class FsUae
{
    public static int Run(string exe, bool headless, TimeSpan timeout, string? kickstart, string model = "A1200", string? fsuaePath = null, string? screenshot = null, string? input = null, string? serial = null, string? programArgs = null, string? muiDrawer = null, string? workbench = null)
    {
        var name = Path.GetFileName(exe);
        var runDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe))!, "run");
        var dh0 = Path.Combine(runDir, "dh0");
        if (Directory.Exists(runDir)) Directory.Delete(runDir, recursive: true);
        Directory.CreateDirectory(Path.Combine(dh0, "S"));
        File.Copy(exe, Path.Combine(dh0, name));
        if (serial != null) File.WriteAllText(serial, "");   // FS-UAE writes the Amiga's serial output into this file

        // Headless runs redirect the program's output to a file and then write a marker, so the
        // host can tell when the program has finished without scraping the screen.
        // AmigaDOS wants the redirection right after the command name, before the program's own arguments.
        if (Os4Vm.ProgramArgsError(programArgs) is { } argsError) { Console.Error.WriteLine($"esharp: {argsError}"); return 1; }
        var line = string.IsNullOrWhiteSpace(programArgs) ? "" : " " + programArgs.Trim();
        var pre = muiDrawer == null ? "" : string.Concat(Mui.StartupLines(IconStackSize(exe)).Select(l => l + "\n"));
        File.WriteAllText(Path.Combine(dh0, "S", "Startup-sequence"), pre + (headless
            ? $"dh0:{name} >dh0:out.txt{line}\necho done >dh0:done\n"
            : $"dh0:{name}{line}\n"));

        var config = Path.Combine(runDir, "run.fs-uae");
        File.WriteAllText(config,
            "[fs-uae]\n" +
            $"amiga_model = {model}\n" +
            $"kickstart_file = {(kickstart ?? "internal")}\n" +
            $"hard_drive_0 = {dh0}\n" +
            "hard_drive_0_label = DH0\n" +
            (muiDrawer != null ? Mui.ConfigLines(muiDrawer, workbench!) : "") +
            (serial != null ? $"serial_port = {Path.GetFullPath(serial)}\n" : "") +
            "window_width = 640\nwindow_height = 480\nfullscreen = 0\n");

        Process? xvfb = null;
        try
        {
            var psi = new ProcessStartInfo(fsuaePath ?? Environment.GetEnvironmentVariable("ESHARP_FSUAE") ?? "fs-uae") { UseShellExecute = false };
            psi.ArgumentList.Add($"--base-dir={Path.Combine(runDir, "fsuae")}");
            psi.ArgumentList.Add(config);

            if (headless)
            {
                psi.Environment["SDL_AUDIODRIVER"] = "dummy";
                if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                {
                    xvfb = StartXvfb(psi);
                    if (xvfb == null) return Fail("headless run needs a display: install xvfb or set DISPLAY");
                }
                psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            }

            Process proc;
            try { proc = Process.Start(psi)!; }
            catch (System.ComponentModel.Win32Exception)
            {
                return Fail($"'{psi.FileName}' not found. Install FS-UAE, then put it on PATH or pass --fsuae <path> (macOS: /Applications/FSUAE.app/Contents/MacOS/fs-uae).");
            }

            using (proc)
            {
                if (!headless)
                {
                    proc.WaitForExit();
                    return proc.ExitCode;
                }

                // Drain output so FS-UAE never blocks on a full pipe.
                proc.OutputDataReceived += (_, _) => { };
                proc.ErrorDataReceived += (_, _) => { };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                var marker = Path.Combine(dh0, "done");
                var outPath = Path.Combine(dh0, "out.txt");
                var deadline = DateTime.UtcNow + timeout;
                var shot = screenshot == null && input == null;
                while (!File.Exists(marker) && !proc.HasExited && DateTime.UtcNow < deadline)
                {
                    if (!shot && OutputContains(outPath, "SHOT"))
                    {
                        Thread.Sleep(1000); // let the emulator draw the frame
                        shot = true;
                        var display = psi.Environment.TryGetValue("DISPLAY", out var d) && d != null ? d : Environment.GetEnvironmentVariable("DISPLAY");
                        if (input != null && !Xtest.Run(display, input)) Console.Error.WriteLine("esharp: could not send input (needs an X display and libXtst)");
                        if (screenshot != null)
                        {
                            if (!Screenshot.Capture(display, screenshot)) Console.Error.WriteLine("esharp: could not capture the screen (is xwd installed?)");
                            else Console.Error.WriteLine($"Saved screenshot {screenshot}");
                        }
                    }
                    Thread.Sleep(250);
                }
                if (!shot) Console.Error.WriteLine("esharp: the program never printed SHOT, so no screenshot was taken");

                var finished = File.Exists(marker);
                if (!proc.HasExited) { proc.Kill(entireProcessTree: true); proc.WaitForExit(); }
                if (!finished) PrintSerial(serial);
                if (!finished) return Fail($"program did not finish within {timeout.TotalSeconds:0}s (see {runDir})");

                var outFile = Path.Combine(dh0, "out.txt");
                if (File.Exists(outFile)) Console.Out.Write(File.ReadAllText(outFile));
                PrintSerial(serial);
                return 0;
            }
        }
        finally
        {
            if (xvfb is { HasExited: false }) xvfb.Kill();
            xvfb?.Dispose();
        }
    }

    /// <summary>The stack size in the icon the build wrote next to the program (EsStackSize), or the default without one.</summary>
    internal static int IconStackSize(string exe)
    {
        var info = exe + ".info";
        try
        {
            if (File.Exists(info) && ESharp.Assets.DiskObject.Read(File.ReadAllBytes(info)).StackSize is var s and >= 4096) return s;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException) { }
        return IconStep.DefaultStackSize;
    }

    private static void PrintSerial(string? serial)
    {
        if (serial == null || !File.Exists(serial)) return;
        var text = File.ReadAllText(serial);
        Console.Error.WriteLine($"--- serial output ({serial}) ---");
        Console.Error.Write(text.Length == 0 || text.EndsWith("\n") ? text : text + "\n");
    }

    private static bool OutputContains(string path, string text)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd().Contains(text);
        }
        catch (IOException) { return false; }
    }

    private static Process? StartXvfb(ProcessStartInfo target)
    {
        // -displayfd lets Xvfb pick a free display itself, so stale sockets from other users do not matter.
        try
        {
            var x = Process.Start(new ProcessStartInfo("Xvfb", "-displayfd 1 -screen 0 800x600x24")
            { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
            if (x == null) return null;
            var line = x.StandardOutput.ReadLine();
            if (x.HasExited || !int.TryParse(line, out var n))
            {
                if (!x.HasExited) x.Kill();
                return null;
            }
            target.Environment["DISPLAY"] = $":{n}";
            return x;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }
}
