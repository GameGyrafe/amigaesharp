using System;
using ESharp;
using ESharp.Platform;

namespace FontAssetSample;

/// <summary>
/// A bitmap font from an asset (assets.json "fonts": slant.png, made by tools/AssetSamples --font, becomes Assets.Slant in Assets.g.cs)
/// drawn through the generated Assets.Slant class (it calls <c>BitmapFont</c>): the same calls on the Amiga (BltTemplate), on sdl and on the host. Every pixel on the screen is
/// checked against the glyph bits of the generated data, decoded here without the font class. Prints "ok NAME" or "FAIL NAME", then
/// "SHOT" and waits a few seconds so a screenshot can be taken.
/// </summary>
public static class Program
{
    const int Back = 1, Ink = 3;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    /// <summary>True when the glyph of c has pixel (x, y) set, read straight from the generated data.</summary>
    static bool Bit(char c, int x, int y)
    {
        int rowBytes = (Assets.Slant.CellWidth + 7) / 8;
        int at = ((c - Assets.Slant.First) * Assets.Slant.CellHeight + y) * rowBytes + (x >> 3);
        return (Assets.Slant.Glyphs[at] & (0x80 >> (x & 7))) != 0;
    }

    /// <summary>
    /// Pixels of <paramref name="text"/> (drawn in Ink on Back at (x, y), perLine characters per line) that differ from the glyph bits;
    /// only the part of the cells on the screen counts.
    /// </summary>
    static int Wrong(string text, int perLine, int x, int y, int scale)
    {
        int wrong = 0;
        for (int k = 0; k < text.Length; k++)
        {
            int left = x + (k % perLine) * Assets.Slant.CellWidth * scale;
            int top = y + (k / perLine) * Assets.Slant.CellHeight * scale;
            for (int row = 0; row < Assets.Slant.CellHeight * scale; row++)
                for (int col = 0; col < Assets.Slant.CellWidth * scale; col++)
                {
                    int got = Video.GetPixel(left + col, top + row);
                    if (got < 0) continue;
                    bool on = text[k] >= Assets.Slant.First && text[k] < Assets.Slant.First + Assets.Slant.Count && Bit(text[k], col / scale, row / scale);
                    if (got != (on ? Ink : Back)) wrong++;
                }
        }
        return wrong;
    }

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
        Check(Assets.Slant.CellWidth == 9 && Assets.Slant.CellHeight == 9 && Assets.Slant.First == 32 && Assets.Slant.Count == 95, "asset constants");
        Check(Assets.Slant.Glyphs.Length == 95 * 9 * 2, "glyph data size");
        Check(Assets.Slant.Has('A') && Assets.Slant.Has(' ') && Assets.Slant.Has('~') && !Assets.Slant.Has((char)31) && !Assets.Slant.Has((char)127), "Has");
        Check(Assets.Slant.Width("Hello", 1) == 45 && Assets.Slant.Width("Hello", 3) == 135 && Assets.Slant.Width("", 1) == 0, "Width");
        Check(Assets.Slant.Width("ab\ncdef\ng", 2) == 72 && Assets.Slant.Height("ab\ncdef\ng", 2) == 54 && Assets.Slant.Height("x", 1) == 9, "Width of the widest line and Height of the lines");
        Check(Assets.Slant.Pixel('I', 0, 8) == false && Assets.Slant.Pixel('I', -1, 0) == false && Assets.Slant.Pixel('I', 9, 0) == false, "Pixel outside the glyph");
        int inkInA = 0;
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
                if (Assets.Slant.Pixel('A', x, y)) inkInA++;
        Check(inkInA > 12 && !Assets.Slant.Pixel(' ', 4, 3) && !Assets.Slant.Pixel((char)200, 4, 3), "A has ink, space and a missing character have none");
        bool same = true;
        for (int c = 32; c < 127; c++)
            for (int y = 0; y < 9; y++)
                for (int x = 0; x < 9; x++)
                    if (Assets.Slant.Pixel((char)c, x, y) != Bit((char)c, x, y)) same = false;
        Check(same, "Pixel reads every glyph bit as the data has it");

        if (!Video.Open(640, 256, 4)) { Check(false, "a screen opens"); return; }
        Video.SetColor(0, 0, 0, 0);
        Video.SetColor(Back, 0, 0, 5);
        Video.SetColor(2, 15, 0, 0);
        Video.SetColor(Ink, 15, 15, 8);
        Video.FillRect(0, 0, 640, 256, Back);

        string all = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        Assets.Slant.Draw(all.Substring(0, 48) + "\n" + all.Substring(48), 8, 8, Ink, 1);
        Check(Wrong(all.Substring(0, 48), 48, 8, 8, 1) == 0 && Wrong(all.Substring(48), 48, 8, 8 + 9, 1) == 0, "every character matches the glyph bits pixel for pixel");
        Check(Count(0, 0, 640, 8, Ink) == 0 && Count(0, 26, 640, 230, Ink) == 0, "nothing is drawn outside the two lines");

        Assets.Slant.Draw("Slant\nfont", 8, 40, Ink, 2);
        Check(Wrong("Slant", 5, 8, 40, 2) == 0 && Wrong("font", 4, 8, 58, 2) == 0, "scale 2 and a newline match pixel for pixel");
        Assets.Slant.Draw("Big", 120, 40, Ink, 4);
        Check(Wrong("Big", 3, 120, 40, 4) == 0, "scale 4 (36 rows, past the BltTemplate strip) matches");

        Assets.Slant.DrawChar('Q', 8, 100, Ink, 1);
        Assets.Slant.DrawChar('Q', 20, 100, Ink, 3);
        Check(Wrong("Q", 1, 8, 100, 1) == 0 && Wrong("Q", 1, 20, 100, 3) == 0, "DrawChar");
        int w = Assets.Slant.DrawInt(-305, 60, 100, Ink, 1);
        Check(w == 36 && Wrong("-305", 4, 60, 100, 1) == 0, "DrawInt returns the width and draws the digits");

        Assets.Slant.Draw("AéB", 120, 100, Ink, 1);
        Check(Wrong("AéB", 3, 120, 100, 1) == 0 && Count(129, 100, 9, 9, Ink) == 0, "a character the font does not have is blank but takes its place");

        Assets.Slant.Draw("ABCD", -9, 130, Ink, 1);
        Check(Wrong("BCD", 3, 0, 130, 1) == 0 && Count(0, 130, 27, 9, Ink) > 20, "text that starts left of the screen is clipped");
        Assets.Slant.Draw("EF", 8, -4, Ink, 1);
        Check(Count(8, 0, 18, 5, Ink) > 3 && Count(0, 5, 640, 3, Ink) == 0, "text that starts above the screen is clipped");

        // 72 characters: wider than one BltTemplate strip (71 characters at 9 pixels), so the Amiga draws it in two calls.
        string line = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789ABCDEF";
        line = line.Substring(0, 72);
        Assets.Slant.Draw(line, 0, 140, 2, 1);
        int bad = 0;
        for (int k = 0; k < 71; k++)
            for (int row = 0; row < 9; row++)
                for (int col = 0; col < 9; col++)
                {
                    int px = k * 9 + col;
                    if (px >= 640) continue;
                    if (Video.GetPixel(px, 140 + row) != (Bit(line[k], col, row) ? 2 : Back)) bad++;
                }
        Check(bad == 0 && Video.GetPixel(639, 140) >= 0, "a line wider than one strip is drawn in pieces");

        // Colours: the same text in two colours at the same scale leaves the other colour alone.
        Assets.Slant.Draw("two", 8, 160, 2, 2);
        Assets.Slant.Draw("tone", 8, 180, Ink, 2);
        Check(Count(8, 160, 54, 18, Ink) == 0 && Count(8, 160, 54, 18, 2) > 20 && Count(8, 180, 72, 18, 2) == 0 && Count(8, 180, 72, 18, Ink) > 20, "colour and transparency");
        Assets.Slant.Draw("", 300, 160, Ink, 1);
        Assets.Slant.Draw("zero", 300, 160, Ink, 0);
        Check(Count(300, 160, 100, 20, Ink) == 0, "empty text and scale 0 draw nothing");

        Assets.Slant.Draw("Bitmap fonts from assets.json", 200, 200, Ink, 2);
        Assets.Slant.Draw("same code on the Amiga and on SDL", 200, 222, 2, 1);

        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Video.WaitFrames(150);
        Video.Close();
        Check(!Video.IsOpen(), "screen closes");
    }
}
