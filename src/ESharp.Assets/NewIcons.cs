using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ESharp.Assets;

/// <summary>
/// The NewIcons image (issue #101): NewIcons, a freeware add-on for Workbench 3.0/3.1, keeps a palette image in an icon's tool types, so a
/// colour icon shows on systems whose icon.library knows only the classic planar image. AROS's and AmigaOS 4's icon.library read it too
/// (they use it only when the icon has no OS 3.5 colour image). Written from the format as we understand it, in our own words:
///
/// <para>The tool types end with a marker line (<see cref="Marker"/>), then the lines of the normal image, each starting <c>IM1=</c>, then the
/// lines of the selected image, each starting <c>IM2=</c> (same size as the normal one). The first line of an image starts with a 5-character
/// header: <c>B</c> when colour 0 is transparent (<c>C</c> when not), the width + 0x21, the height + 0x21, and the colour count as two 6-bit
/// digits, each + 0x21 (high digit first). After the header that line carries the palette (R, G, B bytes); the pixels start on the next line,
/// each pixel a palette index of just enough bits for the colour count.</para>
///
/// <para>Both are a bit stream, most significant bit first, cut into 7-bit groups; every character carries one group: values 0x00-0x4F are
/// written as 0x20-0x6F and 0x50-0x7F as 0xA1-0xD0. A character 0xD1-0xFF stands for (character - 0xD0) groups of zero, a run-length
/// shortcut. A reader starts each line with an empty bit buffer, so a value never crosses a line end; the zero bits that pad the last
/// character of a line are fewer than one value (bits that would make a whole value would be read as one).</para>
///
/// <para>E#'s own limits (choices, not format rules): a line is at most <see cref="MaxLine"/> characters, the picture at most
/// <see cref="MaxSize"/> pixels each way (larger ones get no NewIcons image), and at most <see cref="MaxColours"/> colours, so an icon takes
/// only a few of Workbench's free pens.</para>
/// </summary>
public static class NewIcons
{
    /// <summary>The tool type that starts the NewIcons lines.</summary>
    public const string Marker = "*** DON'T EDIT THE FOLLOWING LINES!! ***";
    public const int MaxLine = 127;
    public const int MaxSize = 93;
    public const int MaxColours = 32;

    /// <summary>The selected image: the same pixels with every colour at two thirds brightness.</summary>
    public static IconPicture Selected(IconPicture normal) =>
        new(normal.Width, normal.Height, normal.Indices,
            normal.Palette.Select(c => (c >> 16 & 255) * 2 / 3 << 16 | (c >> 8 & 255) * 2 / 3 << 8 | (c & 255) * 2 / 3).ToArray(),
            normal.TransparentIndex);

    /// <summary>The tool type lines (marker, IM1=..., IM2=...) for <paramref name="normal"/> and an optional selected image.</summary>
    public static List<string> Encode(IconPicture normal, IconPicture? selected = null)
    {
        var lines = new List<string> { Marker };
        lines.AddRange(EncodeImage(normal, 1));
        var sel = selected ?? Selected(normal);
        if (sel.Width != normal.Width || sel.Height != normal.Height) throw new ArgumentException("the selected image has the size of the normal one");
        lines.AddRange(EncodeImage(sel, 2));
        return lines;
    }

    private static List<string> EncodeImage(IconPicture p, int which)
    {
        if (p.Width > MaxSize || p.Height > MaxSize) throw new ArgumentException($"a NewIcons image is at most {MaxSize} x {MaxSize}");
        if (p.Palette.Length > MaxColours) throw new ArgumentException($"a NewIcons image has at most {MaxColours} colours");
        // Colour 0 is the transparent one in this format: swap the picture's transparent entry into place.
        var indices = p.Indices; var palette = p.Palette;
        if (p.TransparentIndex > 0)
        {
            var t = (byte)p.TransparentIndex;
            indices = indices.Select(i => i == t ? (byte)0 : i == 0 ? t : i).ToArray();
            palette = (uint[])palette.Clone();
            (palette[0], palette[t]) = (palette[t], palette[0]);
        }
        var n = palette.Length;
        var prefix = "IM" + which + "=";
        var header = prefix + (p.TransparentIndex >= 0 ? 'B' : 'C') + (char)(p.Width + 0x21) + (char)(p.Height + 0x21)
                     + (char)((n >> 6) + 0x21) + (char)((n & 63) + 0x21);
        var pal = new int[n * 3];
        for (var i = 0; i < n; i++) { pal[i * 3] = (int)(palette[i] >> 16 & 255); pal[i * 3 + 1] = (int)(palette[i] >> 8 & 255); pal[i * 3 + 2] = (int)(palette[i] & 255); }
        var bits = 1; while ((1 << bits) < n) bits++;

        var lines = new List<string>();
        Pack(pal, 8, header, prefix, lines);
        Pack(indices.Select(i => (int)i).ToArray(), bits, null, prefix, lines);
        return lines;
    }

    // Splits values of `bits` bits into lines. A line holding g groups of 7 bits holds floor(7g / bits) values, so its padding is less than one
    // value; g grows while the line (after the zero-run shortcut) still fits.
    private static void Pack(int[] values, int bits, string? first, string prefix, List<string> lines)
    {
        var at = 0;
        var start = first ?? prefix;
        while (at < values.Length)
        {
            var room = MaxLine - start.Length;
            string? best = null; var bestCount = 0;
            for (var g = 1; ; g++)
            {
                var count = Math.Min(7 * g / bits, values.Length - at);
                if (count == 0) continue;
                var text = Chars(values, at, count, bits);
                if (text.Length > room) break;
                best = text; bestCount = count;
                if (at + count == values.Length) break;
            }
            if (best == null) throw new InvalidOperationException("a NewIcons line has no room");
            lines.Add(start + best);
            at += bestCount;
            start = prefix;
        }
    }

    private static string Chars(int[] values, int at, int count, int bits)
    {
        var groups = new List<int>();
        int buf = 0, have = 0;
        for (var i = at; i < at + count; i++)
        {
            buf = buf << bits | values[i]; have += bits;
            while (have >= 7) { groups.Add(buf >> (have - 7) & 0x7F); have -= 7; buf &= (1 << have) - 1; }
        }
        if (have > 0) groups.Add(buf << (7 - have) & 0x7F);
        var sb = new StringBuilder();
        for (var i = 0; i < groups.Count;)
        {
            if (groups[i] == 0)
            {
                var run = 1;
                while (i + run < groups.Count && groups[i + run] == 0 && run < 0xFF - 0xD0) run++;
                if (run > 1) { sb.Append((char)(0xD0 + run)); i += run; continue; }
            }
            var v = groups[i++];
            sb.Append((char)(v < 0x50 ? v + 0x20 : v + 0x51));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads the NewIcons images from tool types (null when there is no marker). <paramref name="rest"/> gets the tool types that are not
    /// NewIcons lines. Throws InvalidDataException for broken data.
    /// </summary>
    public static (IconPicture Normal, IconPicture? Selected)? Decode(IReadOnlyList<string> toolTypes, out List<string> rest)
    {
        rest = new List<string>();
        var m = -1;
        for (var i = 0; i < toolTypes.Count; i++) if (toolTypes[i] == Marker) { m = i; break; }
        if (m < 0) { rest.AddRange(toolTypes); return null; }
        rest.AddRange(toolTypes.Take(m));
        rest.AddRange(toolTypes.Skip(m + 1).Where(t => !IsImageLine(t)));
        var after = toolTypes.Skip(m + 1).ToList();
        var normal = DecodeImage(after, 1) ?? throw new InvalidDataException("NewIcons marker without IM1 lines");
        var sel = DecodeImage(after, 2);
        return (normal, sel);
    }

    /// <summary>True for a NewIcons image line (IM1= or IM2=).</summary>
    public static bool IsImageLine(string s) => s.StartsWith("IM1=", StringComparison.Ordinal) || s.StartsWith("IM2=", StringComparison.Ordinal);

    private static IconPicture? DecodeImage(List<string> lines, int which)
    {
        var prefix = "IM" + which + "=";
        var mine = lines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).Select(l => l.Substring(4)).ToList();
        if (mine.Count == 0) return null;
        var h = mine[0];
        if (h.Length < 5) throw new InvalidDataException("short NewIcons header");
        var transparent = h[0] == 'B';
        int w = h[1] - 0x21, ht = h[2] - 0x21, n = ((h[3] - 0x21) << 6) + (h[4] - 0x21);
        if (w < 1 || ht < 1 || n < 1 || n > 256) throw new InvalidDataException("bad NewIcons header");
        var line = 0;
        var pal = Unpack(mine, ref line, h.Substring(5), n * 3, 8);
        var bits = 1; while ((1 << bits) < n) bits++;
        var idx = Unpack(mine, ref line, null, w * ht, bits);
        var palette = new uint[n];
        for (var i = 0; i < n; i++) palette[i] = (uint)(pal[i * 3] << 16 | pal[i * 3 + 1] << 8 | pal[i * 3 + 2]);
        return new IconPicture(w, ht, idx.Select(v => (byte)Math.Min(v, n - 1)).ToArray(), palette, transparent ? 0 : -1);
    }

    private static int[] Unpack(List<string> lines, ref int line, string? first, int count, int bits)
    {
        var o = new int[count]; var got = 0;
        var text = first;
        if (text == null) { line++; text = line < lines.Count ? lines[line] : null; }
        while (got < count)
        {
            if (text == null) throw new InvalidDataException("NewIcons data ends early");
            int buf = 0, have = 0;
            foreach (var ch in text)
            {
                int groups, v;
                if (ch >= 0xD1) { groups = ch - 0xD0; v = 0; }
                else { groups = 1; v = ch < 0xA0 ? ch - 0x20 : ch - 0x51; }
                for (var g = 0; g < groups; g++)
                {
                    buf = (buf << 7 | v) & 0x7FFF; have += 7;
                    while (have >= bits && got < count) { o[got++] = buf >> (have - bits) & ((1 << bits) - 1); have -= bits; }
                }
            }
            if (got < count) { line++; text = line < lines.Count ? lines[line] : null; }
        }
        return o;
    }
}
