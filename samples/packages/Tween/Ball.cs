using ESharp;
using ESharp.Platform;

namespace Tween;

/// <summary>
/// The library's own sprite, a shaded ball (16x16, 2 bitplanes, masked), and a short blip sound. Both come from the library's
/// assets.json: `esharp assets` turned ball.png and blip.wav into <see cref="Art"/> (Assets.g.cs, embedded with the other sources),
/// so a game that uses the package gets them as [ChipRam] data on every target.
/// </summary>
public static class Ball
{
    public const int Size = Art.BallWidth;

    /// <summary>Sets palette entries 0..3 to the ball's colours (entry 0 is the background: the ball never draws it).</summary>
    public static void SetPalette()
    {
        for (int i = 0; i < Art.BallPalette.Length; i++)
        {
            int rgb = Art.BallPalette[i];
            Video.SetColor(i, (rgb >> 8) & 15, (rgb >> 4) & 15, rgb & 15);
        }
    }

    /// <summary>Draws the ball with its top-left corner at (x, y); pixels outside the circle are left as they are.</summary>
    public static void Draw(int x, int y)
    {
        Video.BlitMasked(Art.BallPlanes, Art.BallMask, Art.BallWidth, Art.BallHeight, Art.BallDepth, 0, 0, Art.BallWidth, Art.BallHeight, x, y);
    }

    /// <summary>Draws the ball centred on a point of a tween.</summary>
    public static void DrawAt(Point p)
    {
        Draw(p.X - Size / 2, p.Y - Size / 2);
    }

    /// <summary>Plays the blip once on a free voice (Sound must be open); the voice, or -1.</summary>
    public static unsafe int PlayBlip(int volume)
    {
        Sample blip = default;
        fixed (byte* data = Art.BlipData)
        {
            blip.Data = data;
            blip.Length = Art.BlipLength;
            blip.Rate = Art.BlipRate;
            return Sound.Play(in blip, Sound.PeriodForRate(blip.Rate), volume, 1);
        }
    }

    /// <summary>The palette index of pixel (x, y) of the sprite.</summary>
    public static int ColorAt(int x, int y)
    {
        int bytesPerRow = (Art.BallWidth + 15) / 16 * 2;
        int value = 0;
        for (int plane = 0; plane < Art.BallDepth; plane++)
            if ((Art.BallPlanes[plane * bytesPerRow * Art.BallHeight + y * bytesPerRow + (x >> 3)] & (0x80 >> (x & 7))) != 0) value |= 1 << plane;
        return value;
    }

    /// <summary>True when pixel (x, y) of the sprite is drawn (its mask bit is set).</summary>
    public static bool Opaque(int x, int y)
    {
        int bytesPerRow = (Art.BallWidth + 15) / 16 * 2;
        return (Art.BallMask[y * bytesPerRow + (x >> 3)] & (0x80 >> (x & 7))) != 0;
    }
}
