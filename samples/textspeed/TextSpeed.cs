using ESharp;
using ESharp.Platform;

namespace TextSpeedSample;

/// <summary>
/// How long a screen of text takes: 30 lines of 30 characters with <see cref="Font.Draw"/> (one BltTemplate per line on the Amiga), against
/// the same text drawn the old way, one filled rectangle per run of glyph pixels. Prints the times (they are not checks: an emulator is not
/// always real time) and checks that both ways give the same pixels.
/// </summary>
public static class Program
{
    const string Line = "The quick brown fox jumps over";

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    /// <summary>The old way: one rectangle per run of set pixels in each glyph row.</summary>
    static void DrawWithRects(string text, int x, int y, int color)
    {
        for (int k = 0; k < text.Length; k++)
            for (int row = 0; row < Font.GlyphHeight; row++)
            {
                int col = 0;
                while (col < Font.GlyphWidth)
                {
                    if (!Font.Pixel(text[k], col, row)) { col++; continue; }
                    int start = col;
                    while (col < Font.GlyphWidth && Font.Pixel(text[k], col, row)) col++;
                    Video.FillRect(x + k * Font.Advance + start, y + row, col - start, 1, color);
                }
            }
    }

    [EntryPoint]
    public static void Main()
    {
        if (!Video.Open(320, 256, 2)) { Check(false, "a screen opens"); return; }
        Video.SetColor(0, 0, 0, 3);
        Video.SetColor(1, 15, 15, 15);
        Video.SetColor(2, 15, 12, 0);
        Clock.Open();

        Video.FillRect(0, 0, 320, 256, 0);
        long t0 = Clock.Ticks;
        for (int i = 0; i < 30; i++) Font.Draw(Line, 0, i * 8, 1, 1);
        long t1 = Clock.Ticks;
        int fast = Clock.Milliseconds(t0, t1);

        // The same text in a second colour on top, the old way: every pixel of colour 1 must turn into colour 2.
        long t2 = Clock.Ticks;
        for (int i = 0; i < 30; i++) DrawWithRects(Line, 0, i * 8, 2);
        long t3 = Clock.Ticks;
        int slow = Clock.Milliseconds(t2, t3);

        int left = 0, set = 0;
        for (int y = 0; y < 240; y++)
            for (int x = 0; x < 180; x++)
            {
                int p = Video.GetPixel(x, y);
                if (p == 1) left++;
                if (p == 2) set++;
            }
        Check(left == 0 && set > 0, "Font.Draw and one rectangle per run give the same pixels");

        Dos.PutStr($"30 lines of 30 characters: {fast} ms with Font.Draw, {slow} ms with one rectangle per run\n");
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Video.WaitFrames(250);
        Clock.Close();
        Video.Close();
    }
}
