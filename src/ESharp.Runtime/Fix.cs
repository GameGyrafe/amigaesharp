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
    /// Fixed-point multiply built from four 16x16 partial products, so a 68000 uses only its native
    /// <c>muls.w</c>/<c>mulu.w</c> and never a 32-bit multiply helper or 64-bit arithmetic. The
    /// halves are multiplied as unsigned (the high product needs no correction because it is shifted
    /// left by 16) and the cross terms are corrected for the sign of the high halves. Everything is
    /// arithmetic modulo 2^32, so the result matches (a * b) >> 16 exactly.
    /// </summary>
    public static Fix operator *(Fix a, Fix b)
    {
        short ah = (short)(a.Raw >> 16);
        short bh = (short)(b.Raw >> 16);
        ushort al = (ushort)a.Raw;
        ushort bl = (ushort)b.Raw;
        uint high = (uint)((ushort)ah * (ushort)bh) << 16;
        uint mid = (uint)((ushort)ah * bl) + (uint)(al * (ushort)bh);
        uint fix = ((uint)(ah >> 15) & ((uint)bl << 16)) + ((uint)(bh >> 15) & ((uint)al << 16));
        uint low = (uint)(al * bl) >> 16;
        return FromRaw((int)(high + mid - fix + low));
    }

    public static Fix operator *(Fix a, int b) => FromRaw(a.Raw * b);
    public static Fix operator *(int a, Fix b) => FromRaw(a * b.Raw);

    /// <summary>Fixed-point divide. Uses a 64-bit intermediate, so it is slow on a 68000.</summary>
    public static Fix operator /(Fix a, Fix b) => FromRaw((int)(((long)a.Raw << 16) / b.Raw));

    public static Fix operator /(Fix a, int b) => FromRaw(a.Raw / b);

    public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
    public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
    public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
    public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
    public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
}
