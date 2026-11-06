namespace ESharp;

/// <summary>
/// 16.16 signed fixed-point number: 16 integer bits, 16 fraction bits, stored in a 32-bit int.
/// Range is about -32768 to 32767.99998; resolution is 1/65536. Overflow wraps, as in unchecked C#.
/// Use this instead of float/double on a stock Amiga. The same source runs under dotnet and, as
/// plain C functions, on every Amiga target.
/// </summary>
public struct Fix
{
    public int Raw;

    /// <summary>The raw representation of 1.0.</summary>
    public const int OneRaw = 65536;

    public static Fix FromRaw(int raw)
    {
        Fix f = default;
        f.Raw = raw;
        return f;
    }

    /// <summary>Converts an integer in -32768..32767 to fixed point.</summary>
    public static Fix FromInt(int value) => FromRaw(value << 16);

    /// <summary>An integer in -32768..32767 converts implicitly, so <c>Fix f = 3;</c> and <c>f + 1</c> work.</summary>
    public static implicit operator Fix(int value) => FromRaw(value << 16);

    /// <summary>The integer part, rounding towards negative infinity.</summary>
    public static int ToInt(Fix f) => f.Raw >> 16;

    /// <summary>The fraction as 0..65535 (always non-negative, so -0.25 is -1 + 0.75).</summary>
    public static int Frac(Fix f) => f.Raw & 0xFFFF;

    public static Fix operator +(Fix a, Fix b) => FromRaw(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => FromRaw(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => FromRaw(-a.Raw);

    /// <summary>
    /// Fixed-point multiply, <c>(a * b) >> 16</c> through a 64-bit product. On the 68020 that is one
    /// <c>muls.l</c> with a 64-bit result (no helper call); the 68000-style four-partial-product version is gone with the 68000.
    /// </summary>
    public static Fix operator *(Fix a, Fix b) => FromRaw((int)(((long)a.Raw * b.Raw) >> 16));

    public static Fix operator *(Fix a, int b) => FromRaw(a.Raw * b);
    public static Fix operator *(int a, Fix b) => FromRaw(a * b.Raw);

    /// <summary>
    /// Fixed-point divide, <c>(a &lt;&lt; 16) / b</c>. On the 68020 it is one <c>divs.l</c> (64/32 divide) when the quotient fits in 32 bits, and
    /// the software 64-bit divide only when it does not. Division by zero gives 0 on the Amiga (a 68k would otherwise trap) and throws on the host.
    /// </summary>
    public static Fix operator /(Fix a, Fix b) => FromRaw(Intrinsics.es_fixdiv(a.Raw, b.Raw));

    public static Fix operator /(Fix a, int b) => FromRaw(a.Raw / b);

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
}

/// <summary>Operations the C prelude of the generated file implements with the best instruction for the CPU.</summary>
internal static class Intrinsics
{
#if AMIGA
    [Native]
    public static extern int es_fixdiv(int a, int b);
#else
    public static int es_fixdiv(int a, int b) => (int)(((long)a << 16) / b);
#endif
}
