using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ESharp.Assets;

namespace ESharp.Cli;

/// <summary>
/// Runs a program built for the sdl target. Headless, it runs with SDL's offscreen video and dummy audio drivers, inside the
/// esharp-sdl image when one is given (the binary is built there) or on this machine. Otherwise it runs on this machine's desktop
/// (SDL is linked statically, so nothing has to be installed). Scripted input (<c>--input</c>, the same syntax as for FS-UAE) is
/// handed to the program's SDL layer in ESHARP_INPUT and replayed after the program prints SHOT; the screenshot is the program's
/// frame (its own screen size, not the scaled window) written by the program as a PPM and turned into a PNG here.
/// </summary>
public static class SdlRun
{
    public static (int Exit, string Output) Run(string exe, bool headless, TimeSpan timeout, string? docker, string? screenshot, string? input, bool echo = true)
    {
        string? script = null;
        if (input != null)
        {
            try { script = TranslateInput(input); }
            catch (FormatException e) { Console.Error.WriteLine($"esharp: {e.Message}"); return (1, ""); }
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(exe))!;
        var name = Path.GetFileName(exe);
        var ppm = Path.Combine(dir, name + ".shot.ppm");
        if (File.Exists(ppm)) File.Delete(ppm);

        var env = new List<(string, string)>();
        if (headless) { env.Add(("SDL_VIDEODRIVER", "offscreen")); env.Add(("SDL_AUDIODRIVER", "dummy")); }
        if (script != null) env.Add(("ESHARP_INPUT", script));
        var inDocker = headless && docker != null;
        if (screenshot != null) env.Add(("ESHARP_SCREENSHOT", inDocker ? "/work/" + name + ".shot.ppm" : ppm));

        ProcessStartInfo psi;
        string? container = null;
        if (inDocker)
        {
            container = "esharp-sdl-run-" + Guid.NewGuid().ToString("N")[..12];
            psi = new ProcessStartInfo("docker") { UseShellExecute = false };
            foreach (var a in new[] { "run", "--rm", "--name", container, "-v", $"{dir}:/work", "-w", "/work" }) psi.ArgumentList.Add(a);
            foreach (var (k, v) in env) { psi.ArgumentList.Add("-e"); psi.ArgumentList.Add($"{k}={v}"); }
            psi.ArgumentList.Add(docker!);
            psi.ArgumentList.Add("./" + name);
        }
        else
        {
            psi = new ProcessStartInfo(Path.GetFullPath(exe)) { UseShellExecute = false, WorkingDirectory = dir };
            foreach (var (k, v) in env) psi.Environment[k] = v;
        }
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        Process p;
        try { p = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"esharp: '{psi.FileName}' could not be started");
            return (1, "");
        }
        using (p)
        {
            var output = new StringBuilder();
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (output) output.Append(e.Data).Append('\n');
                if (echo) Console.Out.WriteLine(e.Data);
            };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            var finished = headless ? p.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)) : WaitForever(p);
            if (!finished)
            {
                if (container != null) Kill(container);
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                p.WaitForExit();
                Console.Error.WriteLine($"esharp: program did not finish within {timeout.TotalSeconds:0}s");
                lock (output) return (124, output.ToString());
            }
            p.WaitForExit();
            if (screenshot != null)
            {
                if (File.Exists(ppm))
                {
                    File.WriteAllBytes(screenshot, Png.Write(ReadPpm(File.ReadAllBytes(ppm))));
                    File.Delete(ppm);
                    Console.Error.WriteLine($"Saved screenshot {screenshot}");
                }
                else Console.Error.WriteLine("esharp: the program wrote no screenshot (did it open a screen?)");
            }
            lock (output) return (p.ExitCode, output.ToString());
        }
    }

    private static bool WaitForever(Process p) { p.WaitForExit(); return true; }

    private static void Kill(string container)
    {
        try
        {
            using var k = Process.Start(new ProcessStartInfo("docker", $"kill {container}") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            k?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// The --input script for the SDL layer: <c>click X,Y</c> (screen pixels of the program's screen), <c>rel DX,DY</c> and
    /// <c>press</c> pass through; <c>key N</c> and <c>key N+M</c> take X11 key codes as on the FS-UAE path (Esc is 9, left Shift 50)
    /// and become Amiga raw key codes. Throws <see cref="FormatException"/> for a step it does not know.
    /// </summary>
    public static string TranslateInput(string script)
    {
        var steps = new List<string>();
        foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(' ', 2, StringSplitOptions.TrimEntries);
            switch (parts[0])
            {
                case "click" or "rel" when parts.Length == 2:
                    var xy = parts[1].Split(',');
                    if (xy.Length != 2 || !int.TryParse(xy[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
                        || !int.TryParse(xy[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y))
                        throw new FormatException($"bad input step '{raw}'");
                    steps.Add($"{parts[0]} {x},{y}");
                    break;
                case "press" when parts.Length == 1:
                    steps.Add("press");
                    break;
                case "key" when parts.Length == 2:
                    var codes = parts[1].Split('+').Select(c =>
                    {
                        if (!uint.TryParse(c.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var k)) throw new FormatException($"bad key code in '{raw}'");
                        var amiga = AmigaRawKey(k);
                        if (amiga < 0) throw new FormatException($"X11 key code {k} has no Amiga raw key on the sdl target (in '{raw}')");
                        return amiga.ToString(CultureInfo.InvariantCulture);
                    });
                    steps.Add("key " + string.Join("+", codes));
                    break;
                default:
                    throw new FormatException($"bad input step '{raw}' (use 'click X,Y', 'rel DX,DY', 'press' or 'key N')");
            }
        }
        return string.Join(";", steps);
    }

    /// <summary>The Amiga raw key code at the position of an X11 (evdev) key code on a US keyboard, or -1.</summary>
    public static int AmigaRawKey(uint x11)
    {
        switch (x11)
        {
            case 9: return 0x45;                                    // Escape
            case >= 10 and <= 19: return (int)(x11 - 10 + 0x01);    // 1 .. 0
            case 20: return 0x0B;                                   // -
            case 21: return 0x0C;                                   // =
            case 22: return 0x41;                                   // Backspace
            case 23: return 0x42;                                   // Tab
            case >= 24 and <= 35: return (int)(x11 - 24 + 0x10);    // q .. ]
            case 36: return 0x44;                                   // Return
            case 37: case 105: return 0x63;                         // Control
            case >= 38 and <= 48: return (int)(x11 - 38 + 0x20);    // a .. '
            case 49: return 0x00;                                   // `
            case 50: return 0x60;                                   // left Shift
            case 51: return 0x0D;                                   // backslash
            case >= 52 and <= 61: return (int)(x11 - 52 + 0x31);    // z .. /
            case 62: return 0x61;                                   // right Shift
            case 64: return 0x64;                                   // left Alt
            case 65: return 0x40;                                   // Space
            case 66: return 0x62;                                   // Caps Lock
            case >= 67 and <= 76: return (int)(x11 - 67 + 0x50);    // F1 .. F10
            case 104: return 0x43;                                  // keypad Enter
            case 108: return 0x65;                                  // right Alt
            case 111: return 0x4C;                                  // Up
            case 113: return 0x4F;                                  // Left
            case 114: return 0x4E;                                  // Right
            case 116: return 0x4D;                                  // Down
            case 119: return 0x46;                                  // Delete
            case 133: return 0x66;                                  // left Amiga (Super)
            case 134: return 0x67;                                  // right Amiga
            default: return -1;
        }
    }

    /// <summary>Reads a binary PPM (P6, maxval 255), as the SDL layer writes its screenshot.</summary>
    public static RgbaImage ReadPpm(byte[] d)
    {
        var pos = 0;
        string Token()
        {
            while (pos < d.Length && (char.IsWhiteSpace((char)d[pos]) || d[pos] == '#'))
            {
                if (d[pos] == '#') while (pos < d.Length && d[pos] != '\n') pos++;
                else pos++;
            }
            var start = pos;
            while (pos < d.Length && !char.IsWhiteSpace((char)d[pos])) pos++;
            return Encoding.ASCII.GetString(d, start, pos - start);
        }
        if (Token() != "P6") throw new InvalidDataException("not a binary PPM");
        var w = int.Parse(Token(), CultureInfo.InvariantCulture);
        var h = int.Parse(Token(), CultureInfo.InvariantCulture);
        if (Token() != "255") throw new InvalidDataException("PPM maxval is not 255");
        pos++;   // the single whitespace after maxval
        if (d.Length - pos < w * h * 3) throw new InvalidDataException("PPM is truncated");
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            rgba[i * 4] = d[pos + i * 3];
            rgba[i * 4 + 1] = d[pos + i * 3 + 1];
            rgba[i * 4 + 2] = d[pos + i * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }
        return new RgbaImage(w, h, rgba);
    }
}
