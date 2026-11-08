#if HOST
using System.IO;
#endif

namespace ESharp.Platform;

/// <summary>AmigaDOS output. Binds dos.library on the Amiga; writes to a TextWriter on the host.</summary>
public static class Dos
{
#if AMIGA
    /// <summary>dos.library PutStr(), LVO -948.</summary>
    [AmigaLib("dos", -948), Since(36)]
    public static extern int PutStr(string str);

    /// <summary>dos.library Output(), LVO -60: the standard output file handle.</summary>
    [AmigaLib("dos", -60)]
    public static extern nuint Output();

    /// <summary>dos.library Flush(), LVO -360.</summary>
    [AmigaLib("dos", -360), Since(36)]
    public static extern int Flush(nuint fileHandle);

    /// <summary>Writes out buffered standard output now (output redirected to a file is otherwise written when the program ends).</summary>
    public static void FlushOutput() { Flush(Output()); }

    /// <summary>dos.library Delay(), LVO -198: waits for <paramref name="ticks"/> 50ths of a second.</summary>
    [AmigaLib("dos", -198)]
    public static extern void Delay(int ticks);
#else
    /// <summary>Where PutStr writes on the host. Tests replace it to capture output.</summary>
    public static TextWriter Out = System.Console.Out;

    public static int PutStr(string str)
    {
        Out.Write(str);
        return 0;
    }

    public static void FlushOutput() { Out.Flush(); }

    /// <summary>The host does not wait; the fake sound and clock move on by the time.</summary>
    public static void Delay(int ticks) { Sound.HostAdvance(ticks); Clock.HostAdvance((long)ticks * 20000); }
#endif

    /// <summary>Writes a decimal number.</summary>
    public static void PutInt(int value)
    {
        if (value < 0)
        {
            PutStr("-");
            if (value == -2147483648)
            {
                PutStr("2147483648");
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
            PutStr(Digit(digits[n]));
        }
    }

    private static string Digit(int d)
    {
        switch (d)
        {
            case 0: return "0";
            case 1: return "1";
            case 2: return "2";
            case 3: return "3";
            case 4: return "4";
            case 5: return "5";
            case 6: return "6";
            case 7: return "7";
            case 8: return "8";
            default: return "9";
        }
    }
}
