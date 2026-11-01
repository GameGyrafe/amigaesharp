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
#else
    /// <summary>Where PutStr writes on the host. Tests replace it to capture output.</summary>
    public static TextWriter Out = System.Console.Out;

    public static int PutStr(string str)
    {
        Out.Write(str);
        return 0;
    }
#endif
}
