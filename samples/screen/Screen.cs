using System;
using ESharp;
using ESharp.Platform;

namespace ScreenSample;

/// <summary>
/// Phase 3: open a screen, draw rectangles with the blitter, show a PNG converted by `esharp assets`
/// (assets.json, card.png -> Assets.g.cs) and read the pixels back. Prints "ok NAME" or "FAIL NAME",
/// then "SHOT" and waits a few seconds so a screenshot can be taken.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    /// <summary>The palette index of a pixel of the card, decoded from the planar data in <see cref="Assets.CardPlanes"/>.</summary>
    static int CardIndex(int x, int y)
    {
        int bytesPerRow = (Assets.CardWidth + 15) / 16 * 2;
        int value = 0;
        for (int plane = 0; plane < Assets.CardDepth; plane++)
        {
            int offset = plane * bytesPerRow * Assets.CardHeight + y * bytesPerRow + (x >> 3);
            if ((Assets.CardPlanes[offset] & (0x80 >> (x & 7))) != 0) value |= 1 << plane;
        }
        return value;
    }

    [EntryPoint]
    public static void Main()
    {
        bool opened = Video.Open(320, 200, 4);
        Check(opened, "screen opens");
        if (!opened) return;

        // Entries 0..7 belong to the card, 8..11 to the rectangles.
        Video.SetPalette(Assets.CardPalette);
        Video.SetColor(8, 15, 0, 0);
        Video.SetColor(9, 0, 15, 0);
        Video.SetColor(10, 15, 15, 0);
        Video.SetColor(11, 0, 0, 4);
        Video.FillRect(0, 0, 320, 200, 11);
        Video.FillRect(20, 20, 100, 60, 8);
        Video.FillRect(60, 50, 100, 60, 9);
        Video.FillRect(200, 120, 80, 40, 10);

        Check(Video.GetPixel(2, 2) == 11, "background pixel");
        Check(Video.GetPixel(30, 30) == 8, "red rectangle");
        Check(Video.GetPixel(70, 60) == 9 && Video.GetPixel(100, 70) == 9, "green rectangle on top of red");
        Check(Video.GetPixel(210, 130) == 10, "yellow rectangle");
        Check(Video.GetPixel(119, 25) == 8 && Video.GetPixel(120, 25) == 11, "rectangle edges are exact");
        Check(Video.GetPixel(-1, 0) == -1 && Video.GetPixel(0, 200) == -1, "outside the screen");

        Check(Assets.CardWidth == 64 && Assets.CardHeight == 88 && Assets.CardPalette.Length == 1 << Assets.CardDepth, "asset constants");

        int cx = 220;
        int cy = 20;
        Video.Blit(Assets.CardPlanes, Assets.CardWidth, Assets.CardHeight, Assets.CardDepth, cx, cy);
        int wrong = 0;
        for (int y = 0; y < Assets.CardHeight; y++)
            for (int x = 0; x < Assets.CardWidth; x++)
                if (Video.GetPixel(cx + x, cy + y) != CardIndex(x, y)) wrong++;
        Check(wrong == 0, "blitted card matches the planar data pixel for pixel");
        Check(Video.GetPixel(cx + 32, cy + 44) == CardIndex(32, 44) && CardIndex(32, 44) != 0, "card centre is not background");
        Check(Video.GetPixel(cx - 1, cy) == 11 && Video.GetPixel(cx + 64, cy) == 11, "blit does not spill outside the card");

        // Text: 'A' has rows .###. / #...# / #...# / ##### ...
        Font.DrawChar('A', 10, 170, 10, 1);
        Check(Video.GetPixel(10, 170) == 11 && Video.GetPixel(11, 170) == 10 && Video.GetPixel(13, 170) == 10 && Video.GetPixel(14, 170) == 11, "font: top row of A");
        Check(Video.GetPixel(10, 173) == 10 && Video.GetPixel(14, 173) == 10 && Video.GetPixel(12, 171) == 11, "font: crossbar of A");
        Font.DrawChar('A', 30, 170, 10, 2);
        Check(Video.GetPixel(32, 170) == 10 && Video.GetPixel(33, 171) == 10 && Video.GetPixel(30, 170) == 11 && Video.GetPixel(38, 176) == 10, "font: scale 2 doubles pixels");
        Font.Draw("Hi\nA", 60, 170, 9, 1);
        Check(Video.GetPixel(60, 170) == 9 && Video.GetPixel(61, 179) == 9 && Font.Width("Hi", 1) == 11, "font: strings, newline and width");
        int w = Font.DrawInt(-305, 100, 170, 9, 1);
        Check(w == 23 && Video.GetPixel(100, 173) == 9, "font: numbers");

        // Every printable character, two lines (one Draw call each on the Amiga), against the glyph table pixel for pixel.
        const string all = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        Font.Draw(all.Substring(0, 33) + "\n" + all.Substring(33, 33) + "\n" + all.Substring(66), 0, 112, 9, 1);
        Check(GlyphsWrong(all, 33, 0, 112, 1) == 0, "font: all characters match the glyphs pixel for pixel");
        Font.Draw("Az09?", 0, 150, 9, 2);
        Check(GlyphsWrong("Az09?", 5, 0, 150, 2) == 0, "font: scale 2 matches pixel for pixel");

        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Video.WaitFrames(150);
        Video.Close();
        Check(!Video.IsOpen(), "screen closes");

        // A screen 640 wide is hires on the chipset (not a lores screen twice as wide as the display).
        Check(Video.Open(640, 256, 4), "a 640 x 256 screen opens");
        Check(Video.IsHires(), "it is hires");
        Video.SetColor(0, 0, 0, 0);
        Video.SetColor(3, 15, 15, 15);
        Video.FillRect(0, 0, 640, 256, 0);
        Video.FillRect(600, 240, 20, 10, 3);
        Font.Draw("right edge", 560, 20, 3, 1);
        Check(Video.GetPixel(610, 245) == 3 && Video.GetPixel(599, 245) == 0 && Video.GetPixel(639, 255) == 0, "hires pixels");
        Check(Video.GetPixel(560, 20) == (Font.Pixel('r', 0, 0) ? 3 : 0) && Video.GetPixel(560, 22) == 3 && Font.Pixel('r', 0, 2), "text near the right edge");
        Video.Close();
    }

    /// <summary>Pixels of <paramref name="text"/> drawn in colour 9 on colour 11 at (x, y) that differ from the glyphs (perLine characters per line).</summary>
    static int GlyphsWrong(string text, int perLine, int x, int y, int scale)
    {
        int wrong = 0;
        for (int k = 0; k < text.Length; k++)
        {
            int left = x + (k % perLine) * Font.Advance * scale;
            int top = y + (k / perLine) * Font.LineHeight * scale;
            for (int row = 0; row < Font.GlyphHeight * scale; row++)
                for (int col = 0; col < Font.Advance * scale; col++)
                {
                    int want = Font.Pixel(text[k], col / scale, row / scale) ? 9 : 11;
                    if (Video.GetPixel(left + col, top + row) != want) wrong++;
                }
        }
        return wrong;
    }
}
