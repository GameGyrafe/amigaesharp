using System;
using System.Collections.Generic;
using System.Linq;

namespace ESharp.Assets;

/// <summary>An indexed image ready for the Amiga: planar data and a 12-bit (OCS) palette.</summary>
public sealed class PlanarImage
{
    public PlanarImage(int width, int height, int depth, byte[] planes, ushort[] palette, byte[] indices)
    {
        Width = width;
        Height = height;
        Depth = depth;
        Planes = planes;
        Palette = palette;
        Indices = indices;
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    /// <summary>Bitplane after bitplane; within a plane each row is padded to a multiple of 16 pixels, most significant bit leftmost.</summary>
    public byte[] Planes { get; }

    /// <summary>0x0RGB words, 4 bits per channel, <c>1 &lt;&lt; Depth</c> entries.</summary>
    public ushort[] Palette { get; }

    /// <summary>The palette index of every pixel (kept for tests and previews).</summary>
    public byte[] Indices { get; }

    public int BytesPerRow => (Width + 15) / 16 * 2;
}

public static class Planar
{
    /// <summary>
    /// Reduces <paramref name="image"/> to at most <paramref name="maxColors"/> colours and packs it into bitplanes.
    /// Colours are first rounded to 4 bits per channel (what OCS can show). If that leaves no more than
    /// <paramref name="maxColors"/> distinct colours the mapping is exact, otherwise median cut chooses the palette.
    /// Pixels with alpha below 128 become palette entry 0, which is black. No dithering yet.
    /// </summary>
    public static PlanarImage Convert(RgbaImage image, int maxColors)
    {
        if (maxColors < 2 || maxColors > 32) throw new ArgumentOutOfRangeException(nameof(maxColors), "colours must be 2..32");
        var n = image.Width * image.Height;
        var rgb12 = new int[n];
        var transparent = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var o = i * 4;
            transparent[i] = image.Rgba[o + 3] < 128;
            rgb12[i] = (Round4(image.Rgba[o]) << 8) | (Round4(image.Rgba[o + 1]) << 4) | Round4(image.Rgba[o + 2]);
        }

        var hasTransparent = transparent.Any(t => t);
        var colorBudget = hasTransparent ? maxColors - 1 : maxColors;
        var distinct = rgb12.Where((c, i) => !transparent[i]).Distinct().OrderBy(c => c).ToList();
        List<int> palette = distinct.Count <= colorBudget ? distinct : MedianCut(rgb12.Where((c, i) => !transparent[i]).ToList(), colorBudget);

        // Entry 0 is the transparent/background colour: black when there is transparency.
        if (hasTransparent) { palette.Remove(0); palette.Insert(0, 0); }

        var depth = 1;
        while ((1 << depth) < palette.Count) depth++;
        var entries = new ushort[1 << depth];
        for (var i = 0; i < palette.Count; i++) entries[i] = (ushort)palette[i];

        var indices = new byte[n];
        var cache = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            if (transparent[i]) { indices[i] = 0; continue; }
            if (!cache.TryGetValue(rgb12[i], out var best))
            {
                best = Nearest(palette, rgb12[i], hasTransparent ? 1 : 0);
                cache[rgb12[i]] = best;
            }
            indices[i] = (byte)best;
        }

        var bytesPerRow = (image.Width + 15) / 16 * 2;
        var planes = new byte[depth * bytesPerRow * image.Height];
        for (var p = 0; p < depth; p++)
            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                    if ((indices[y * image.Width + x] >> p & 1) != 0)
                        planes[p * bytesPerRow * image.Height + y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));

        return new PlanarImage(image.Width, image.Height, depth, planes, entries, indices);
    }

    private static int Round4(int v) => (v * 15 + 127) / 255;

    private static int Nearest(List<int> palette, int color, int first)
    {
        var best = first;
        var bestDistance = int.MaxValue;
        for (var i = first; i < palette.Count; i++)
        {
            var d = Distance(palette[i], color);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    private static int Distance(int a, int b)
    {
        var dr = ((a >> 8) & 15) - ((b >> 8) & 15);
        var dg = ((a >> 4) & 15) - ((b >> 4) & 15);
        var db = (a & 15) - (b & 15);
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>Median cut over 12-bit colours, weighting by how often each colour occurs.</summary>
    private static List<int> MedianCut(List<int> pixels, int count)
    {
        var boxes = new List<List<int>> { pixels };
        while (boxes.Count < count)
        {
            var target = boxes.Where(b => b.Distinct().Count() > 1).OrderByDescending(b => b.Count).FirstOrDefault();
            if (target == null) break;
            var channel = WidestChannel(target);
            var sorted = target.OrderBy(c => (c >> channel) & 15).ToList();
            var mid = sorted.Count / 2;
            // Keep equal values together so a box never splits one colour.
            var split = mid;
            while (split < sorted.Count && ((sorted[split] >> channel) & 15) == ((sorted[mid - 1] >> channel) & 15)) split++;
            if (split >= sorted.Count) { split = mid; while (split > 0 && ((sorted[split - 1] >> channel) & 15) == ((sorted[mid] >> channel) & 15)) split--; }
            if (split <= 0 || split >= sorted.Count) split = mid;
            boxes.Remove(target);
            boxes.Add(sorted.Take(split).ToList());
            boxes.Add(sorted.Skip(split).ToList());
        }
        return boxes.Select(Average).Distinct().OrderBy(c => c).ToList();
    }

    private static int WidestChannel(List<int> colors)
    {
        var best = 0;
        var bestRange = -1;
        foreach (var shift in new[] { 8, 4, 0 })
        {
            var values = colors.Select(c => (c >> shift) & 15);
            var range = values.Max() - values.Min();
            if (range > bestRange) { bestRange = range; best = shift; }
        }
        return best;
    }

    private static int Average(List<int> colors)
    {
        int r = 0, g = 0, b = 0;
        foreach (var c in colors) { r += (c >> 8) & 15; g += (c >> 4) & 15; b += c & 15; }
        var n = colors.Count;
        return (((r + n / 2) / n) << 8) | (((g + n / 2) / n) << 4) | ((b + n / 2) / n);
    }
}
