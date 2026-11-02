using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace ESharp.Cli;

/// <summary>
/// Sends scripted mouse and keyboard input to an X display through the XTEST extension (libX11 + libXtst),
/// so headless emulator runs can exercise the program's input handling.
/// Script: steps separated by ';': <c>click X,Y</c> (move to display pixels, then click), <c>rel DX,DY</c> (relative motion), <c>press</c> (click here), <c>key N</c> (X11 keycode, Esc is 9).
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
                    var steps = Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 4, 1);
                    for (var i = 0; i < steps; i++)
                    {
                        XTestFakeRelativeMotionEvent(display, dx / steps, dy / steps, 0); XFlush(display); Thread.Sleep(15);
                    }
                    Thread.Sleep(300);
                }
                else if (parts[0] == "press")
                {
                    XTestFakeButtonEvent(display, 1, 1, 0); XFlush(display); Thread.Sleep(120);
                    XTestFakeButtonEvent(display, 1, 0, 0); XFlush(display); Thread.Sleep(500);
                }
                else if (parts[0] == "key" && parts.Length == 2)
                {
                    var code = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                    XTestFakeKeyEvent(display, code, 1, 0); XFlush(display); Thread.Sleep(120);
                    XTestFakeKeyEvent(display, code, 0, 0); XFlush(display); Thread.Sleep(500);
                }
                else
                {
                    throw new FormatException($"bad input step '{raw}' (use 'click X,Y' or 'key N')");
                }
            }
            return true;
        }
        finally { XCloseDisplay(display); }
    }
}
