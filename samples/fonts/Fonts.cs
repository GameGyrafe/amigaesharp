using ESharp;
using ESharp.Platform;

namespace FontsSample;

/// <summary>
/// Fonts by name and size (<see cref="Fonts"/>): topaz 8 is in ROM and always opens; other fonts come from the FONTS:
/// drawer through diskfont.library, which is on the Workbench disk and may be missing, so the sample says what it
/// found instead of failing. Prints "ok NAME" or "FAIL NAME", then draws a line of text in each font it has.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    /// <summary>Number of pixels in the given colour inside a rectangle.</summary>
    static int Count(int x, int y, int w, int h, int color)
    {
        int n = 0;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                if (Video.GetPixel(x + i, y + j) == color) n++;
        return n;
    }

    [EntryPoint]
    public static void Main()
    {
        FontHandle topaz = Fonts.Open("topaz", 8);
        Check(topaz.IsOpen, "topaz 8 opens");
        Dos.PutStr($"topaz 8: height {topaz.Height}, width {topaz.Width}, baseline {topaz.Baseline}\n");
        Check(topaz.Height == 8 && topaz.Width == 8, "topaz 8 is 8x8");
        FontHandle none = Fonts.Open("nosuchfont", 8);
        Check(!none.IsOpen, "a font that does not exist does not open");

        bool disk = Fonts.DiskFontsAvailable;
        Dos.PutStr(disk ? "diskfont.library opens\n" : "diskfont.library is not installed: disk fonts skipped\n");
        FontHandle ruby = new FontHandle();
        FontHandle second = new FontHandle();
        if (disk)
        {
            ruby = Fonts.Open("ruby", 12);
            if (ruby.IsOpen)
            {
                Dos.PutStr($"ruby 12: height {ruby.Height}, width {ruby.Width}, baseline {ruby.Baseline}\n");
                Check(ruby.Height > 8, "the disk font is taller than topaz 8");
            }
            else Dos.PutStr("no ruby font in FONTS: (not a failure)\n");
            second = Fonts.Open("topaz", 8);
            Check(second.IsOpen, "the same font opens twice");
        }

        if (!Video.Open(320, 256, 2)) { Check(false, "a screen opens"); return; }
        Video.SetColor(0, 0, 0, 0);
        Video.SetColor(1, 15, 15, 15);
        Fonts.Draw(topaz, "Hello from topaz 8", 8, 24, 1);
        int w = Fonts.TextWidth(topaz, "Hello from topaz 8");
        Dos.PutStr($"text width {w}\n");
        Check(w >= 18 * 6 && w <= 18 * 9, "the text width is about 18 characters");
        Check(Count(8, 24, 150, 8, 1) > 40, "text was drawn in topaz");
        Check(Count(8, 36, 150, 8, 1) == 0, "nothing was drawn below it");
        if (ruby.IsOpen)
        {
            Fonts.Draw(ruby, "Disk font ruby", 8, 48, 1);
            Check(Count(8, 48, 150, ruby.Height, 1) > 40, "text was drawn in the disk font");
        }
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Video.WaitFrames(200);                                  // about 4 seconds, long enough for a screenshot
        Video.Close();

        Fonts.Close(ref topaz);
        Fonts.Close(ref second);
        Fonts.Close(ref ruby);
        Fonts.Close(ref ruby);                                    // closing twice is harmless
        Check(!topaz.IsOpen, "closed fonts report closed");
        Fonts.Close();
    }
}
