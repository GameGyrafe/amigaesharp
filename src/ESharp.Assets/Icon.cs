using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ESharp.Assets;

/// <summary>
/// A picture for an icon: one palette index per pixel (row after row) and the palette as 0xRRGGBB. Index
/// <see cref="TransparentIndex"/> (when not -1) is drawn as "nothing" by the colour icon.
/// </summary>
public sealed class IconPicture
{
    public IconPicture(int width, int height, byte[] indices, uint[] palette, int transparentIndex = -1)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "an icon image has at least one pixel");
        if (indices.Length != width * height) throw new ArgumentException("one index per pixel", nameof(indices));
        if (palette.Length < 1 || palette.Length > 256) throw new ArgumentException("1..256 colours", nameof(palette));
        foreach (var i in indices) if (i >= palette.Length) throw new ArgumentException("an index is outside the palette", nameof(indices));
        Width = width; Height = height; Indices = indices; Palette = palette; TransparentIndex = transparentIndex;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Indices { get; }
    public uint[] Palette { get; }
    public int TransparentIndex { get; }

    /// <summary>Bits per pixel needed for the palette (at least 1).</summary>
    public int Depth { get { var d = 1; while ((1 << d) < Palette.Length) d++; return d; } }
}

/// <summary>The kinds of icon (the DiskObject type byte).</summary>
public enum IconType : byte { Disk = 1, Drawer = 2, Tool = 3, Project = 4, Garbage = 5, Device = 6, Kick = 7, AppIcon = 8 }

/// <summary>
/// A Workbench icon file (<c>.info</c>) as E# writes and reads it. Written from the file layout as we understand it, in our own words:
///
/// <para><b>Classic part (every Amiga OS reads it).</b> Big-endian throughout. A 78-byte DiskObject header: magic 0xE310, version 1, a
/// 44-byte Gadget record (position and size of the icon, flags, a non-zero "render" field when an image follows, and a user-data
/// long that holds 1 for the revision with tool types), the type byte, a pad byte, then six long fields that are only "something or
/// nothing" markers in the file (default tool, tool types, current X, current Y, drawer data, tool window), and the stack size as a
/// long. Current X and Y are 0x80000000 when the icon has no saved position. After the header, in this order: the drawer record
/// (drawers only; E# writes no drawers, so none), the normal image, the selected image (E# writes none), the default tool, the tool
/// types and the tool window. An image is a 20-byte record (left, top, width, height, depth words, a marker long, plane-pick and
/// plane-on/off bytes, a next-image long) followed by its bit planes, plane after plane, each row padded to whole words. A string is a
/// long length (with the closing zero byte) and the bytes. The tool types are a long that holds (count + 1) * 4, then one string each.</para>
///
/// <para><b>Colour part (OS 3.5 and later, OS 4, MorphOS, AROS).</b> A standard IFF <c>FORM</c> of type <c>ICON</c> appended after the classic data.
/// It holds a <c>FACE</c> chunk (width - 1, height - 1, flags, aspect, palette size - 1) and one or two <c>IMAG</c> chunks (normal, selected).
/// An IMAG has a 10-byte header (transparent colour, colour count - 1, flags with bit 0 = transparent colour valid and bit 1 = palette
/// present, image format, palette format, bits per pixel, size - 1 of the image data, size - 1 of the palette), then the pixels packed at
/// the given depth, then the palette as RGB byte triplets. E# writes the uncompressed format (0) with one byte per pixel and the depth
/// field set to 8: found by trying it on AmigaOS 4.1, whose Workbench drops the whole icon when an uncompressed image is packed at
/// fewer bits per pixel (3 for a 7-colour image) and shows it with 8. Readers that do not know the colour part stop after the classic
/// data and ignore it. The palette-size field in FACE is the largest palette in bytes minus 1.</para>
/// </summary>
public sealed class DiskObject
{
    public const ushort Magic = 0xE310;
    public const int HeaderSize = 78;
    /// <summary>The value of the position fields when the icon has no saved place on the Workbench window.</summary>
    public const int NoPosition = unchecked((int)0x80000000);

    public IconType Type { get; set; } = IconType.Tool;
    public int StackSize { get; set; } = 16384;
    public string? DefaultTool { get; set; }
    public List<string> ToolTypes { get; } = new();
    public string? ToolWindow { get; set; }
    public int CurrentX { get; set; } = NoPosition;
    public int CurrentY { get; set; } = NoPosition;
    /// <summary>The classic image: at most 4 colours are meaningful for a 2-bitplane icon, but any depth up to 8 is written.</summary>
    public IconPicture? Classic { get; set; }
    /// <summary>The OS 3.5+ colour image (up to 256 colours), or null for a classic-only icon.</summary>
    public IconPicture? Colour { get; set; }

    // Gadget flags: highlight by complementing the image, and "the render field is an image".
    private const ushort GadgetFlags = 0x0004;
    private const ushort GadgetActivation = 0x0003;   // release-verify and immediate
    private const ushort BoolGadget = 1;

    public byte[] Write()
    {
        var m = new MemoryStream();
        void W16(int v) { m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
        void W32(int v) { W16(v >> 16); W16(v); }
        void Str(string s)
        {
            var b = Encoding.Latin1.GetBytes(s);
            W32(b.Length + 1); m.Write(b); m.WriteByte(0);
        }

        if (Type is IconType.Drawer or IconType.Disk or IconType.Garbage)
            throw new NotSupportedException("E# writes tool and project icons only (no drawer record)");
        var c = Classic;
        W16(Magic); W16(1);
        // Gadget: next gadget, left, top, width, height, flags, activation, type, render, select render, text, mutual exclude, special info, id, user data.
        W32(0); W16(0); W16(0); W16(c?.Width ?? 0); W16(c?.Height ?? 0);
        W16(GadgetFlags); W16(GadgetActivation); W16(BoolGadget);
        W32(c != null ? 1 : 0); W32(0); W32(0); W32(0); W32(0); W16(0); W32(1);
        m.WriteByte((byte)Type); m.WriteByte(0);
        W32(DefaultTool != null ? 1 : 0);
        W32(ToolTypes.Count > 0 ? 1 : 0);
        W32(CurrentX); W32(CurrentY);
        W32(0);                                  // drawer data: none
        W32(ToolWindow != null ? 1 : 0);
        W32(StackSize);
        if (c != null)
        {
            var depth = Math.Min(c.Depth, 8);
            var bpr = (c.Width + 15) / 16 * 2;
            W16(0); W16(0); W16(c.Width); W16(c.Height); W16(depth);
            W32(1);                              // image data follows
            m.WriteByte((byte)((1 << depth) - 1)); m.WriteByte(0);   // plane pick (all planes), plane on/off
            W32(0);                              // next image: none
            for (var p = 0; p < depth; p++)
                for (var y = 0; y < c.Height; y++)
                {
                    var row = new byte[bpr];
                    for (var x = 0; x < c.Width; x++)
                        if ((c.Indices[y * c.Width + x] >> p & 1) != 0) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    m.Write(row);
                }
        }
        if (DefaultTool != null) Str(DefaultTool);
        if (ToolTypes.Count > 0)
        {
            W32((ToolTypes.Count + 1) * 4);
            foreach (var t in ToolTypes) Str(t);
        }
        if (ToolWindow != null) Str(ToolWindow);

        if (Colour != null) WriteColour(m, Colour);
        return m.ToArray();
    }

    private static void WriteColour(MemoryStream m, IconPicture p)
    {
        if (p.Width > 256 || p.Height > 256) throw new ArgumentException("the colour image is at most 256 x 256");
        const int depth = 8;
        var packed = p.Indices;                  // one byte per pixel (at most 256 x 256 = 65536 bytes, which the size field can hold)
        var pal = new byte[p.Palette.Length * 3];
        for (var i = 0; i < p.Palette.Length; i++)
        {
            pal[i * 3] = (byte)(p.Palette[i] >> 16); pal[i * 3 + 1] = (byte)(p.Palette[i] >> 8); pal[i * 3 + 2] = (byte)p.Palette[i];
        }

        var face = new byte[] { (byte)(p.Width - 1), (byte)(p.Height - 1), 1 /* no frame */, 0, (byte)((pal.Length - 1) >> 8), (byte)(pal.Length - 1) };
        var imag = new MemoryStream();
        imag.WriteByte((byte)Math.Max(p.TransparentIndex, 0));
        imag.WriteByte((byte)(p.Palette.Length - 1));
        imag.WriteByte((byte)((p.TransparentIndex >= 0 ? 1 : 0) | 2));
        imag.WriteByte(0); imag.WriteByte(0);   // image and palette stored uncompressed
        imag.WriteByte((byte)depth);
        imag.WriteByte((byte)((packed.Length - 1) >> 8)); imag.WriteByte((byte)(packed.Length - 1));
        imag.WriteByte((byte)((pal.Length - 1) >> 8)); imag.WriteByte((byte)(pal.Length - 1));
        imag.Write(packed); imag.Write(pal);

        var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("ICON"));
        Chunk(body, "FACE", face);
        Chunk(body, "IMAG", imag.ToArray());
        m.Write(Encoding.ASCII.GetBytes("FORM"));
        m.Write(Be32((int)body.Length));
        m.Write(body.ToArray());
    }

    private static void Chunk(MemoryStream m, string id, byte[] data)
    {
        m.Write(Encoding.ASCII.GetBytes(id)); m.Write(Be32(data.Length)); m.Write(data);
        if ((data.Length & 1) != 0) m.WriteByte(0);
    }

    private static byte[] Be32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <summary>Reads an icon file written by <see cref="Write"/> (tool and project icons, no drawer record). Throws InvalidDataException when it is not one.</summary>
    public static DiskObject Read(byte[] d)
    {
        var pos = 0;
        int U16() { if (pos + 2 > d.Length) throw new InvalidDataException("icon file too short"); var v = d[pos] << 8 | d[pos + 1]; pos += 2; return v; }
        int S32() { var v = U16() << 16; return v | U16(); }
        string Str()
        {
            var n = S32();
            if (n < 1 || pos + n > d.Length) throw new InvalidDataException("bad string in icon file");
            var s = Encoding.Latin1.GetString(d, pos, n - 1); pos += n; return s;
        }

        if (d.Length < HeaderSize || U16() != Magic) throw new InvalidDataException("not an icon file (magic 0xE310 missing)");
        U16();                                   // version
        S32(); U16(); U16();                     // next gadget, left, top
        U16(); U16();                            // gadget width and height (the image record has its own)
        U16(); U16(); U16();                     // flags, activation, type
        var render = S32(); S32(); S32(); S32(); S32(); U16(); S32();
        var o = new DiskObject { Type = (IconType)d[pos] };
        pos += 2;
        var hasTool = S32() != 0; var hasTypes = S32() != 0;
        o.CurrentX = S32(); o.CurrentY = S32();
        var drawer = S32(); var window = S32() != 0;
        o.StackSize = S32();
        if (drawer != 0) throw new InvalidDataException("drawer icons are not read");

        if (render != 0)
        {
            U16(); U16(); var w = U16(); var h = U16(); var depth = U16(); S32();
            pos += 2; S32();
            var bpr = (w + 15) / 16 * 2;
            if (depth > 8 || pos + bpr * h * depth > d.Length) throw new InvalidDataException("bad image in icon file");
            var idx = new byte[w * h];
            for (var p = 0; p < depth; p++)
                for (var y = 0; y < h; y++, pos += bpr)
                    for (var x = 0; x < w; x++)
                        if ((d[pos + (x >> 3)] & (0x80 >> (x & 7))) != 0) idx[y * w + x] |= (byte)(1 << p);
            var pal = new uint[1 << depth];
            o.Classic = new IconPicture(w, h, idx, pal);
        }
        if (hasTool) o.DefaultTool = Str();
        if (hasTypes)
        {
            var table = S32();
            for (var i = 0; i < table / 4 - 1; i++) o.ToolTypes.Add(Str());
        }
        if (window) o.ToolWindow = Str();

        if (pos + 12 <= d.Length && d[pos] == 'F' && d[pos + 1] == 'O' && d[pos + 2] == 'R' && d[pos + 3] == 'M')
        {
            pos += 4; var size = S32(); var end = Math.Min(pos + size, d.Length);
            if (Encoding.ASCII.GetString(d, pos, 4) != "ICON") throw new InvalidDataException("FORM is not ICON");
            pos += 4;
            int fw = 0, fh = 0;
            while (pos + 8 <= end)
            {
                var id = Encoding.ASCII.GetString(d, pos, 4); pos += 4;
                var len = S32(); var next = pos + len + (len & 1);
                if (id == "FACE") { fw = d[pos] + 1; fh = d[pos + 1] + 1; }
                else if (id == "IMAG" && o.Colour == null)
                {
                    var trans = d[pos]; var colours = d[pos + 1] + 1; var flags = d[pos + 2]; var depth = d[pos + 5];
                    var isize = (d[pos + 6] << 8 | d[pos + 7]) + 1; var psize = (d[pos + 8] << 8 | d[pos + 9]) + 1;
                    if (d[pos + 3] != 0 || d[pos + 4] != 0) throw new InvalidDataException("compressed colour icon");
                    var q = pos + 10;
                    if (depth != 8 || isize != fw * fh) throw new InvalidDataException("only 8-bit uncompressed colour images are read");
                    var idx = new byte[fw * fh];
                    Array.Copy(d, q, idx, 0, idx.Length);
                    q += isize;
                    var pal = new uint[colours];
                    for (var i = 0; i < colours && i * 3 < psize; i++) pal[i] = (uint)(d[q + i * 3] << 16 | d[q + i * 3 + 1] << 8 | d[q + i * 3 + 2]);
                    o.Colour = new IconPicture(fw, fh, idx, pal, (flags & 1) != 0 ? trans : -1);
                }
                pos = next;
            }
        }
        return o;
    }
}
