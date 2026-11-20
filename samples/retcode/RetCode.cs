using ESharp;
using ESharp.Platform;

namespace RetCodeSample;

/// <summary>
/// A test program whose return code differs between the host and the target on purpose, to see <c>esharp test</c> catch it:
/// with <c>--args TARGETRC=5</c> the target build's Main returns 5 while the host run returns 0, so the ok/FAIL lines agree and
/// the test still fails on the return code. Without the argument both return 0 and the test passes.
/// <c>esharp test samples/retcode --target m68k --docker &lt;image&gt; --args TARGETRC=5</c> (also os4, aros, aros-i386, sdl).
/// </summary>
public static class Program
{
    [EntryPoint]
    public static int Main(string[] args)
    {
        Dos.PutStr("ok main runs\n");
        int rc = 0;
#if !HOST
        rc = Args.Int("TARGETRC", 0);
#endif
        Dos.PutStr($"rc {rc}\n");
        return rc;
    }
}
