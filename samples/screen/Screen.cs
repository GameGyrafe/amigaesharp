using System;
using ESharp;
using ESharp.Platform;

namespace ScreenSample;

/// <summary>
/// Phase 3: open a screen, draw rectangles with the blitter, show a PNG converted by `esharp assets`
/// (assets.json, card.png -> Assets.g.cs) and read the pixels back. Prints "ok NAME" or "FAIL NAME",
/// then "SHOT" and waits a few seconds so a screenshot can be taken.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    /// <summary>The palette index of a pixel of the card, decoded from the planar data in <see cref="Assets.CardPlanes"/>.</summary>
    static int CardIndex(int x, int y)
    {
        int bytesPerRow = (Assets.CardWidth + 15) / 16 * 2;
        int value = 0;
        for (int plane = 0; plane < Assets.CardDepth; plane++)
        {
            int offset = plane * bytesPerRow * Assets.CardHeight + y * bytesPerRow + (x >> 3);
            if ((Assets.CardPlanes[offset] & (0x80 >> (x & 7))) != 0) value |= 1 << plane;
        }
        return value;
    }

    [EntryPoint]
    public static void Main()
    {
        bool opened = Video.Open(320, 200, 4);
        Check(opened, "screen opens");
        if (!opened) return;

        // Entries 0..7 belong to the card, 8..11 to the rectangles.
        Video.SetPalette(Assets.CardPalette);
        Video.SetColor(8, 15, 0, 0);
        Video.SetColor(9, 0, 15, 0);
        Video.SetColor(10, 15, 15, 0);
        Video.SetColor(11, 0, 0, 4);
        Video.FillRect(0, 0, 320, 200, 11);
        Video.FillRect(20, 20, 100, 60, 8);
        Video.FillRect(60, 50, 100, 60, 9);
        Video.FillRect(200, 120, 80, 40, 10);

        Check(Video.GetPixel(2, 2) == 11, "background pixel");
        Check(Video.GetPixel(30, 30) == 8, "red rectangle");
        Check(Video.GetPixel(70, 60) == 9 && Video.GetPixel(100, 70) == 9, "green rectangle on top of red");
        Check(Video.GetPixel(210, 130) == 10, "yellow rectangle");
        Check(Video.GetPixel(119, 25) == 8 && Video.GetPixel(120, 25) == 11, "rectangle edges are exact");
        Check(Video.GetPixel(-1, 0) == -1 && Video.GetPixel(0, 200) == -1, "outside the screen");

        Check(Assets.CardWidth == 64 && Assets.CardHeight == 88 && Assets.CardPalette.Length == 1 << Assets.CardDepth, "asset constants");

        int cx = 220;
        int cy = 20;
        Video.Blit(Assets.CardPlanes, Assets.CardWidth, Assets.CardHeight, Assets.CardDepth, cx, cy);
        int wrong = 0;
        for (int y = 0; y < Assets.CardHeight; y++)
            for (int x = 0; x < Assets.CardWidth; x++)
                if (Video.GetPixel(cx + x, cy + y) != CardIndex(x, y)) wrong++;
        Check(wrong == 0, "blitted card matches the planar data pixel for pixel");
        Check(Video.GetPixel(cx + 32, cy + 44) == CardIndex(32, 44) && CardIndex(32, 44) != 0, "card centre is not background");
        Check(Video.GetPixel(cx - 1, cy) == 11 && Video.GetPixel(cx + 64, cy) == 11, "blit does not spill outside the card");

        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Video.WaitFrames(150);
        Video.Close();
        Check(!Video.IsOpen(), "screen closes");
    }
}
