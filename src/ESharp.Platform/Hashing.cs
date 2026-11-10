using System;

namespace ESharp.Platform;

/// <summary>
/// Pointer views of strings, for library code that stores strings in raw memory. On the Amiga a string is a pointer to
/// zero-terminated bytes, so both calls are plain casts (the emitter inlines them). On the host strings are managed:
/// the fake copies each distinct string into native memory once and remembers which string it came from.
/// </summary>
public static unsafe class Cast
{
#if AMIGA
    /// <summary>The address of the string's bytes. Emitter intrinsic: <c>(uint8_t *)s</c>.</summary>
    public static extern byte* StringToPtr(string s);

    /// <summary>The string that starts at <paramref name="p"/>. Emitter intrinsic: <c>(const char *)p</c>.</summary>
    public static extern string PtrToString(byte* p);
#else
    private static readonly System.Collections.Generic.Dictionary<string, ulong> ToNative = new();
    private static readonly System.Collections.Generic.Dictionary<ulong, string> FromNative = new();

    public static byte* StringToPtr(string s)
    {
        if (!ToNative.TryGetValue(s, out var address))
        {
            var bytes = System.Text.Encoding.Latin1.GetBytes(s);
            var p = (byte*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)bytes.Length + 1);
            for (int i = 0; i < bytes.Length; i++) p[i] = bytes[i];
            address = (ulong)p;
            ToNative[s] = address;
            FromNative[address] = s;
        }
        return (byte*)address;
    }

    public static string PtrToString(byte* p) { return FromNative[(ulong)p]; }
#endif
}

/// <summary>Hash and equality helpers shared by the hash collections.</summary>
public static unsafe class Hashing
{
    /// <summary>Hash of <paramref name="size"/> bytes (shift-and-add, no multiplication: cheap on a 68000).</summary>
    public static uint Bytes(byte* p, int size)
    {
        uint h = 5381;
        for (int i = 0; i < size; i++) h = ((h << 5) + h) ^ p[i];
        return h ^ (h >> 15);
    }

    public static bool SameBytes(byte* a, byte* b, int size)
    {
        for (int i = 0; i < size; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Hash of the characters of a string.</summary>
    public static uint Text(string s)
    {
        uint h = 5381;
        for (int i = 0; i < s.Length; i++) h = ((h << 5) + h) ^ s[i];
        return h ^ (h >> 15);
    }

    /// <summary>String equality by characters (the same as <c>a == b</c>).</summary>
    public static bool SameText(string a, string b) { return a == b; }
}
