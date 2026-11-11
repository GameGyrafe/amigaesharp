using System;
using ESharp;
using ESharp.Platform;

namespace AssetsSample;

/// <summary>
/// Dithered images, an IFF ILBM picture and an embedded sound, all made by `esharp assets` (assets.json; the inputs are
/// made by tools/AssetSamples). Checks the pixels, palette, mask and sample, plays the sample, and draws three
/// versions of one gradient (Floyd-Steinberg, ordered, flat) and the badge. Prints "ok NAME" or "FAIL NAME", then "SHOT".
/// </summary>
public static unsafe class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
        Dos.FlushOutput();
    }

    /// <summary>The palette index of a pixel, decoded from planar data (rows padded to 16 pixels).</summary>
    static int Index(ReadOnlySpan<byte> planes, int width, int height, int depth, int x, int y)
    {
        int bytesPerRow = (width + 15) / 16 * 2;
        int value = 0;
        for (int plane = 0; plane < depth; plane++)
        {
            int offset = plane * bytesPerRow * height + y * bytesPerRow + (x >> 3);
            if ((planes[offset] & (0x80 >> (x & 7))) != 0) value |= 1 << plane;
        }
        return value;
    }

    /// <summary>How many times the index changes along row <paramref name="y"/>, and how many different indices it uses.</summary>
    static int Changes(ReadOnlySpan<byte> planes, int width, int height, int depth, int y, out int used)
    {
        int changes = 0;
        int seen = 0;
        int last = -1;
        for (int x = 0; x < width; x++)
        {
            int i = Index(planes, width, height, depth, x, y);
            seen |= 1 << i;
            if (last >= 0 && i != last) changes++;
            last = i;
        }
        used = 0;
        for (int b = 0; b < 8; b++) if ((seen & (1 << b)) != 0) used++;
        return changes;
    }

    static bool MaskBit(ReadOnlySpan<byte> mask, int width, int x, int y)
    {
        int bytesPerRow = (width + 15) / 16 * 2;
        return (mask[y * bytesPerRow + (x >> 3)] & (0x80 >> (x & 7))) != 0;
    }

    [EntryPoint]
    public static void Main()
    {
        // Dithering: the flat version has one index per band, the dithered ones mix neighbouring indices.
        int usedFs, usedOrdered, usedFlat;
        int changesFs = Changes(Assets.FsPlanes, Assets.FsWidth, Assets.FsHeight, Assets.FsDepth, 10, out usedFs);
        int changesOrdered = Changes(Assets.OrderedPlanes, Assets.OrderedWidth, Assets.OrderedHeight, Assets.OrderedDepth, 10, out usedOrdered);
        int changesFlat = Changes(Assets.FlatPlanes, Assets.FlatWidth, Assets.FlatHeight, Assets.FlatDepth, 10, out usedFlat);
        Check(Assets.FsWidth == 96 && Assets.FsHeight == 32 && Assets.FsDepth == 3, "gradient size and depth");
        Check(changesFlat < usedFlat && usedFlat >= 4, "flat: one band per palette entry");
        Check(changesFs > 2 * changesFlat && usedFs >= usedFlat, "floyd-steinberg mixes indices inside the bands");
        Check(changesOrdered > 2 * changesFlat && usedOrdered >= usedFlat, "ordered dither mixes indices inside the bands");
        Check(Assets.FsPalette.Length == 8 && Assets.FsPalette[0] == Assets.FlatPalette[0] && Assets.FsPalette[7] == Assets.FlatPalette[7], "dithering keeps the palette");

        // ILBM: 37x24, 3 planes, ByteRun1, mask plane; the colours are the ones the file names.
        Check(Assets.BadgeWidth == 37 && Assets.BadgeHeight == 24 && Assets.BadgeDepth == 3, "ilbm: size (an odd width) and depth");
        Check(!MaskBit(Assets.BadgeMask, Assets.BadgeWidth, 0, 0) && MaskBit(Assets.BadgeMask, Assets.BadgeWidth, 18, 12) && MaskBit(Assets.BadgeMask, Assets.BadgeWidth, 36, 12), "ilbm: the corner is masked out, the middle and the last column are not");
        int c0 = Index(Assets.BadgePlanes, Assets.BadgeWidth, Assets.BadgeHeight, Assets.BadgeDepth, 18, 12);
        int c1 = Index(Assets.BadgePlanes, Assets.BadgeWidth, Assets.BadgeHeight, Assets.BadgeDepth, 36, 12);
        int c2 = Index(Assets.BadgePlanes, Assets.BadgeWidth, Assets.BadgeHeight, Assets.BadgeDepth, 4, 8);
        Check(Assets.BadgePalette[c0] == 0xF00, "ilbm: pixel (18,12) is red");
        Check(Assets.BadgePalette[c1] == 0x0FF, "ilbm: pixel (36,12) is cyan");
        Check(Assets.BadgePalette[c2] == 0xFF0, "ilbm: pixel (4,8) is yellow");

        // Sample: 8-bit signed mono, 1600 bytes at 8000 Hz, a 440 Hz sine.
        Check(Assets.BeepLength == 1600 && Assets.BeepRate == 8000 && Assets.BeepData.Length == 1600, "sample: length and rate");
        Check((sbyte)Assets.BeepData[0] == 0 && (sbyte)Assets.BeepData[4] == 98 && (sbyte)Assets.BeepData[13] < -90, "sample: sine values as signed bytes");

        if (Sound.Available && Sound.Open())
        {
            Sample beep = default;
            fixed (byte* data = Assets.BeepData)
            {
                beep.Data = data;
                beep.Length = Assets.BeepLength;
                beep.Rate = Assets.BeepRate;
                int voice = Sound.Play(in beep, Sound.PeriodForRate(beep.Rate), 40, 1);
                Check(voice >= 0 && Sound.Busy(voice), "sample: plays, voice is busy");
                Sound.Stop();
                Check(voice >= 0 && !Sound.Busy(voice), "sample: Stop silences it");
            }
            Sound.Close();
        }
        else
        {
#if AMIGAOS3
            Check(false, "audio.device opens");             // Paula and audio.device are always there on a classic Amiga
#else
            Dos.PutStr("note: no working sound hardware here (audio.device did not open), so the sample is not played\n");
            Dos.PutStr("skip sample: plays, voice is busy\nskip sample: Stop silences it\n");
#endif
        }

        // Show it: 16 colours, entries 0..7 the gradient's, 8..15 the badge's.
        bool opened = Video.Open(320, 200, 4);
        Check(opened, "screen opens");
        if (!opened) return;
        Video.SetPalette(Assets.FsPalette);
        for (int i = 0; i < 8; i++)
        {
            int rgb = Assets.BadgePalette[i];
            Video.SetColor(8 + i, (rgb >> 8) & 15, (rgb >> 4) & 15, rgb & 15);
        }
        Video.FillRect(0, 0, 320, 200, 0);
        Video.Blit(Assets.FsPlanes, Assets.FsWidth, Assets.FsHeight, Assets.FsDepth, 10, 10);
        Video.Blit(Assets.OrderedPlanes, Assets.OrderedWidth, Assets.OrderedHeight, Assets.OrderedDepth, 10, 50);
        Video.Blit(Assets.FlatPlanes, Assets.FlatWidth, Assets.FlatHeight, Assets.FlatDepth, 10, 90);
        int bx = 130;
        int by = 10;
        for (int y = 0; y < Assets.BadgeHeight; y++)
            for (int x = 0; x < Assets.BadgeWidth; x++)
                if (MaskBit(Assets.BadgeMask, Assets.BadgeWidth, x, y))
                    Video.FillRect(bx + x * 4, by + y * 4, 4, 4, 8 + Index(Assets.BadgePlanes, Assets.BadgeWidth, Assets.BadgeHeight, Assets.BadgeDepth, x, y));
        Check(Video.GetPixel(10, 10) == Index(Assets.FsPlanes, 96, 32, 3, 0, 0) && Video.GetPixel(bx, by) == 0 && Video.GetPixel(bx + 18 * 4, by + 12 * 4) == 8 + c0, "screen shows the assets");

        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Video.WaitFrames(150);
        Video.Close();
        Check(!Video.IsOpen(), "screen closes");
    }
}
