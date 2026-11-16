using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ESharp.Assets;

/// <summary>
/// The pictures for a program's icon: E#'s own default (drawn here, no file involved) or a project's PNG converted to the two icon
/// formats. The classic image can only use the four standard Workbench colours (grey, black, white, blue) because older systems show
/// it with the screen's first four pens; the colour image keeps up to 64 colours.
/// </summary>
public static class IconArt
{
    /// <summary>The four classic Workbench pens, as RGB: grey (the background), black, white, blue.</summary>
    public static readonly uint[] ClassicPalette = { 0xAAAAAA, 0x000000, 0xFFFFFF, 0x6688BB };

    // 5 x 7 letters, one string per row, '#' set.
    private static readonly string[] GlyphE = { "#####", "#....", "#....", "####.", "#....", "#....", "#####" };
    private static readonly string[] GlyphHash = { ".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#." };

    private const int W = 48, H = 32;

    /// <summary>The default E# icon, 48 x 32: a blue plate with a light top edge and a dark bottom edge, and a white "E#" on it.</summary>
    public static (IconPicture Classic, IconPicture Colour) Default()
    {
        // Pixel classes: 0 nothing, 1 outline, 2 body (upper half), 6 body (lower half), 3 letter, 4 highlight, 5 shadow.
        var cls = new byte[W * H];
        for (var y = 1; y < H - 1; y++)
            for (var x = 1; x < W - 1; x++)
            {
                var corner = (x < 4 || x >= W - 4) && (y < 4 || y >= H - 4);   // cut the corners
                if (corner && (Math.Min(x, W - 1 - x) + Math.Min(y, H - 1 - y)) < 3) continue;
                var edge = x == 1 || y == 1 || x == W - 2 || y == H - 2;
                cls[y * W + x] = (byte)(edge ? 1 : y == 2 || x == 2 ? 4 : y == H - 3 || x == W - 3 ? 5 : y < H / 2 ? 2 : 6);
            }
        void Glyph(string[] g, int left, int top)
        {
            for (var gy = 0; gy < 7; gy++)
                for (var gx = 0; gx < 5; gx++)
                    if (g[gy][gx] == '#')
                        for (var dy = 0; dy < 2; dy++)
                            for (var dx = 0; dx < 2; dx++)
                                cls[(top + gy * 2 + dy) * W + left + gx * 2 + dx] = 3;
        }
        Glyph(GlyphE, 9, 9);
        Glyph(GlyphHash, 27, 9);

        var classicMap = new byte[] { 0, 1, 3, 2, 2, 1, 3 };
        var classic = new IconPicture(W, H, cls.Select(c => classicMap[c]).ToArray(), ClassicPalette);
        var palette = new uint[] { 0x000000, 0x0E1C46, 0x3B72DC, 0xFFFFFF, 0x9CC4FF, 0x14285C, 0x2A58B4 };
        var colour = new IconPicture(W, H, cls, palette, transparentIndex: 0);
        return (classic, colour);
    }

    /// <summary>
    /// Converts a PNG for the icon: the classic image maps every opaque pixel to the nearest of the four Workbench colours
    /// (transparent pixels become the background), the colour image has up to 64 colours with colour 0 transparent when the PNG has transparency.
    /// Images larger than 256 x 256 are refused (the colour format stores sizes in a byte).
    /// </summary>
    public static (IconPicture Classic, IconPicture Colour) FromPng(byte[] png)
    {
        var img = Png.Read(png);
        if (img.Width > 256 || img.Height > 256) throw new InvalidDataException("an icon PNG is at most 256 x 256 pixels");
        var n = img.Width * img.Height;
        var ci = new byte[n];
        for (var i = 0; i < n; i++)
        {
            var o = i * 4;
            if (img.Rgba[o + 3] < 128) { ci[i] = 0; continue; }
            var best = 1; var bestD = int.MaxValue;
            for (var k = 1; k < 4; k++)
            {
                var p = ClassicPalette[k];
                int dr = img.Rgba[o] - (int)(p >> 16 & 255), dg = img.Rgba[o + 1] - (int)(p >> 8 & 255), db = img.Rgba[o + 2] - (int)(p & 255);
                var d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = k; }
            }
            ci[i] = (byte)best;
        }
        var planar = Planar.Convert(img, 64, aga: true);
        var palette = planar.Palette24.Take(1 << planar.Depth).ToArray();
        var indices = planar.Indices;
        var transparent = img.Rgba.Where((_, i) => i % 4 == 3).Any(a => a < 128);
        if (transparent)
        {
            // Planar.Convert shares entry 0 between "transparent" and black (a blit mask says which is which). An icon has no mask, so
            // an opaque pixel must not use entry 0: those pixels move to an extra black entry at the end.
            var black = (byte)palette.Length;
            var moved = false;
            indices = (byte[])indices.Clone();
            for (var i = 0; i < n; i++)
                if (img.Rgba[i * 4 + 3] >= 128 && indices[i] == 0) { indices[i] = black; moved = true; }
            if (moved) palette = palette.Append(0x000000u).ToArray();
        }
        return (new IconPicture(img.Width, img.Height, ci, ClassicPalette), new IconPicture(img.Width, img.Height, indices, palette, transparent ? 0 : -1));
    }
}
