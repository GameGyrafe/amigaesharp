using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace ESharp.Cli;

/// <summary>
/// Sends scripted mouse and keyboard input to an X display through the XTEST extension (libX11 + libXtst),
/// so headless emulator runs can exercise the program's input handling.
/// Script: steps separated by ';': <c>click X,Y</c> (move to display pixels, then click), <c>rel DX,DY</c> (relative motion), <c>press</c> (click here), <c>down</c> / <c>up</c> (hold and release the button, for a drag), <c>key N</c> (X11 keycode, Esc is 9; <c>key 50+38</c> holds the first keys while the last is pressed: Shift+A).
/// </summary>
internal static class Xtest
{
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, ulong delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeRelativeMotionEvent(IntPtr display, int dx, int dy, ulong delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, int isPress, ulong delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, ulong delay);

    /// <summary>
    /// A relative move of (<paramref name="dx"/>, <paramref name="dy"/>) as steps of at most 4 pixels on the longer axis. Each step is
    /// where the line should be after it minus where the steps so far went, so the steps add up to exactly (dx, dy) and the shorter
    /// axis is spread over the move (integer division per step dropped it: 'rel 76,-320' did not move sideways at all).
    /// </summary>
    internal static List<(int Dx, int Dy)> RelSteps(int dx, int dy)
    {
        var steps = Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 4, 1);
        var result = new List<(int, int)>(steps);
        int doneX = 0, doneY = 0;
        for (var i = 1; i <= steps; i++)
        {
            var x = (int)((long)dx * i / steps);
            var y = (int)((long)dy * i / steps);
            result.Add((x - doneX, y - doneY));
            doneX = x;
            doneY = y;
        }
        return result;
    }

    public static bool Run(string? displayName, string script)
    {
        IntPtr display;
        try { display = XOpenDisplay(displayName); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
        if (display == IntPtr.Zero) return false;
        try
        {
            foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = raw.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (parts[0] == "click" && parts.Length == 2)
                {
                    var xy = parts[1].Split(',');
                    var x = int.Parse(xy[0], CultureInfo.InvariantCulture);
                    var y = int.Parse(xy[1], CultureInfo.InvariantCulture);
                    // Move in a few steps: the emulator derives button positions from motion events.
                    XTestFakeMotionEvent(display, -1, x - 3, y - 3, 0); XFlush(display); Thread.Sleep(150);
                    XTestFakeMotionEvent(display, -1, x, y, 0); XFlush(display); Thread.Sleep(250);
                    XTestFakeButtonEvent(display, 1, 1, 0); XFlush(display); Thread.Sleep(120);
                    XTestFakeButtonEvent(display, 1, 0, 0); XFlush(display); Thread.Sleep(500);
                }
                else if (parts[0] == "rel" && parts.Length == 2)
                {
                    // Relative motion in small steps: a grabbed emulator reads mouse deltas.
                    var xy = parts[1].Split(',');
                    var dx = int.Parse(xy[0], CultureInfo.InvariantCulture);
                    var dy = int.Parse(xy[1], CultureInfo.InvariantCulture);
                    foreach (var (sx, sy) in RelSteps(dx, dy))
                    {
                        XTestFakeRelativeMotionEvent(display, sx, sy, 0); XFlush(display); Thread.Sleep(15);
                    }
                    Thread.Sleep(300);
                }
                else if (parts[0] == "press")
                {
                    XTestFakeButtonEvent(display, 1, 1, 0); XFlush(display); Thread.Sleep(120);
                    XTestFakeButtonEvent(display, 1, 0, 0); XFlush(display); Thread.Sleep(500);
                }
                else if (parts[0] is "down" or "up")
                {
                    // Hold or release the button, for a drag: "down;rel 40,0;up" moves a slider knob.
                    XTestFakeButtonEvent(display, 1, parts[0] == "down" ? 1 : 0, 0); XFlush(display); Thread.Sleep(parts[0] == "down" ? 250 : 500);
                }
                else if (parts[0] == "key" && parts.Length == 2)
                {
                    // "key 38", or "key 50+38" to hold 50 (left Shift) while 38 is pressed.
                    var codes = Array.ConvertAll(parts[1].Split('+'), c => uint.Parse(c, CultureInfo.InvariantCulture));
                    foreach (var code in codes) { XTestFakeKeyEvent(display, code, 1, 0); XFlush(display); Thread.Sleep(120); }
                    for (var i = codes.Length - 1; i >= 0; i--) { XTestFakeKeyEvent(display, codes[i], 0, 0); XFlush(display); Thread.Sleep(i == 0 ? 500 : 120); }
                }
                else
                {
                    throw new FormatException($"bad input step '{raw}' (use 'click X,Y', 'rel DX,DY', 'press', 'down', 'up' or 'key N')");
                }
            }
            return true;
        }
        finally { XCloseDisplay(display); }
    }
}
