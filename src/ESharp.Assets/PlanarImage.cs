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

    /// <summary>For AGA images (<see cref="Planar.Convert"/> with <c>aga: true</c>): 0x00RRGGBB, 8 bits per channel, <c>1 &lt;&lt; Depth</c> entries. Empty for OCS images.</summary>
    public uint[] Palette24 { get; init; } = Array.Empty<uint>();

    public int BytesPerRow => (Width + 15) / 16 * 2;

    /// <summary>One bitplane laid out like a plane of <see cref="Planes"/>: a 1 bit marks an opaque pixel (alpha of 128 or more). Used for masked blits.</summary>
    public byte[] Mask { get; init; } = Array.Empty<byte>();
}

/// <summary>How colours are spread over the palette when an image is reduced.</summary>
public enum Dither
{
    /// <summary>Every pixel takes its nearest palette colour (flat bands).</summary>
    None,
    /// <summary>Floyd-Steinberg error diffusion, left to right.</summary>
    FloydSteinberg,
    /// <summary>A 4x4 Bayer matrix added to the colour before the nearest-colour search.</summary>
    Ordered,
}

public static class Planar
{
    /// <summary>Parses a manifest value: null/"none", "fs"/"floyd-steinberg", "ordered"/"bayer".</summary>
    public static Dither ParseDither(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "none" => Dither.None,
        "fs" or "floyd-steinberg" or "floydsteinberg" => Dither.FloydSteinberg,
        "ordered" or "bayer" => Dither.Ordered,
        _ => throw new System.IO.InvalidDataException($"unknown dither '{name}' (use \"fs\", \"ordered\" or \"none\")"),
    };

    private static readonly int[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    /// <summary>
    /// Reduces <paramref name="image"/> to at most <paramref name="maxColors"/> colours and packs it into bitplanes.
    /// Colours are first rounded to 4 bits per channel (what OCS can show), or with <paramref name="aga"/> kept at 8 bits per
    /// channel (up to 256 colours; the palette comes back in <see cref="PlanarImage.Palette24"/>). If that leaves no more than
    /// <paramref name="maxColors"/> distinct colours the mapping is exact, otherwise median cut chooses the palette.
    /// Pixels with alpha below 128 become palette entry 0, which is black (opaque black pixels use it too; the mask tells them apart).
    /// With <paramref name="dither"/> the pixels are mapped to the reduced palette with error diffusion or an ordered matrix (computed from the full 8-bit source colours); it
    /// only matters when colours were really reduced (an exact mapping is left alone), and transparent pixels neither take nor give error.
    /// </summary>
    public static PlanarImage Convert(RgbaImage image, int maxColors, bool aga = false, Dither dither = Dither.None)
    {
        var limit = aga ? 256 : 32;
        if (maxColors < 2 || maxColors > limit) throw new ArgumentOutOfRangeException(nameof(maxColors), $"colours must be 2..{limit}");
        var bits = aga ? 8 : 4;
        var n = image.Width * image.Height;
        var rgb12 = new int[n];
        var transparent = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var o = i * 4;
            transparent[i] = image.Rgba[o + 3] < 128;
            rgb12[i] = (Round(image.Rgba[o], bits) << (2 * bits)) | (Round(image.Rgba[o + 1], bits) << bits) | Round(image.Rgba[o + 2], bits);
        }

        var hasTransparent = transparent.Any(t => t);
        var colorBudget = hasTransparent ? maxColors - 1 : maxColors;
        var distinct = rgb12.Where((c, i) => !transparent[i]).Distinct().OrderBy(c => c).ToList();
        List<int> palette = distinct.Count <= colorBudget ? distinct : MedianCut(rgb12.Where((c, i) => !transparent[i]).ToList(), colorBudget, bits);

        // Entry 0 is the transparent/background colour: black when there is transparency.
        if (hasTransparent) { palette.Remove(0); palette.Insert(0, 0); }

        var depth = 1;
        while ((1 << depth) < palette.Count) depth++;
        var entries = new ushort[aga ? 0 : 1 << depth];
        var entries24 = new uint[aga ? 1 << depth : 0];
        for (var i = 0; i < palette.Count; i++)
        {
            if (aga) entries24[i] = (uint)palette[i];
            else entries[i] = (ushort)palette[i];
        }

        var indices = new byte[n];
        if (dither != Dither.None && distinct.Count > colorBudget)
            DitherMap(image, transparent, palette, bits, dither, indices);
        else
        {
            var cache = new Dictionary<int, int>();
            for (var i = 0; i < n; i++)
            {
                if (transparent[i]) { indices[i] = 0; continue; }
                if (!cache.TryGetValue(rgb12[i], out var best))
                {
                    best = Nearest(palette, rgb12[i], 0, bits); // black may use entry 0: the mask, not the index, says what is transparent
                    cache[rgb12[i]] = best;
                }
                indices[i] = (byte)best;
            }
        }

        var bytesPerRow = (image.Width + 15) / 16 * 2;
        var planes = new byte[depth * bytesPerRow * image.Height];
        for (var p = 0; p < depth; p++)
            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                    if ((indices[y * image.Width + x] >> p & 1) != 0)
                        planes[p * bytesPerRow * image.Height + y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));

        var mask = new byte[bytesPerRow * image.Height];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (!transparent[y * image.Width + x])
                    mask[y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));

        return new PlanarImage(image.Width, image.Height, depth, planes, entries, indices) { Mask = mask, Palette24 = entries24 };
    }

    private static void DitherMap(RgbaImage image, bool[] transparent, List<int> palette, int bits, Dither dither, byte[] indices)
    {
        var w = image.Width;
        var h = image.Height;
        var max = (1 << bits) - 1;
        var mask = max;
        // The palette in 0..255 per channel, so errors are measured on the source scale.
        var pr = new int[palette.Count]; var pg = new int[palette.Count]; var pb = new int[palette.Count];
        for (var k = 0; k < palette.Count; k++)
        {
            pr[k] = (((palette[k] >> (2 * bits)) & mask) * 255 + max / 2) / max;
            pg[k] = (((palette[k] >> bits) & mask) * 255 + max / 2) / max;
            pb[k] = ((palette[k] & mask) * 255 + max / 2) / max;
        }
        var spread = (float)(255.0 / (2 * Math.Cbrt(palette.Count)));
        var err = new float[3][] { new float[w * h], new float[w * h], new float[w * h] };
        var v = new float[3];
        var e = new float[3];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (transparent[i]) { indices[i] = 0; continue; }
                for (var c = 0; c < 3; c++) v[c] = image.Rgba[i * 4 + c];
                if (dither == Dither.Ordered)
                {
                    var bias = (float)((Bayer4[(y & 3) * 4 + (x & 3)] + 0.5) / 16 - 0.5) * spread;
                    for (var c = 0; c < 3; c++) v[c] += bias;
                }
                else
                    for (var c = 0; c < 3; c++) v[c] += err[c][i];
                var best = 0;
                var bestD = float.MaxValue;
                for (var k = 0; k < palette.Count; k++)
                {
                    var dr = v[0] - pr[k]; var dg = v[1] - pg[k]; var db = v[2] - pb[k];
                    var d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = k; }
                }
                indices[i] = (byte)best;
                if (dither != Dither.FloydSteinberg) continue;
                e[0] = v[0] - pr[best]; e[1] = v[1] - pg[best]; e[2] = v[2] - pb[best];
                Diffuse(err, transparent, w, h, x + 1, y, e, 7f / 16);
                Diffuse(err, transparent, w, h, x - 1, y + 1, e, 3f / 16);
                Diffuse(err, transparent, w, h, x, y + 1, e, 5f / 16);
                Diffuse(err, transparent, w, h, x + 1, y + 1, e, 1f / 16);
            }
    }

    private static void Diffuse(float[][] err, bool[] transparent, int w, int h, int x, int y, float[] e, float f)
    {
        if (x < 0 || x >= w || y >= h) return;
        var j = y * w + x;
        if (transparent[j]) return;
        for (var c = 0; c < 3; c++) err[c][j] += e[c] * f;
    }

    private static int Round(int v, int bits)
    {
        var max = (1 << bits) - 1;
        return (v * max + 127) / 255;
    }

    private static int Nearest(List<int> palette, int color, int first, int bits)
    {
        var best = first;
        var bestDistance = int.MaxValue;
        for (var i = first; i < palette.Count; i++)
        {
            var d = Distance(palette[i], color, bits);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    private static int Distance(int a, int b, int bits)
    {
        var mask = (1 << bits) - 1;
        var dr = ((a >> (2 * bits)) & mask) - ((b >> (2 * bits)) & mask);
        var dg = ((a >> bits) & mask) - ((b >> bits) & mask);
        var db = (a & mask) - (b & mask);
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>Median cut over packed colours (<paramref name="bits"/> per channel), weighting by how often each colour occurs.</summary>
    private static List<int> MedianCut(List<int> pixels, int count, int bits)
    {
        var mask = (1 << bits) - 1;
        var boxes = new List<List<int>> { pixels };
        while (boxes.Count < count)
        {
            var target = boxes.Where(b => b.Distinct().Count() > 1).OrderByDescending(b => b.Count).FirstOrDefault();
            if (target == null) break;
            var channel = WidestChannel(target, bits);
            var sorted = target.OrderBy(c => (c >> channel) & mask).ToList();
            var mid = sorted.Count / 2;
            // Keep equal values together so a box never splits one colour.
            var split = mid;
            while (split < sorted.Count && ((sorted[split] >> channel) & mask) == ((sorted[mid - 1] >> channel) & mask)) split++;
            if (split >= sorted.Count) { split = mid; while (split > 0 && ((sorted[split - 1] >> channel) & mask) == ((sorted[mid] >> channel) & mask)) split--; }
            if (split <= 0 || split >= sorted.Count) split = mid;
            boxes.Remove(target);
            boxes.Add(sorted.Take(split).ToList());
            boxes.Add(sorted.Skip(split).ToList());
        }
        return boxes.Select(b => Average(b, bits)).Distinct().OrderBy(c => c).ToList();
    }

    private static int WidestChannel(List<int> colors, int bits)
    {
        var mask = (1 << bits) - 1;
        var best = 0;
        var bestRange = -1;
        foreach (var shift in new[] { 2 * bits, bits, 0 })
        {
            var values = colors.Select(c => (c >> shift) & mask);
            var range = values.Max() - values.Min();
            if (range > bestRange) { bestRange = range; best = shift; }
        }
        return best;
    }

    private static int Average(List<int> colors, int bits)
    {
        var mask = (1 << bits) - 1;
        long r = 0, g = 0, b = 0;
        foreach (var c in colors) { r += (c >> (2 * bits)) & mask; g += (c >> bits) & mask; b += c & mask; }
        var n = colors.Count;
        return (int)(((r + n / 2) / n) << (2 * bits) | ((g + n / 2) / n) << bits | ((b + n / 2) / n));
    }
}
