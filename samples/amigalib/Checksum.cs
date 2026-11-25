using ESharp;

namespace AmigaLibSample;

/// <summary>What a caller gets back through a pointer to a struct (the binding copies the struct's fields for the program).</summary>
public struct Stats
{
    public int Calls;
    public int Bytes;
    public uint Last;
}

/// <summary>Checksums of the caller's memory, and statistics per opener.</summary>
public static unsafe class Checksum
{
    private static int _calls;
    private static int _bytes;
    private static uint _last;

    /// <summary>Adler-32 of <paramref name="length"/> bytes at <paramref name="data"/> (a0, d0).</summary>
    [LibraryFunction]
    public static uint Adler32(byte* data, int length)
    {
        uint a = 1, b = 0;
        for (int i = 0; i < length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        _calls++;
        _bytes += length;
        _last = (b << 16) | a;
        return _last;
    }

    /// <summary>The length of a string (a0).</summary>
    [LibraryFunction]
    public static int Length(string text)
    {
        if (text == null) return -1;
        return text.Length;
    }

    /// <summary>Fills <paramref name="stats"/> with this opener's figures.</summary>
    [LibraryFunction]
    public static void GetStats(Stats* stats)
    {
        stats->Calls = _calls;
        stats->Bytes = _bytes;
        stats->Last = _last;
    }

    /// <summary>The library's own name, from its data (a string result).</summary>
    [LibraryFunction]
    public static string Name() { return "amigalib"; }
}
