using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ESharp.Cli;

/// <summary>
/// Launches a built 68k program in FS-UAE with its output directory mounted as the hard drive
/// (DH0:), booting straight into the program through S:Startup-sequence.
/// </summary>
internal static class FsUae
{
    public static int Run(string exe, bool headless, TimeSpan timeout, string? kickstart)
    {
        var name = Path.GetFileName(exe);
        var runDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe))!, "run");
        var dh0 = Path.Combine(runDir, "dh0");
        if (Directory.Exists(runDir)) Directory.Delete(runDir, recursive: true);
        Directory.CreateDirectory(Path.Combine(dh0, "S"));
        File.Copy(exe, Path.Combine(dh0, name));

        // Headless runs redirect the program's output to a file and then write a marker, so the
        // host can tell when the program has finished without scraping the screen.
        File.WriteAllText(Path.Combine(dh0, "S", "Startup-sequence"), headless
            ? $"dh0:{name} >dh0:out.txt\necho done >dh0:done\n"
            : $"dh0:{name}\n");

        var config = Path.Combine(runDir, "run.fs-uae");
        File.WriteAllText(config,
            "[fs-uae]\n" +
            "amiga_model = A500\n" +
            $"kickstart_file = {(kickstart ?? "internal")}\n" +
            $"hard_drive_0 = {dh0}\n" +
            "hard_drive_0_label = DH0\n" +
            "window_width = 640\nwindow_height = 480\nfullscreen = 0\n");

        Process? xvfb = null;
        try
        {
            var psi = new ProcessStartInfo("fs-uae") { UseShellExecute = false };
            psi.ArgumentList.Add($"--base-dir={Path.Combine(runDir, "fsuae")}");
            psi.ArgumentList.Add(config);

            if (headless)
            {
                psi.Environment["SDL_AUDIODRIVER"] = "dummy";
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
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
                return Fail("fs-uae not found. Install FS-UAE (apt install fs-uae) and try again.");
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
                var deadline = DateTime.UtcNow + timeout;
                while (!File.Exists(marker) && !proc.HasExited && DateTime.UtcNow < deadline)
                    Thread.Sleep(250);

                var finished = File.Exists(marker);
                if (!proc.HasExited) { proc.Kill(entireProcessTree: true); proc.WaitForExit(); }
                if (!finished) return Fail($"program did not finish within {timeout.TotalSeconds:0}s (see {runDir})");

                var outFile = Path.Combine(dh0, "out.txt");
                if (File.Exists(outFile)) Console.Out.Write(File.ReadAllText(outFile));
                return 0;
            }
        }
        finally
        {
            if (xvfb is { HasExited: false }) xvfb.Kill();
            xvfb?.Dispose();
        }
    }

    private static Process? StartXvfb(ProcessStartInfo target)
    {
        for (var n = 99; n < 120; n++)
        {
            if (File.Exists($"/tmp/.X{n}-lock")) continue;
            try
            {
                var x = Process.Start(new ProcessStartInfo("Xvfb", $":{n} -screen 0 800x600x24")
                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                if (x == null) return null;
                Thread.Sleep(1500);
                if (x.HasExited) continue;
                target.Environment["DISPLAY"] = $":{n}";
                return x;
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
        return null;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }
}
