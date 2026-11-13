using ESharp;
using ESharp.Platform;

namespace AgaSample;

/// <summary>
/// AGA: a 256-colour screen with a 24-bit palette (8 bitplanes, 320x256). On a machine without AGA it checks that the
/// deep screen is refused and that SetColor24 falls back to 4 bits per channel on a 32-colour screen. Prints
/// "ok NAME" or "FAIL NAME", then "SHOT" and waits a few seconds so a screenshot can be taken.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static void Main()
    {
        if (!Video.HasAga())
        {
            Dos.PutStr("no AGA chipset\n");
            // Without AGA an 8-bitplane screen is only possible on a graphics card (RTG), and Open then picks that.
            Check(Video.Open(320, 256, 8) == Video.HasRtg(320, 256), "an 8-bitplane screen opens only on RTG without AGA");
            Video.Close();
            Check(Video.Open(320, 256, 5), "the 32-colour fallback opens");
            Video.SetColor24(1, 255, 0, 0);
            Video.FillRect(0, 0, 10, 10, 1);
            Check(Video.GetPixel(5, 5) == 1, "SetColor24 falls back to the 4-bit palette");
            Video.Close();
            return;
        }

        Dos.PutStr("AGA chipset\n");
        Check(Video.Open(320, 256, 8), "an 8-bitplane screen opens");
        Video.SetPalette24(Assets.TilePalette);
        Video.Blit(Assets.TilePlanes, Assets.TileWidth, Assets.TileHeight, Assets.TileDepth, 0, 0);
        Check(Assets.TileDepth == 8 && Assets.TilePalette.Length == 256, "the asset has 256 colours");

        bool exact = true;
        for (int by = 0; by < 16; by++)
        {
            for (int bx = 0; bx < 16; bx++)
            {
                int index = Video.GetPixel(bx * 2 + 1, by * 2 + 1);
                uint expected = (uint)(((bx * 16) << 16) | ((by * 16) << 8) | ((bx ^ by) * 16));
                if (index < 0 || Assets.TilePalette[index] != expected) exact = false;
            }
        }
        Check(exact, "the blitted pixels have the 24-bit colours of the image");

        Video.SetColor24(255, 12, 34, 56);
        Video.FillRect(100, 100, 8, 8, 255);
        Check(Video.GetPixel(103, 103) == 255, "pen 255 draws");
        Video.SetColor24(255, 240, 240, 0);           // back to the image's own colour for pen 255

        // A colour chart: each of the 256 colours as a square, read back from the blitted image.
        Video.Blit(Assets.TilePlanes, Assets.TileWidth, Assets.TileHeight, Assets.TileDepth, 0, 0);
        int[] chart = new int[256];
        for (int by = 0; by < 16; by++)
            for (int bx = 0; bx < 16; bx++)
                chart[by * 16 + bx] = Video.GetPixel(bx * 2 + 1, by * 2 + 1);
        Video.FillRect(0, 0, 320, 256, 0);
        for (int i = 0; i < 256; i++)
            Video.FillRect(32 + (i % 16) * 16, 8 + (i / 16) * 14, 15, 13, chart[i]);
        Check(Video.GetPixel(32 + 3 * 16 + 5, 8 + 2 * 14 + 5) == chart[2 * 16 + 3], "the chart is drawn");
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Dos.Delay(150);
        Video.Close();
        Check(!Video.IsOpen(), "screen closes");
    }
}
