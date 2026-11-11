using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ESharp.Cli;

/// <summary>
/// Scripted input for os4 runs through the QEMU monitor (<c>mouse_move</c>, <c>mouse_button</c>, <c>sendkey</c>). The syntax is
/// the one of the other targets: <c>click X,Y</c>, <c>rel DX,DY</c>, <c>press</c>, <c>key N</c>, steps separated by ';'. Two
/// differences: the QEMU mouse is relative, so <c>click X,Y</c> first drives the pointer into the top-left corner (AmigaOS
/// clamps it to the screen) and then moves it right and down by X,Y; and N is an Amiga raw key code (Esc is 69, A is 32,
/// Return is 68; <c>key 96+32</c> holds Left Shift while A is pressed), which is mapped to a QEMU key name.
/// </summary>
public static class Os4Input
{
    /// <summary>One monitor command and the pause after it.</summary>
    public readonly record struct Step(string Command, int DelayMs);

    /// <summary>Pointer units sent per monitor command.</summary>
    public const int Chunk = 24;

    /// <summary>Pointer movement per unit asked for, found by experiment (AmigaOS pointer acceleration is part of it).</summary>
    public static double Scale = 1.0;

    /// <summary>Amiga raw key code to QEMU key name; null for a code that has no key here.</summary>
    public static string? KeyName(int raw)
    {
        if (raw >= 0x01 && raw <= 0x09) return ((char)('0' + raw)).ToString();
        if (raw == 0x0A) return "0";
        const string row1 = "qwertyuiop", row2 = "asdfghjkl", row3 = "zxcvbnm";
        if (raw >= 0x10 && raw <= 0x19) return row1[raw - 0x10].ToString();
        if (raw >= 0x20 && raw <= 0x28) return row2[raw - 0x20].ToString();
        if (raw >= 0x31 && raw <= 0x37) return row3[raw - 0x31].ToString();
        if (raw >= 0x50 && raw <= 0x59) return "f" + (raw - 0x50 + 1);
        return raw switch
        {
            0x00 => "grave_accent", 0x0B => "minus", 0x0C => "equal", 0x0D => "backslash",
            0x1A => "bracket_left", 0x1B => "bracket_right", 0x29 => "semicolon", 0x2A => "apostrophe", 0x2B => "backslash",
            0x30 => "less", 0x38 => "comma", 0x39 => "dot", 0x3A => "slash",
            0x0F => "kp_0", 0x1D => "kp_1", 0x1E => "kp_2", 0x1F => "kp_3", 0x2D => "kp_4", 0x2E => "kp_5", 0x2F => "kp_6",
            0x3D => "kp_7", 0x3E => "kp_8", 0x3F => "kp_9", 0x3C => "kp_decimal", 0x43 => "kp_enter",
            0x40 => "spc", 0x41 => "backspace", 0x42 => "tab", 0x44 => "ret", 0x45 => "esc", 0x46 => "delete",
            0x4C => "up", 0x4D => "down", 0x4E => "right", 0x4F => "left",
            0x60 => "shift", 0x61 => "shift_r", 0x62 => "caps_lock", 0x63 => "ctrl", 0x64 => "alt", 0x65 => "alt_r",
            0x66 => "meta_l", 0x67 => "meta_r",
            _ => null,
        };
    }

    private static void Move(List<Step> steps, int dx, int dy)
    {
        var n = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / (double)Chunk));
        int sx = 0, sy = 0;
        for (var i = 1; i <= n; i++)
        {
            int tx = dx * i / n, ty = dy * i / n;
            steps.Add(new Step($"mouse_move {tx - sx} {ty - sy}", 20));
            sx = tx; sy = ty;
        }
        steps[^1] = steps[^1] with { DelayMs = 300 };
    }

    private static void Press(List<Step> steps)
    {
        steps.Add(new Step("mouse_button 1", 120));
        steps.Add(new Step("mouse_button 0", 500));
    }

    /// <summary>Turns a script into monitor commands. Throws FormatException for a step it does not know.</summary>
    public static List<Step> Translate(string script)
    {
        var steps = new List<Step>();
        foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(' ', 2, StringSplitOptions.TrimEntries);
            try
            {
                if (parts[0] == "click" && parts.Length == 2)
                {
                    var (x, y) = Pair(parts[1]);
                    Move(steps, -4000, -4000);                       // the corner: the pointer stops at the screen edge
                    Move(steps, (int)Math.Round(x * Scale), (int)Math.Round(y * Scale));
                    Press(steps);
                }
                else if (parts[0] == "rel" && parts.Length == 2)
                {
                    var (dx, dy) = Pair(parts[1]);
                    Move(steps, (int)Math.Round(dx * Scale), (int)Math.Round(dy * Scale));
                }
                else if (parts[0] == "press") Press(steps);
                else if (parts[0] == "key" && parts.Length == 2)
                {
                    var names = parts[1].Split('+').Select(c =>
                        KeyName(int.Parse(c, CultureInfo.InvariantCulture)) ?? throw new FormatException($"no QEMU key for Amiga raw code {c}")).ToArray();
                    steps.Add(new Step("sendkey " + string.Join("-", names), 500));
                }
                else throw new FormatException("unknown step");
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException)
            {
                throw new FormatException($"bad input step '{raw}' (use 'click X,Y', 'rel DX,DY', 'press' or 'key N'; N is an Amiga raw key code for os4): {e.Message}");
            }
        }
        return steps;
    }

    private static (int, int) Pair(string s)
    {
        var xy = s.Split(',');
        return (int.Parse(xy[0], CultureInfo.InvariantCulture), int.Parse(xy[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Sends the steps over one monitor connection.</summary>
    public static bool Send(string socketPath, IEnumerable<Step> steps)
    {
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(socketPath));
            s.ReceiveTimeout = 50;
            var buf = new byte[4096];
            void Drain() { try { while (s.Available > 0 && s.Receive(buf) > 0) { } } catch (SocketException) { } }
            Thread.Sleep(100);
            Drain();
            foreach (var st in steps)
            {
                s.Send(Encoding.ASCII.GetBytes(st.Command + "\n"));
                Thread.Sleep(st.DelayMs);
                Drain();
            }
            return true;
        }
        catch (Exception e) when (e is SocketException or System.IO.IOException) { return false; }
    }
}
