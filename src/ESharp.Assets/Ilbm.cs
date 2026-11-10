using System;
using System.Collections.Generic;
using System.IO;

namespace ESharp.Assets;

/// <summary>
/// A reader (and a small writer, for tests and sample generators) for IFF ILBM pictures: BMHD, CMAP, optional CAMG
/// and BODY, uncompressed or ByteRun1, with masking none, a mask plane, or a transparent colour index. 1 to 8
/// bitplanes use the colour map; 24 planes are read as true colour. HAM, Extra-Half-Brite and lasso masking are rejected.
/// </summary>
public static class Ilbm
{
    public static bool Looks(byte[] d) =>
        d.Length >= 12 && d[0] == 'F' && d[1] == 'O' && d[2] == 'R' && d[3] == 'M' && d[8] == 'I' && d[9] == 'L' && d[10] == 'B' && d[11] == 'M';

    private static int Be32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
    private static int Be16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static string Tag(byte[] d, int o) => System.Text.Encoding.ASCII.GetString(d, o, 4);

    public static RgbaImage Read(byte[] d)
    {
        if (!Looks(d)) throw new InvalidDataException("not an IFF ILBM file");
        int width = 0, height = 0, planes = 0, masking = 0, compression = 0, transparent = 0;
        uint camg = 0;
        byte[]? cmap = null;
        var bodyPos = -1;
        var bodyLen = 0;
        var haveHeader = false;
        for (var p = 12; p + 8 <= d.Length;)
        {
            var type = Tag(d, p);
            var len = Be32(d, p + 4);
            var body = p + 8;
            if (len < 0 || len > d.Length - body) len = d.Length - body;   // a truncated last chunk: use what is there
            switch (type)
            {
                case "BMHD":
                    if (len < 20) throw new InvalidDataException("ILBM: BMHD chunk is too short");
                    width = Be16(d, body); height = Be16(d, body + 2);
                    planes = d[body + 8]; masking = d[body + 9]; compression = d[body + 10];
                    transparent = Be16(d, body + 12);
                    haveHeader = true;
                    break;
                case "CMAP": cmap = d.AsSpan(body, len).ToArray(); break;
                case "CAMG": if (len >= 4) camg = (uint)Be32(d, body); break;
                case "BODY": bodyPos = body; bodyLen = len; break;
            }
            if (bodyPos >= 0) break;
            p = body + len + (len & 1);
        }
        if (!haveHeader) throw new InvalidDataException("ILBM: no BMHD chunk");
        if (bodyPos < 0) throw new InvalidDataException("ILBM: no BODY chunk");
        if ((camg & 0x800) != 0) throw new InvalidDataException("ILBM: HAM pictures are not supported (save the picture with a plain palette)");
        if ((camg & 0x80) != 0) throw new InvalidDataException("ILBM: Extra-Half-Brite pictures are not supported (save the picture with a plain palette)");
        if (width < 1 || height < 1) throw new InvalidDataException("ILBM: empty picture");
        if (planes != 24 && (planes < 1 || planes > 8)) throw new InvalidDataException($"ILBM: {planes} bitplanes are not supported (1 to 8, or 24)");
        if (masking > 2) throw new InvalidDataException("ILBM: lasso masking is not supported");
        if (compression > 1) throw new InvalidDataException($"ILBM: compression {compression} is not supported");
        if (planes != 24 && cmap == null) throw new InvalidDataException("ILBM: no CMAP chunk");

        if ((long)width * height > 4096L * 4096L) throw new InvalidDataException("ILBM: picture too large");
        var rowBytes = (width + 15) / 16 * 2;
        var rowPlanes = planes + (masking == 1 ? 1 : 0);
        var rgba = new byte[width * height * 4];
        var row = new byte[rowBytes * rowPlanes];
        var src = bodyPos;
        var end = bodyPos + bodyLen;
        for (var y = 0; y < height; y++)
        {
            if (compression == 0)
            {
                if (src + row.Length > end) throw new InvalidDataException("ILBM: BODY is truncated");
                Array.Copy(d, src, row, 0, row.Length);
                src += row.Length;
            }
            else
            {
                for (var o = 0; o < row.Length;)
                {
                    if (src >= end) throw new InvalidDataException("ILBM: BODY is truncated");
                    int n = (sbyte)d[src++];
                    if (n >= 0)
                    {
                        var count = n + 1;
                        if (src + count > end || o + count > row.Length) throw new InvalidDataException("ILBM: bad ByteRun1 data");
                        Array.Copy(d, src, row, o, count);
                        src += count; o += count;
                    }
                    else if (n != -128)
                    {
                        var count = 1 - n;
                        if (src >= end || o + count > row.Length) throw new InvalidDataException("ILBM: bad ByteRun1 data");
                        for (var i = 0; i < count; i++) row[o++] = d[src];
                        src++;
                    }
                }
            }
            for (var x = 0; x < width; x++)
            {
                int Bit(int plane) => (row[plane * rowBytes + (x >> 3)] >> (7 - (x & 7))) & 1;
                var o = (y * width + x) * 4;
                byte alpha = 255;
                if (planes == 24)
                {
                    int r = 0, g = 0, b = 0;
                    for (var i = 0; i < 8; i++) { r |= Bit(i) << i; g |= Bit(8 + i) << i; b |= Bit(16 + i) << i; }
                    rgba[o] = (byte)r; rgba[o + 1] = (byte)g; rgba[o + 2] = (byte)b;
                }
                else
                {
                    var index = 0;
                    for (var i = 0; i < planes; i++) index |= Bit(i) << i;
                    if (index * 3 + 2 < cmap!.Length)
                    {
                        rgba[o] = cmap[index * 3]; rgba[o + 1] = cmap[index * 3 + 1]; rgba[o + 2] = cmap[index * 3 + 2];
                    }
                    if (masking == 2 && index == transparent) alpha = 0;
                }
                if (masking == 1 && Bit(planes) == 0) alpha = 0;
                rgba[o + 3] = alpha;
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    /// <summary>Writes an indexed picture (<paramref name="depth"/> 1 to 8 planes, <paramref name="palette"/> as 8-bit RGB triples) as ILBM.</summary>
    /// <param name="maskPlane">Optional one-bool-per-pixel mask (true = opaque); written as masking 1 (hasMask).</param>
    /// <param name="transparentIndex">When not null and no mask plane, written as masking 2 (hasTransparentColor).</param>
    public static byte[] Write(int width, int height, int depth, byte[] indices, byte[] palette, bool byteRun1, bool[]? maskPlane = null, int? transparentIndex = null, uint camg = 0)
    {
        var rowBytes = (width + 15) / 16 * 2;
        var masking = maskPlane != null ? 1 : transparentIndex != null ? 2 : 0;
        var body = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            var row = new byte[rowBytes * (depth + (masking == 1 ? 1 : 0))];
            for (var x = 0; x < width; x++)
            {
                for (var p = 0; p < depth; p++)
                    if (((indices[y * width + x] >> p) & 1) != 0) row[p * rowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                if (masking == 1 && maskPlane![y * width + x]) row[depth * rowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
            if (!byteRun1) body.AddRange(row);
            else
            {
                for (var i = 0; i < row.Length;)
                {
                    var run = 1;
                    while (i + run < row.Length && run < 128 && row[i + run] == row[i]) run++;
                    if (run >= 2) { body.Add((byte)(257 - run)); body.Add(row[i]); i += run; continue; }
                    var lit = 1;
                    while (i + lit < row.Length && lit < 128 && !(i + lit + 1 < row.Length && row[i + lit] == row[i + lit + 1])) lit++;
                    body.Add((byte)(lit - 1));
                    for (var k = 0; k < lit; k++) body.Add(row[i + k]);
                    i += lit;
                }
            }
        }
        var ms = new MemoryStream();
        void Raw(string t) { foreach (var ch in t) ms.WriteByte((byte)ch); }
        void B32(int v) { ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void Chunk(string t, byte[] data) { Raw(t); B32(data.Length); ms.Write(data); if ((data.Length & 1) != 0) ms.WriteByte(0); }
        var bmhd = new byte[20];
        bmhd[0] = (byte)(width >> 8); bmhd[1] = (byte)width; bmhd[2] = (byte)(height >> 8); bmhd[3] = (byte)height;
        bmhd[8] = (byte)depth; bmhd[9] = (byte)masking; bmhd[10] = (byte)(byteRun1 ? 1 : 0);
        bmhd[12] = (byte)((transparentIndex ?? 0) >> 8); bmhd[13] = (byte)(transparentIndex ?? 0);
        bmhd[14] = 10; bmhd[15] = 11; bmhd[16] = (byte)(width >> 8); bmhd[17] = (byte)width; bmhd[18] = (byte)(height >> 8); bmhd[19] = (byte)height;
        Raw("FORM"); B32(0); Raw("ILBM");
        Chunk("BMHD", bmhd);
        Chunk("CMAP", palette);
        if (camg != 0) Chunk("CAMG", new[] { (byte)(camg >> 24), (byte)(camg >> 16), (byte)(camg >> 8), (byte)camg });
        Chunk("BODY", body.ToArray());
        var bytes = ms.ToArray();
        var size = bytes.Length - 8;
        bytes[4] = (byte)(size >> 24); bytes[5] = (byte)(size >> 16); bytes[6] = (byte)(size >> 8); bytes[7] = (byte)size;
        return bytes;
    }
}

/// <summary>Reads a PNG or an IFF ILBM picture, told apart by the first bytes (not the extension).</summary>
public static class ImageFile
{
    public static RgbaImage Read(byte[] data) => Ilbm.Looks(data) ? Ilbm.Read(data) : Png.Read(data);
}
