using System;
using System.IO;

namespace ESharp.Assets;

/// <summary>
/// Turns a font image (a PNG or ILBM grid of equal glyph cells) into glyph bits for <c>ESharp.Platform.BitmapFont</c>: one bit per
/// pixel, <c>(cellWidth + 7) / 8</c> bytes per glyph row, leftmost pixel in the most significant bit, glyph after glyph from the
/// first character. The glyphs run left to right, top to bottom, <paramref name="columns"/> per row of cells.
/// A pixel is ink when it is opaque (alpha 128 or more) and not the background colour; the background is the colour of the
/// image's top-left pixel (so keep the first cell's corner empty), or, when that pixel is transparent, any transparent pixel.
/// </summary>
public static class FontImage
{
    public const int MaxCell = 64;

    public sealed record Glyphs(byte[] Data, int Columns, int Rows, int InkPixels);

    public static Glyphs Convert(RgbaImage image, int cellWidth, int cellHeight, int first, int count, int columns = 0)
    {
        if (cellWidth < 1 || cellWidth > MaxCell || cellHeight < 1 || cellHeight > MaxCell)
            throw new InvalidDataException($"font cell {cellWidth}x{cellHeight} is not between 1x1 and {MaxCell}x{MaxCell}");
        if (first < 0 || first > 255) throw new InvalidDataException($"font first character {first} is not between 0 and 255");
        if (count < 1 || first + count > 256) throw new InvalidDataException($"font count {count} with first character {first} does not fit the character codes 0..255");
        if (image.Width % cellWidth != 0)
            throw new InvalidDataException($"the font image is {image.Width} pixels wide, not a multiple of the cell width {cellWidth}");
        var fit = image.Width / cellWidth;
        if (columns == 0) columns = fit;
        if (columns < 1 || columns > fit)
            throw new InvalidDataException($"font columns {columns} do not fit an image {image.Width} pixels wide with cells {cellWidth} wide ({fit} fit)");
        var rows = (count + columns - 1) / columns;
        if (image.Height < rows * cellHeight)
            throw new InvalidDataException($"the font image is {image.Height} pixels high but {count} glyphs in {columns} columns of cells {cellHeight} high need {rows * cellHeight}");

        var rowBytes = (cellWidth + 7) / 8;
        var data = new byte[count * cellHeight * rowBytes];
        var bgOpaque = image.Rgba[3] >= 128;
        var (br, bg, bb) = (image.Rgba[0], image.Rgba[1], image.Rgba[2]);
        var ink = 0;
        for (var g = 0; g < count; g++)
        {
            var ox = g % columns * cellWidth;
            var oy = g / columns * cellHeight;
            for (var y = 0; y < cellHeight; y++)
                for (var x = 0; x < cellWidth; x++)
                {
                    var o = ((oy + y) * image.Width + ox + x) * 4;
                    if (image.Rgba[o + 3] < 128) continue;
                    if (bgOpaque && image.Rgba[o] == br && image.Rgba[o + 1] == bg && image.Rgba[o + 2] == bb) continue;
                    data[(g * cellHeight + y) * rowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    ink++;
                }
        }
        return new Glyphs(data, columns, rows, ink);
    }
}
