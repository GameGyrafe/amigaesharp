using ESharp;

namespace Tween;

/// <summary>A point on the screen, in pixels.</summary>
public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>Blending of numbers: plain integers and 16.16 fixed point.</summary>
public static class Lerp
{
    /// <summary>a + (b - a) * t, with t in 0..Ease.One.</summary>
    public static int Int(int a, int b, int t) => a + (((b - a) * t) >> 12);

    /// <summary>The same for 16.16 fixed point (t in 0..Ease.One).</summary>
    public static Fix Fixed(Fix a, Fix b, int t) => a + (b - a) * Fix.FromRaw(t << 4);

    /// <summary>One of two values of any unmanaged type (a generic method, monomorphised for each type it is used with).</summary>
    public static T Pick<T>(bool first, T a, T b) where T : unmanaged => first ? a : b;
}

/// <summary>
/// A tween: a value that moves from <see cref="From"/> to <see cref="To"/> over <see cref="Frames"/> frames along a curve.
/// Generic over the value type (a generic struct, monomorphised for each type argument): <see cref="Points"/> blends the
/// ones of this library, and a game can write the same for its own structs.
/// </summary>
public struct Tween<T> where T : unmanaged
{
    public T From;
    public T To;
    public int Frames;
    public int Frame;
    public Curve Curve;

    public Tween(T from, T to, int frames, Curve curve)
    {
        From = from;
        To = to;
        Frames = frames < 1 ? 1 : frames;
        Frame = 0;
        Curve = curve;
    }

    /// <summary>The eased progress, 0..Ease.One.</summary>
    public int Progress() => Ease.Apply(Curve, Frame * Ease.One / Frames);

    /// <summary>Advances one frame; false once the tween is at its end.</summary>
    public bool Step()
    {
        if (Frame >= Frames) return false;
        Frame++;
        return true;
    }

    public bool Done() => Frame >= Frames;

    /// <summary>Back to the start, or to the start the other way round.</summary>
    public void Restart(bool reverse)
    {
        if (reverse)
        {
            T old = From;
            From = To;
            To = old;
        }
        Frame = 0;
    }
}

/// <summary>Blends points along a tween.</summary>
public static class Points
{
    public static Point At(Tween<Point> tween)
    {
        int t = tween.Progress();
        return new Point(Lerp.Int(tween.From.X, tween.To.X, t), Lerp.Int(tween.From.Y, tween.To.Y, t));
    }

    public static int IntAt(Tween<int> tween) => Lerp.Int(tween.From, tween.To, tween.Progress());
}
