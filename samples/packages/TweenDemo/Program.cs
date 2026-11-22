using ESharp;
using ESharp.Platform;
using Tween;

namespace TweenDemo;

/// <summary>The game's own Ease: the same simple name as the package's Tween.Ease, in another namespace (prefixed C names).</summary>
public static class Ease
{
    public static int Bounces;

    public static int Twice(int v) => v * 2;
}

/// <summary>
/// Uses the E# library ESharpSample.Tween from a package: easing curves, the generic Tween struct (with int and with the library's
/// Point), a generic method, fixed point, and the library's assets (a masked ball sprite and a blip sound from its assets.json),
/// drawn along a tween on a screen. Every check prints "ok NAME" or "FAIL NAME"; esharp test compares the host and Amiga runs.
/// Prints "SHOT" while the screen shows the ball's path.
/// </summary>
public static class Program
{
    static int failures;

    static void Check(bool ok, string name)
    {
        if (!ok) failures++;
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static void Value(string name, int v) { Dos.PutStr($"{name} {v}\n"); }

    [EntryPoint]
    public static int Main()
    {
        Dos.PutStr("Tween demo: an E# library from a package\n");

        int half = Tween.Ease.One / 2;
        Check(Tween.Ease.Linear(half) == 2048 && Tween.Ease.Linear(-5) == 0 && Tween.Ease.Linear(9999) == Tween.Ease.One, "linear, clamped");
        Check(Tween.Ease.InQuad(half) == 1024 && Tween.Ease.OutQuad(half) == 3072, "quadratic in and out at the middle");
        Check(Tween.Ease.InOutQuad(half) == 2048 && Tween.Ease.InOutQuad(1024) == 512 && Tween.Ease.InOutQuad(3072) == 3584, "in-out quadratic");
        Check(Tween.Ease.SmoothStep(0) == 0 && Tween.Ease.SmoothStep(half) == 2048 && Tween.Ease.SmoothStep(Tween.Ease.One) == Tween.Ease.One, "smoothstep ends and middle");
        Check(Tween.Ease.Apply(Curve.OutQuad, 1024) == Tween.Ease.OutQuad(1024), "curve as a value");

        // The generic struct with int: 0 to 100 over 8 frames, out-quad.
        var x = new Tween<int>(0, 100, 8, Curve.OutQuad);
        int frames = 0, last = -1;
        bool rising = true;
        while (x.Step())
        {
            frames++;
            int v = Points.IntAt(x);
            if (v < last) rising = false;
            last = v;
        }
        Check(frames == 8 && last == 100 && rising && x.Done(), "tween of int reaches its end");
        x.Restart(true);
        x.Step();
        Value("after one frame back", Points.IntAt(x));
        Check(x.From == 100 && x.To == 0 && Points.IntAt(x) == 76, "tween reversed");

        // The generic struct with the library's Point: a sprite path.
        var path = new Tween<Point>(new Point(16, 200), new Point(304, 40), 16, Curve.SmoothStep);
        for (int i = 0; i < 8; i++) path.Step();
        var mid = Points.At(path);
        Value("sprite x at the middle", mid.X);
        Value("sprite y at the middle", mid.Y);
        Check(mid.X == 160 && mid.Y == 120, "tween of a point, halfway");
        while (path.Step()) { }
        var end = Points.At(path);
        Check(end.X == 304 && end.Y == 40, "tween of a point, at the end");

        Check(Lerp.Pick(false, new Point(1, 2), new Point(3, 4)).Y == 4 && Lerp.Pick(true, 7, 8) == 7, "generic method of the package");
        Fix f = Lerp.Fixed(Fix.FromInt(10), Fix.FromInt(20), Tween.Ease.One / 4);
        Check(Fix.ToInt(f) == 12 && Fix.Frac(f) == 32768, "fixed point blend");

        Ease.Bounces = Ease.Twice(21);
        Check(Ease.Bounces == 42, "own Ease next to the package's");

        // The library's assets (Tween.Art, generated from its assets.json and shipped in the package).
        Check(Art.BallWidth == 16 && Art.BallHeight == 16 && Art.BallDepth == 2 && Art.BallPalette.Length == 4 &&
              Art.BallPlanes.Length == 2 * 2 * 16 && Art.BallMask.Length == 2 * 16, "package sprite: size, depth and palette");
        Check(!Ball.Opaque(0, 0) && !Ball.Opaque(15, 15) && Ball.Opaque(8, 8) && Ball.Opaque(1, 8) && Art.BallPalette[0] == 0, "package sprite: the mask is the circle");
        Check(Art.BlipLength == 480 && Art.BlipRate == 8000 && Art.BlipData.Length == 480 && (sbyte)Art.BlipData[0] > 80 && (sbyte)Art.BlipData[479] > -4, "package sound: length, rate and data");

        if (Sound.Available && Sound.Open())
        {
            int voice = Ball.PlayBlip(40);
            Check(voice >= 0, "package sound plays");
            Sound.Stop();
            Sound.Close();
        }
        else
        {
#if AMIGAOS3
            Check(false, "package sound plays");   // Paula and audio.device are always there on a classic Amiga
#else
            Dos.PutStr("skip package sound plays\n");
#endif
        }

        // The ball along a tween on the screen: dots for the path, the ball at five points of it.
        bool opened = Video.Open(320, 200, 3);
        Check(opened, "screen opens");
        if (opened)
        {
            Ball.SetPalette();
            Video.SetColor(4, 15, 8, 0);
            Video.SetColor(5, 6, 6, 6);
            Video.FillRect(0, 0, 320, 200, 0);
            Video.FillRect(0, 186, 320, 14, 5);
            var arc = new Tween<Point>(new Point(24, 170), new Point(296, 30), 32, Curve.InOutQuad);
            while (arc.Step())
            {
                var p = Points.At(arc);
                Video.FillRect(p.X - 1, p.Y - 1, 2, 2, 4);
            }
            arc.Restart(false);
            Point at = default;
            for (int i = 0; i <= 4; i++)
            {
                while (arc.Frame < i * 8) arc.Step();
                at = Points.At(arc);
                Ball.DrawAt(at);
            }
            int left = at.X - Ball.Size / 2, top = at.Y - Ball.Size / 2;
            Check(Video.GetPixel(left + 8, top + 8) != 0 && Video.GetPixel(left + 8, top + 8) == Ball.ColorAt(8, 8) &&
                  Video.GetPixel(left, top) == 0 && Video.GetPixel(left + 15, top + 15) == 0, "package sprite on the screen, masked");
            Font.Draw("TWEEN + PACKAGE ART", 8, 189, 3, 1);
            Dos.PutStr("SHOT\n");
            Dos.FlushOutput();
            Video.WaitFrames(150);
            Video.Close();
        }

        Dos.PutStr(failures == 0 ? "all checks passed\n" : "some checks failed\n");
        return failures == 0 ? 0 : 5;
    }
}
