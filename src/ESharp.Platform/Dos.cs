#if HOST
using System.IO;
#endif

namespace ESharp.Platform;

/// <summary>AmigaDOS output. Binds dos.library on the Amiga; writes to a TextWriter on the host.</summary>
public static class Dos
{
#if AMIGA
    /// <summary>dos.library PutStr(), LVO -948.</summary>
    [AmigaLib("dos", -948)]
    public static extern int PutStr(string str);

    /// <summary>dos.library Output(), LVO -60: the standard output file handle.</summary>
    [AmigaLib("dos", -60)]
    public static extern uint Output();

    /// <summary>dos.library Flush(), LVO -360.</summary>
    [AmigaLib("dos", -360)]
    public static extern int Flush(uint fileHandle);

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

    /// <summary>The host does not wait.</summary>
    public static void Delay(int ticks) { }
#endif
}
