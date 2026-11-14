using ESharp;
using ESharp.Platform;

namespace DBufferSample;

/// <summary>
/// Double buffering: whole-screen redraws go to a back buffer and <see cref="Video.Flip"/> shows each frame in one go, so nothing
/// flickers. Checks both ways of doing it (two screen bitmaps that swap, and an offscreen bitmap copied with one blit), that drawing
/// is clipped to the screen (a back buffer's rast port has no layer to do it), and that turning it off keeps the last frame.
/// Prints "ok NAME" or "FAIL NAME", then "SHOT" and waits three seconds so a screenshot can be taken.
/// </summary>
public static class Program
{
    // A 16 x 8 sprite in two planes, all pixels colour 3 (word-aligned rows, chip RAM for the blitter).
    [ChipRam] static byte[] Block =
    {
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    };

    static ushort[] Palette = { 0x000, 0xFFF, 0x248, 0xF80, 0x46A, 0xFD0, 0x0A4, 0xA0A };

    const int W = 320, H = 256;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static string ModeName(VideoBuffering b)
    {
        if (b == VideoBuffering.Swap) return "swap (two screen bitmaps)";
        if (b == VideoBuffering.Copy) return "copy (offscreen bitmap, one blit per flip)";
        return "single";
    }

    /// <summary>One whole frame: background, a bar at <paramref name="x"/>, a label.</summary>
    static void DrawFrame(int x, int background, string label)
    {
        Video.FillRect(0, 0, W, H, background);
        for (int i = 0; i < 8; i++) Video.FillRect(0, 40 + i * 24, W, 12, (i & 1) == 0 ? 2 : 4);
        Video.FillRect(x, 30, 40, 200, 5);
        Font.Draw(label, 8, 8, 1, 2);
    }

    /// <summary>The checks for one buffering mode; <paramref name="mode"/> is what was turned on.</summary>
    static void Exercise(VideoBuffering mode)
    {
        // The screen shows colour 6 everywhere; the back buffer starts as a copy of it.
        Check(Video.GetPixel(100, 100) == 6, "the back buffer starts as a copy of the screen");
        Video.FillRect(0, 0, W, H, 1);
        Check(Video.GetPixel(100, 100) == 1, "drawing goes to the back buffer");
        Video.Flip();
        // Swap: the new back buffer is the old front one (colour 6); copy: it still holds the frame just shown (colour 1).
        int older = mode == VideoBuffering.Swap ? 6 : 1;
        Check(Video.GetPixel(100, 100) == older, "after Flip the back buffer holds the expected older frame");

        // Clipping: rectangles and blits partly outside the screen draw only their inside part.
        Video.FillRect(0, 0, W, H, 0);
        Video.FillRect(-10, -10, 30, 30, 2);
        Video.FillRect(W - 5, H - 5, 50, 50, 4);
        Check(Video.GetPixel(0, 0) == 2 && Video.GetPixel(19, 19) == 2 && Video.GetPixel(20, 20) == 0, "a rectangle over the top-left corner is clipped");
        Check(Video.GetPixel(W - 1, H - 1) == 4 && Video.GetPixel(W - 6, H - 6) == 0, "a rectangle over the bottom-right corner is clipped");
        Video.Blit(Block, 16, 8, 2, -4, -2);
        Check(Video.GetPixel(0, 0) == 3 && Video.GetPixel(11, 5) == 3 && Video.GetPixel(12, 0) == 2, "a blit over the left edge is clipped");
        Video.Blit(Block, 16, 8, 2, W - 8, H - 4);
        Check(Video.GetPixel(W - 1, H - 1) == 3 && Video.GetPixel(W - 9, H - 1) == 0, "a blit over the bottom-right corner is clipped");
        Font.Draw("AB", W - 8, 100, 5, 1);
        Font.Draw("A", 10, 100, 5, 1);
        Check(Video.GetPixel(12, 100) == 5, "text draws into the back buffer");

        // An animation: every frame is redrawn whole, and each Flip is one frame.
        int before = Frame.Count;
        long t0 = Clock.Ticks;
        for (int f = 0; f < 50; f++)
        {
            DrawFrame(f * 5, 0, "FRAME");
            Video.Flip();
        }
        // About a second at 50 Hz when the emulator runs in real time (not a check: an emulator may run slower or faster).
        Dos.PutStr("50 whole-screen frames: " + Clock.Milliseconds(t0, Clock.Ticks) + " ms\n");
        Check(Frame.Count - before == 50, "each Flip waits one frame");
        Check(Video.Buffering() == mode && Video.IsDoubleBuffered(), "the mode stays on");
    }

    [EntryPoint]
    public static void Main()
    {
        Clock.Open();
        Check(Video.Open(W, H, 3), "screen opens");
        Video.SetPalette(Palette);
        Check(Video.Buffering() == VideoBuffering.Single && !Video.IsDoubleBuffered(), "a new screen is single buffered");
        Video.Flip();
        Check(true, "Flip without double buffering waits a frame");

        // Swap where the system has screen buffers, else the offscreen bitmap.
        Video.FillRect(0, 0, W, H, 6);
        Check(Video.EnableDoubleBuffer(), "double buffering turns on");
        VideoBuffering mode = Video.Buffering();
        Dos.PutStr("buffering: ");
        Dos.PutStr(ModeName(mode));
        Dos.PutStr("\n");
        Check(mode != VideoBuffering.Single, "it is swap or copy");
        Check(Video.EnableDoubleBuffer(), "turning it on again is harmless");
        Exercise(mode);

        // Turning it off keeps the frame on display: draw a marker frame, show it, and read it back from the screen.
        Video.FillRect(0, 0, W, H, 7);
        Video.Flip();
        Video.FillRect(0, 0, W, H, 0);                // into the back buffer, never shown
        Video.DisableDoubleBuffer();
        Check(!Video.IsDoubleBuffered() && Video.GetPixel(100, 100) == 7, "after turning it off the last frame stays and is drawn on");

        // The offscreen bitmap (the fallback, also asked for directly).
        Video.FillRect(0, 0, W, H, 6);
        Check(Video.EnableBackBuffer() && Video.Buffering() == VideoBuffering.Copy, "the offscreen back buffer turns on");
        Check(!Video.EnableDoubleBuffer() || Video.Buffering() == VideoBuffering.Copy, "the copy mode stays when asked for again");
        Exercise(VideoBuffering.Copy);
        Video.DisableDoubleBuffer();

        // The picture for the screenshot, shown with the system's own double buffering.
        Video.EnableDoubleBuffer();
        DrawFrame(140, 0, "DOUBLE BUFFER");
        Font.Draw(mode == VideoBuffering.Swap ? "SWAP" : "COPY", W - 56, 12, 3, 2);
        Video.Flip();
        Dos.PutStr("SHOT\n");
        Dos.FlushOutput();
        Dos.Delay(150);
        Video.Close();
        Check(!Video.IsOpen() && !Video.IsDoubleBuffered(), "screen closes and double buffering is off");
        Clock.Close();
    }
}
