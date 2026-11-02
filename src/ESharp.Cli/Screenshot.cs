using System;
using System.Diagnostics;
using System.IO;
using ESharp.Assets;

namespace ESharp.Cli;

/// <summary>Captures an X display with <c>xwd</c> and saves it as a PNG.</summary>
internal static class Screenshot
{
    public static bool Capture(string? display, string pngPath)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "esharp-" + Guid.NewGuid().ToString("N") + ".xwd");
        try
        {
            var psi = new ProcessStartInfo("xwd") { UseShellExecute = false, RedirectStandardError = true };
            psi.ArgumentList.Add("-root");
            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add("-out");
            psi.ArgumentList.Add(tmp);
            if (display != null) { psi.ArgumentList.Add("-display"); psi.ArgumentList.Add(display); }
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode != 0 || !File.Exists(tmp)) return false;
            var image = Xwd.Read(File.ReadAllBytes(tmp));
            File.WriteAllBytes(pngPath, Png.Write(image));
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }
}

/// <summary>Reads 24/32-bit ZPixmap images written by xwd.</summary>
internal static class Xwd
{
    public static RgbaImage Read(byte[] d)
    {
        int U(int i) => (d[i * 4] << 24) | (d[i * 4 + 1] << 16) | (d[i * 4 + 2] << 8) | d[i * 4 + 3];
        var headerSize = U(0);
        var width = U(4);
        var height = U(5);
        var byteOrder = U(7);          // 0 = LSBFirst
        var bitsPerPixel = U(11);
        var bytesPerLine = U(12);
        var redMask = (uint)U(14);
        var greenMask = (uint)U(15);
        var blueMask = (uint)U(16);
        var colors = U(19);
        if (bitsPerPixel != 32 && bitsPerPixel != 24) throw new NotSupportedException($"xwd with {bitsPerPixel} bits per pixel");
        var pixels = headerSize + colors * 12;
        var bytesPerPixel = bitsPerPixel / 8;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var o = pixels + y * bytesPerLine + x * bytesPerPixel;
                uint v = 0;
                for (var b = 0; b < bytesPerPixel; b++)
                    v |= (uint)d[o + b] << (byteOrder == 0 ? 8 * b : 8 * (bytesPerPixel - 1 - b));
                var i = (y * width + x) * 4;
                rgba[i] = Channel(v, redMask);
                rgba[i + 1] = Channel(v, greenMask);
                rgba[i + 2] = Channel(v, blueMask);
                rgba[i + 3] = 255;
            }
        return new RgbaImage(width, height, rgba);
    }

    private static byte Channel(uint v, uint mask)
    {
        if (mask == 0) return 0;
        var shift = 0;
        while (((mask >> shift) & 1) == 0) shift++;
        var max = mask >> shift;
        return (byte)(((v & mask) >> shift) * 255 / max);
    }
}
