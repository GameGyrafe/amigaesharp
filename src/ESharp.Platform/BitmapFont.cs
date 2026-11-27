using System;

namespace ESharp.Platform;

/// <summary>
/// The drawing code behind the bitmap fonts that <c>esharp assets</c> makes from font images (the <c>"fonts"</c> list of
/// <c>assets.json</c>), the same on every target (m68k, os4, morphos, aros, sdl and the host): no diskfont.library and no files at
/// run time. Programs do not call it directly: the generated <c>Assets.g.cs</c> has a nested class per font (<c>Assets.Slant</c>) with
/// <c>Draw</c>, <c>DrawChar</c>, <c>DrawInt</c>, <c>Width</c>, <c>Height</c>, <c>Has</c> and <c>Pixel</c> that pass the font's glyph
/// data and size to these methods. Every glyph has the same cell (the gap between characters and lines is part of it), so a string
/// is <c>length * cellWidth * scale</c> pixels wide. Characters the font does not have (outside <c>first</c> .. <c>first + count - 1</c>)
/// draw as nothing but still take their place. The glyphs are drawn in one colour on a transparent background, like
/// <see cref="Font"/>; <c>scale</c> multiplies the size.
/// <para>
/// The data is one bit per pixel, <c>(CellWidth + 7) / 8</c> bytes per glyph row, the leftmost pixel in the most significant bit,
/// the glyphs one after the other from the first character. On the Amiga a line of text is one BltTemplate call (as for
/// <see cref="Font"/>; up to 640 pixels and 32 rows per call, longer lines take more calls, anything that does not fit, or would
/// start left of or above the screen, is drawn as one filled rectangle per run of pixels); the other targets fill the same
/// pixels directly.
/// </para>
/// </summary>
public static unsafe class BitmapFont
{
#if AMIGA
    // The strip that BltTemplate draws from: 640 pixels (80 bytes) by 32 rows, word aligned, in chip RAM for the blitter.
    private const int StripBytesPerRow = 80, StripRows = 32;
    [ChipRam] private static ushort[] Strip = new ushort[StripBytesPerRow / 2 * StripRows];
#endif

    /// <summary>True when the font (first character <paramref name="first"/>, <paramref name="count"/> glyphs) has a glyph for <paramref name="c"/>.</summary>
    public static bool Has(int first, int count, char c)
    {
        int code = c;
        return code >= first && code < first + count;
    }

    /// <summary>True when pixel (<paramref name="x"/>, <paramref name="y"/>) of the glyph of <paramref name="c"/> is set (false outside the cell or for a character the font does not have).</summary>
    public static bool Pixel(ReadOnlySpan<byte> glyphs, int cellWidth, int cellHeight, int first, int count, char c, int x, int y)
    {
        if (!Has(first, count, c) || x < 0 || x >= cellWidth || y < 0 || y >= cellHeight) return false;
        int rowBytes = (cellWidth + 7) / 8;
        int at = ((c - first) * cellHeight + y) * rowBytes + (x >> 3);
        return (glyphs[at] & (0x80 >> (x & 7))) != 0;
    }

    /// <summary>Width in pixels of the widest line of <paramref name="text"/> at <paramref name="scale"/> ('\n' starts a new line).</summary>
    public static int Width(int cellWidth, string text, int scale)
    {
        int best = 0;
        int run = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') run = 0;
            else run++;
            if (run > best) best = run;
        }
        return best * cellWidth * scale;
    }

    /// <summary>Height in pixels of <paramref name="text"/> at <paramref name="scale"/>: one cell height per line.</summary>
    public static int Height(int cellHeight, string text, int scale)
    {
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') lines++;
        return lines * cellHeight * scale;
    }

    // One filled rectangle per run of set pixels in a glyph row.
    private static void DrawCharRects(ReadOnlySpan<byte> glyphs, int cellWidth, int cellHeight, int first, int count, char c, int x, int y, int color, int scale)
    {
        if (!Has(first, count, c)) return;
        int rowBytes = (cellWidth + 7) / 8;
        int glyph = (c - first) * cellHeight * rowBytes;
        for (int row = 0; row < cellHeight; row++)
        {
            int col = 0;
            while (col < cellWidth)
            {
                if ((glyphs[glyph + row * rowBytes + (col >> 3)] & (0x80 >> (col & 7))) == 0) { col++; continue; }
                int start = col;
                while (col < cellWidth && (glyphs[glyph + row * rowBytes + (col >> 3)] & (0x80 >> (col & 7))) != 0) col++;
                Video.FillRect(x + start * scale, y + row * scale, (col - start) * scale, scale, color);
            }
        }
    }

    /// <summary>Draws one character with its top-left corner at (x, y).</summary>
    public static void DrawChar(ReadOnlySpan<byte> glyphs, int cellWidth, int cellHeight, int first, int count, char c, int x, int y, int color, int scale)
    {
        if (!Has(first, count, c) || scale < 1) return;
#if AMIGA
        byte[] one = new byte[1];
        one[0] = (byte)c;
        if (DrawCodes(glyphs, cellWidth, cellHeight, first, count, one, 1, x, y, color, scale)) return;
#endif
        DrawCharRects(glyphs, cellWidth, cellHeight, first, count, c, x, y, color, scale);
    }

    /// <summary>Draws <paramref name="text"/> with its top-left corner at (x, y); '\n' starts a new line.</summary>
    public static void Draw(ReadOnlySpan<byte> glyphs, int cellWidth, int cellHeight, int first, int count, string text, int x, int y, int color, int scale)
    {
        if (scale < 1) return;
        int cx = x;
        int cy = y;
#if AMIGA
        // Each line goes in pieces that fit the strip, one BltTemplate each (a cell wider than the strip is drawn with rectangles).
        int perCall = StripBytesPerRow * 8 / (cellWidth * scale);
        if (perCall > 100) perCall = 100;
        byte[] codes = new byte[100];
        int n = 0;
        int start = cx;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length || text[i] == '\n';
            if (!end) { codes[n] = (byte)text[i]; n++; cx += cellWidth * scale; }
            if (end || n >= perCall || n >= 100)
            {
                if (n > 0 && !(perCall > 0 && DrawCodes(glyphs, cellWidth, cellHeight, first, count, codes, n, start, cy, color, scale)))
                    for (int k = 0; k < n; k++) DrawCharRects(glyphs, cellWidth, cellHeight, first, count, (char)codes[k], start + k * cellWidth * scale, cy, color, scale);
                n = 0;
                start = cx;
            }
            if (end) { cx = x; start = x; cy += cellHeight * scale; }
        }
#else
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n') { cx = x; cy += cellHeight * scale; continue; }
            DrawCharRects(glyphs, cellWidth, cellHeight, first, count, c, cx, cy, color, scale);
            cx += cellWidth * scale;
        }
#endif
    }

    /// <summary>Draws a decimal number and returns its width in pixels.</summary>
    public static int DrawInt(ReadOnlySpan<byte> glyphs, int cellWidth, int cellHeight, int first, int count, int value, int x, int y, int color, int scale)
    {
        int[] digits = new int[11];
        int n = 0;
        bool negative = value < 0;
        long v = value;
        if (negative) v = -v;
        do
        {
            digits[n] = (int)(v % 10);
            n++;
            v /= 10;
        }
        while (v > 0);
        int cx = x;
        if (negative) { DrawChar(glyphs, cellWidth, cellHeight, first, count, '-', cx, y, color, scale); cx += cellWidth * scale; }
        while (n > 0)
        {
            n--;
            DrawChar(glyphs, cellWidth, cellHeight, first, count, (char)('0' + digits[n]), cx, y, color, scale);
            cx += cellWidth * scale;
        }
        return cx - x;
    }

#if AMIGA
    /// <summary>
    /// Draws <paramref name="n"/> character codes in one BltTemplate: false (nothing drawn) when they do not fit the strip
    /// or would start left of or above the screen, so the caller draws them with rectangles instead.
    /// </summary>
    private static bool DrawCodes(ReadOnlySpan<byte> glyphs, int cellWidth, int cellHeight, int first, int count, ReadOnlySpan<byte> codes, int n, int x, int y, int color, int scale)
    {
        if (n <= 0) return true;
        int width = n * cellWidth * scale;
        int rows = cellHeight * scale;
        if (rows > StripRows || width > StripBytesPerRow * 8 || x < 0 || y < 0) return false;
        int rowBytes = (cellWidth + 7) / 8;
        int bytesPerRow = (width + 15) / 16 * 2;
        fixed (ushort* words = Strip)
        {
            byte* strip = (byte*)words;
            for (int i = 0; i < bytesPerRow * rows; i++) strip[i] = 0;
            for (int k = 0; k < n; k++)
            {
                char c = (char)codes[k];
                if (!Has(first, count, c)) continue;
                int glyph = (c - first) * cellHeight * rowBytes;
                int left = k * cellWidth * scale;
                for (int row = 0; row < cellHeight; row++)
                    for (int col = 0; col < cellWidth; col++)
                    {
                        if ((glyphs[glyph + row * rowBytes + (col >> 3)] & (0x80 >> (col & 7))) == 0) continue;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            byte* line = strip + (row * scale + sy) * bytesPerRow;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = left + col * scale + sx;
                                line[px >> 3] |= (byte)(0x80 >> (px & 7));
                            }
                        }
                    }
            }
            Video.Template(strip, bytesPerRow, width, rows, x, y, color);
        }
        return true;
    }
#endif
}
