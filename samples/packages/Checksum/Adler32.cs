using System;
using ESharp;

namespace Checksum;

/// <summary>
/// The Adler-32 checksum (the one zlib uses: two running sums modulo 65521). On every E# target the work is done by the
/// library's C function cksum_adler32 (native/common/adler32.c); the dotnet host build, which has no C, runs the same sums in C#.
/// </summary>
public static unsafe class Adler32
{
    /// <summary>The checksum of no bytes; <see cref="Update"/> starts from it.</summary>
    public const uint Start = 1;

    /// <summary>The checksum of <paramref name="data"/> (an array, or a slice of one).</summary>
    public static uint Of(ReadOnlySpan<byte> data)
    {
        return Update(Start, data);
    }

    /// <summary>Continues <paramref name="sum"/> (a checksum so far, or <see cref="Start"/>) over <paramref name="data"/>.</summary>
    public static uint Update(uint sum, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return sum;
        fixed (byte* p = data)
        {
            return Run(sum, p, data.Length);
        }
    }

#if HOST
    private static uint Run(uint sum, byte* data, int length)
    {
        uint a = sum & 0xFFFF, b = sum >> 16;
        for (int i = 0; i < length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
#else
    /// <summary>The library's C function (native/common/adler32.c); its prototype is made from this declaration.</summary>
    [Native("cksum_adler32")]
    private static extern uint Run(uint sum, byte* data, int length);
#endif
}
