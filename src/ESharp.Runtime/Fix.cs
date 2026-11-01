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

    /// <summary>The integer part, rounding towards negative infinity.</summary>
    public static int ToInt(Fix f) => f.Raw >> 16;

    /// <summary>The fraction as 0..65535 (always non-negative, so -0.25 is -1 + 0.75).</summary>
    public static int Frac(Fix f) => f.Raw & 0xFFFF;

    public static Fix operator +(Fix a, Fix b) => FromRaw(a.Raw + b.Raw);
    public static Fix operator -(Fix a, Fix b) => FromRaw(a.Raw - b.Raw);
    public static Fix operator -(Fix a) => FromRaw(-a.Raw);

    /// <summary>
    /// Fixed-point multiply built from 16x16 partial products, so a 68000 needs only its native
    /// 16x16 multiply instructions and never a 64-bit helper.
    /// </summary>
    public static Fix operator *(Fix a, Fix b)
    {
        int ah = a.Raw >> 16;
        int bh = b.Raw >> 16;
        ushort al = (ushort)(a.Raw & 0xFFFF);
        ushort bl = (ushort)(b.Raw & 0xFFFF);
        uint high = (uint)((short)ah * (short)bh) << 16;
        uint mid = (uint)((short)ah * bl) + (uint)(al * (short)bh);
        uint low = (uint)(al * bl) >> 16;
        return FromRaw((int)(high + mid + low));
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
