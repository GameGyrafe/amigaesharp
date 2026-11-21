using ESharp;

namespace Tween;

/// <summary>
/// Easing curves on fixed-point time and value: 0 is the start, <see cref="One"/> (4096) the end. Integer maths only, so they
/// are cheap on a 68020 without an FPU. Times outside 0..One are clamped.
/// </summary>
public static class Ease
{
    /// <summary>1.0 in the curves' fixed point (12 fraction bits).</summary>
    public const int One = 4096;

    public static int Linear(int t) => Clamp(t);

    public static int InQuad(int t)
    {
        t = Clamp(t);
        return (t * t) >> 12;
    }

    public static int OutQuad(int t)
    {
        int u = One - Clamp(t);
        return One - ((u * u) >> 12);
    }

    public static int InOutQuad(int t)
    {
        t = Clamp(t);
        if (t < One / 2) return (2 * t * t) >> 12;
        int u = One - t;
        return One - ((2 * u * u) >> 12);
    }

    /// <summary>3t^2 - 2t^3: starts and ends with zero speed.</summary>
    public static int SmoothStep(int t)
    {
        t = Clamp(t);
        int t2 = (t * t) >> 12;
        return (t2 * (3 * One - 2 * t)) >> 12;
    }

    /// <summary>The curve <paramref name="curve"/> at <paramref name="t"/>.</summary>
    public static int Apply(Curve curve, int t)
    {
        switch (curve)
        {
            case Curve.InQuad: return InQuad(t);
            case Curve.OutQuad: return OutQuad(t);
            case Curve.InOutQuad: return InOutQuad(t);
            case Curve.SmoothStep: return SmoothStep(t);
            default: return Linear(t);
        }
    }

    // Internal: a program cannot call it (one assembly in the Amiga build, but two in the dotnet build; esharp reports ES0116).
    internal static int Clamp(int t) => t < 0 ? 0 : t > One ? One : t;
}

/// <summary>The curves of <see cref="Ease"/>, as a value.</summary>
public enum Curve
{
    Linear,
    InQuad,
    OutQuad,
    InOutQuad,
    SmoothStep,
}
