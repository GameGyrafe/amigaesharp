using System.Diagnostics;

namespace ESharp.Platform;

/// <summary>
/// Debug tracing to the serial port, for the cases where a screen or a file is not available (a crash, a program that owns
/// the display). On the Amiga the text goes out through exec's RawPutChar, which writes straight to the serial hardware
/// (Kickstart 3.1 has it, no debug.lib needed; AROS routes it to its debug output, which is the host terminal when hosted).
/// On SDL and on the host it goes to standard error.
/// <para>
/// The calls exist only when the program is built with <c>esharp build --trace</c> or <c>--debug</c> (which defines
/// <c>TRACE</c>): otherwise the compiler drops each call statement, arguments included, so a release build pays nothing.
/// Under plain dotnet <c>TRACE</c> is defined by default, so the host test run traces.
/// </para>
/// </summary>
public static class Trace
{
#if AMIGA
    private static bool _ready;

    private static void Put(int c)
    {
        if (!_ready)
        {
            Intrinsics.es_rawioinit();
            _ready = true;
        }
        Intrinsics.es_rawputchar(c);
    }

    private static void PutText(string s)
    {
        for (int i = 0; i < s.Length; i++) Put(s[i]);
    }

    private static void PutNumber(int value)
    {
        if (value < 0)
        {
            Put('-');
            if (value == -2147483648)
            {
                PutText("2147483648");
                return;
            }
            value = -value;
        }
        int[] digits = new int[11];
        int n = 0;
        do
        {
            digits[n] = value % 10;
            n++;
            value /= 10;
        }
        while (value > 0);
        while (n > 0)
        {
            n--;
            Put('0' + digits[n]);
        }
    }

    /// <summary>Writes the text to the serial port.</summary>
    [Conditional("TRACE")]
    public static void Write(string text) { PutText(text); }

    /// <summary>Writes the text and a line feed.</summary>
    [Conditional("TRACE")]
    public static void Line(string text) { PutText(text); Put('\n'); }

    /// <summary>Writes <c>label=value</c> and a line feed.</summary>
    [Conditional("TRACE")]
    public static void Value(string label, int value)
    {
        PutText(label);
        Put('=');
        PutNumber(value);
        Put('\n');
    }

    /// <summary>
    /// Writes <c>label: </c> and where this call is in the program, in the form the target's crash reports use and
    /// <c>esharp addr</c> takes: <c>hunk 0 offset 0x1a2c</c> (m68k), <c>segment 1 .text offset 0x698</c> (AROS),
    /// <c>_start+0x9cc</c> (AmigaOS 4). Use it to see how far a program got, or to find a line's code offset.
    /// </summary>
    [Conditional("TRACE")]
    [Native("es_trace_where")]
    public static extern void Where(string label);
#elif SDL
    /// <summary>Writes the text to standard error.</summary>
    [Conditional("TRACE")]
    public static void Write(string text) { Crt.es_std_trace(text); }

    /// <summary>Writes the text and a line feed.</summary>
    [Conditional("TRACE")]
    public static void Line(string text) { Crt.es_std_trace(text); Crt.es_std_trace_char('\n'); }

    /// <summary>Writes <c>label=value</c> and a line feed.</summary>
    [Conditional("TRACE")]
    public static void Value(string label, int value)
    {
        Crt.es_std_trace(label);
        Crt.es_std_trace_char('=');
        Crt.es_std_trace_int(value);
        Crt.es_std_trace_char('\n');
    }

    /// <summary>Writes <c>label: </c> and a note: a desktop build has no code offset to give (the Amiga targets do).</summary>
    [Conditional("TRACE")]
    public static void Where(string label) { Crt.es_std_trace(label); Crt.es_std_trace(": (no code offset on this target)\n"); }
#else
    private static readonly System.Threading.AsyncLocal<System.IO.TextWriter?> _out = new();
    private static readonly System.IO.TextWriter _error = System.Console.Error;

    /// <summary>Where the host writes trace text. Tests replace it to capture the output. The value is per execution flow, so tests that run in parallel do not see each other's output.</summary>
    public static System.IO.TextWriter Out
    {
        get => _out.Value ?? _error;
        set => _out.Value = value;
    }

    /// <summary>Writes the text to standard error.</summary>
    [Conditional("TRACE")]
    public static void Write(string text) { Out.Write(text); }

    /// <summary>Writes the text and a line feed.</summary>
    [Conditional("TRACE")]
    public static void Line(string text) { Out.Write(text + "\n"); }

    /// <summary>Writes <c>label=value</c> and a line feed.</summary>
    [Conditional("TRACE")]
    public static void Value(string label, int value) { Out.Write(label + "=" + value + "\n"); }

    /// <summary>Writes <c>label: </c> and a note: the host has no code offset to give (the Amiga targets do).</summary>
    [Conditional("TRACE")]
    public static void Where(string label) { Out.Write(label + ": (no code offset on the host)\n"); }
#endif
}
