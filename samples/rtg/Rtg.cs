using ESharp;
using ESharp.Platform;

namespace RtgSample;

/// <summary>
/// Retargetable graphics (RTG): finds out whether a graphics card driver is installed, opens the screen on it when it is
/// and on the native chipset when it is not. Prints "ok NAME" or "FAIL NAME". On a machine without RTG (the emulator's
/// A500+ and A1200) it checks that the program still starts and that the native screen is not reported as RTG.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static void Main()
    {
        bool rtg = Video.HasRtg(640, 480);
        Dos.PutStr(rtg ? "RTG present\n" : "no RTG\n");
        if (rtg)
        {
            Check(Video.OpenRtg(640, 480, 8), "an RTG screen opens");
            Check(Video.IsRtg(), "the screen is reported as RTG");
            Video.SetColor24(200, 12, 200, 99);
            Video.FillRect(10, 10, 20, 20, 200);
            Check(Video.GetPixel(15, 15) == 200, "pen 200 draws on the card");
            Video.Close();
            Check(!Video.IsRtg(), "closed screen is not RTG");
        }
        else
        {
            Check(!Video.OpenRtg(640, 480, 8), "an RTG screen is refused without a card");
            Check(!Video.IsRtg(), "no screen is not RTG");
        }

        Check(Video.Open(320, 200, 4), "the native screen opens");
        Check(!Video.IsRtg(), "the native screen is not RTG");
        Video.SetColor(1, 15, 0, 0);
        Video.FillRect(0, 0, 8, 8, 1);
        Check(Video.GetPixel(4, 4) == 1, "native drawing works");
        Video.Close();
    }
}
