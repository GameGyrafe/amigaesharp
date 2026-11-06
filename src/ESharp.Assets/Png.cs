using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace ESharp.Assets;

/// <summary>An image as 8-bit RGBA, four bytes per pixel, row after row.</summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }
}

/// <summary>
/// A small PNG reader and writer with no dependencies. Reads non-interlaced images of colour types
/// grey, RGB, palette, grey+alpha and RGBA at 8 bits (and grey/palette at 1, 2 and 4 bits).
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static RgbaImage Read(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("not a PNG file");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        var idat = new MemoryStream();
        for (var pos = 8; pos + 8 <= data.Length;)
        {
            var length = ReadInt(data, pos);
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = pos + 8;
            switch (type)
            {
                case "IHDR":
                    width = ReadInt(data, body);
                    height = ReadInt(data, body + 4);
                    bitDepth = data[body + 8];
                    colorType = data[body + 9];
                    interlace = data[body + 12];
                    break;
                case "PLTE":
                    palette = data.AsSpan(body, length).ToArray();
                    break;
                case "IDAT":
                    idat.Write(data, body, length);
                    break;
            }
            if (type == "IEND") break;
            pos = body + length + 4;
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG has no IHDR");
        if (interlace != 0) throw new NotSupportedException("interlaced PNG files are not supported; re-save without interlacing");
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"PNG colour type {colorType}") };
        if (bitDepth != 8 && !(colorType is 0 or 3 && bitDepth is 1 or 2 or 4))
            throw new NotSupportedException($"PNG with {bitDepth} bits per sample and colour type {colorType}");
        if (colorType == 3 && palette == null) throw new InvalidDataException("palette PNG without PLTE");

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var bitsPerPixel = channels * bitDepth;
        var stride = (width * bitsPerPixel + 7) / 8;
        var bpp = Math.Max(1, bitsPerPixel / 8);
        var raw = new byte[stride * height];
        var previous = new byte[stride];
        var line = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = z.ReadByte();
            if (filter < 0) throw new InvalidDataException("truncated PNG data");
            ReadFully(z, line);
            Unfilter(filter, line, previous, bpp);
            Array.Copy(line, 0, raw, y * stride, stride);
            (previous, line) = (line, previous);
        }

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                var row = y * stride;
                switch (colorType)
                {
                    case 0:
                    {
                        var v = Sample(raw, row, x, bitDepth);
                        var g = bitDepth == 8 ? v : v * 255 / ((1 << bitDepth) - 1);
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = (byte)g;
                        rgba[o + 3] = 255;
                        break;
                    }
                    case 2:
                        rgba[o] = raw[row + x * 3]; rgba[o + 1] = raw[row + x * 3 + 1]; rgba[o + 2] = raw[row + x * 3 + 2];
                        rgba[o + 3] = 255;
                        break;
                    case 3:
                    {
                        var i = Sample(raw, row, x, bitDepth);
                        rgba[o] = palette![i * 3]; rgba[o + 1] = palette[i * 3 + 1]; rgba[o + 2] = palette[i * 3 + 2];
                        rgba[o + 3] = 255;
                        break;
                    }
                    case 4:
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = raw[row + x * 2];
                        rgba[o + 3] = raw[row + x * 2 + 1];
                        break;
                    case 6:
                        Array.Copy(raw, row + x * 4, rgba, o, 4);
                        break;
                }
            }
        }
        return new RgbaImage(width, height, rgba);
    }

    public static byte[] Write(RgbaImage image)
    {
        var stride = image.Width * 4;
        var filtered = new byte[(stride + 1) * image.Height];
        for (var y = 0; y < image.Height; y++)
            Array.Copy(image.Rgba, y * stride, filtered, y * (stride + 1) + 1, stride);

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(filtered, 0, filtered.Length);

        var output = new MemoryStream();
        output.Write(Signature);
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, image.Width);
        WriteInt(ihdr, 4, image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(output, "IHDR", ihdr);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static int Sample(byte[] raw, int row, int x, int bitDepth)
    {
        if (bitDepth == 8) return raw[row + x];
        var perByte = 8 / bitDepth;
        var b = raw[row + x / perByte];
        var shift = 8 - bitDepth * (x % perByte + 1);
        return (b >> shift) & ((1 << bitDepth) - 1);
    }

    private static void Unfilter(int filter, byte[] line, byte[] prev, int bpp)
    {
        for (var i = 0; i < line.Length; i++)
        {
            int a = i >= bpp ? line[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            int add = filter switch
            {
                0 => 0,
                1 => a,
                2 => b,
                3 => (a + b) / 2,
                4 => Paeth(a, b, c),
                _ => throw new InvalidDataException($"bad PNG filter {filter}"),
            };
            line[i] = (byte)(line[i] + add);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void ReadFully(Stream s, byte[] buffer)
    {
        for (var n = 0; n < buffer.Length;)
        {
            var r = s.Read(buffer, n, buffer.Length - n);
            if (r <= 0) throw new InvalidDataException("truncated PNG data");
            n += r;
        }
    }

    private static int ReadInt(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

    private static void WriteInt(byte[] d, int p, int v)
    {
        d[p] = (byte)(v >> 24); d[p + 1] = (byte)(v >> 16); d[p + 2] = (byte)(v >> 8); d[p + 3] = (byte)v;
    }

    private static void Chunk(Stream output, string type, byte[] body)
    {
        var header = new byte[4];
        WriteInt(header, 0, body.Length);
        output.Write(header);
        var typed = new byte[4 + body.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typed);
        Array.Copy(body, 0, typed, 4, body.Length);
        output.Write(typed);
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(typed));
        output.Write(crc);
    }

    private static uint[]? _table;

    private static uint Crc32(byte[] data)
    {
        if (_table == null)
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            _table = t;
        }
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
