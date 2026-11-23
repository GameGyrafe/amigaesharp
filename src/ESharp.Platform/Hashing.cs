using System;

namespace ESharp.Platform;

/// <summary>
/// Pointer views of strings, for library code that stores strings in raw memory. On the Amiga a string is a pointer to
/// zero-terminated bytes, so both calls are plain casts (the emitter inlines them). On the host strings are managed:
/// the fake copies each distinct string into native memory once and remembers which string it came from.
/// </summary>
public static unsafe class Cast
{
#if AMIGA || SDL
    /// <summary>The address of the string's bytes. Emitter intrinsic: <c>(uint8_t *)s</c>.</summary>
    public static extern byte* StringToPtr(string s);

    /// <summary>The string that starts at <paramref name="p"/>. Emitter intrinsic: <c>(const char *)p</c>.</summary>
    public static extern string PtrToString(byte* p);

    /// <summary>The address of a class object, for a system slot that holds a pointer (a hook's h_Data, a user-data attribute).
    /// It does not keep the object alive or count as an owner. Emitter intrinsic: <c>(void *)o</c>; <typeparamref name="T"/> must be
    /// a class (not an interface, delegate or array: ES0099). Platform-internal for now (the MUI wrappers may need it).</summary>
    internal static extern void* ObjectToPtr<T>(T o) where T : class;

    /// <summary>The object whose address <see cref="ObjectToPtr"/> gave. Unchecked on the Amiga: a pointer that did not come from there, an
    /// object of another class or a freed object is not detected (the host throws). Emitter intrinsic: <c>(T *)p</c>.</summary>
    internal static extern T PtrToObject<T>(void* p) where T : class;
#else
    private static readonly System.Collections.Generic.Dictionary<string, ulong> ToNative = new();
    private static readonly System.Collections.Generic.Dictionary<ulong, string> FromNative = new();

    // The host tables are process-wide and test classes run in parallel (several samples in one process): every access is locked, as a
    // Dictionary mutated by two threads loses entries or throws (#141).
    private static readonly object HostLock = new();

    public static byte* StringToPtr(string s)
    {
        lock (HostLock)
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
    }

    public static string PtrToString(byte* p) { lock (HostLock) return FromNative[(ulong)p]; }

    // Host: objects get made-up addresses (never dereferenced), kept in both directions.
    private static readonly System.Collections.Generic.Dictionary<object, ulong> ObjToNative = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private static readonly System.Collections.Generic.Dictionary<ulong, object> NativeToObj = new();
    private static ulong _nextObject = 0x10000;

    internal static void* ObjectToPtr<T>(T o) where T : class
    {
        lock (HostLock)
        {
            if (!ObjToNative.TryGetValue(o, out var address))
            {
                address = _nextObject;
                _nextObject += 16;
                ObjToNative[o] = address;
                NativeToObj[address] = o;
            }
            return (void*)address;
        }
    }

    internal static T PtrToObject<T>(void* p) where T : class { lock (HostLock) return (T)NativeToObj[(ulong)p]; }
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
