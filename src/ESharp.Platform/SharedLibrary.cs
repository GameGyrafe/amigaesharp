#if HOST
using System;
using System.Collections.Generic;
using System.Reflection;
#endif

namespace ESharp.Platform;

/// <summary>
/// Opening an E# Amiga shared library (a project with EsOutputType AmigaLibrary, issue #154) through its generated binding. On the Amiga
/// targets an opener is exec's OpenLibrary, whose base the binding's vector calls pass (in a6 on m68k, as the AROS ABI passes a base on
/// AROS); on AmigaOS 4 an opener is the library's main interface, which the library clones for each opener, and the calls go through
/// it. On the host and on sdl the library's code is compiled into the program and the binding calls it directly; the host then keeps
/// each opener's static fields apart (Enter and Leave swap them in and out around a call), so the program sees what it sees on the
/// Amiga. sdl does the same in C: there each opener has its own copy of the library's static data (CEmitter.Openers.cs).
/// </summary>
public static unsafe class SharedLibrary
{
#if AMIGAOS4
    /// <summary>
    /// exec OpenLibrary, then the library's main interface (GetInterface "main" 1): the opener's interface, which the library made for this
    /// opener alone, or null when the library is missing or older than <paramref name="version"/>. The binding's calls go through it.
    /// </summary>
    public static void* Open(string name, int version, int libraryVersion, string types)
    {
        return OpenMain(name, version);
    }

    /// <summary>DropInterface of the opener's interface (its data goes with it), then CloseLibrary.</summary>
    public static void Close(void* library)
    {
        if (library != null) CloseMain(library);
    }

    [Native("es_lib_open_main")]
    private static extern void* OpenMain(string name, int version);

    [Native("es_lib_close_main")]
    private static extern void CloseMain(void* main);
#elif AMIGA
    /// <summary>exec OpenLibrary: the opener's base, or null when the library is missing or older than <paramref name="version"/>.</summary>
    public static void* Open(string name, int version, int libraryVersion, string types)
    {
        return Exec.OpenLibrary(name, (uint)version);
    }

    public static void Close(void* library)
    {
        if (library != null) Exec.CloseLibrary(library);
    }
#endif
#if AMIGA

    /// <summary>The open count of the library in exec's list (found by name, under Forbid), or -1 when it is not in memory.</summary>
    public static int OpenCount(string name) { return OpenCountOf(name); }

    [Native("es_lib_open_count")]
    private static extern int OpenCountOf(string name);

    /// <summary>
    /// While the library is open: asks it to go when its last opener closes (exec RemLibrary, whose Expunge then sets LIBF_DELEXP).
    /// False when it is not open (use <see cref="FlushUnused"/> then).
    /// </summary>
    public static bool ExpungeWhenClosed(string name) { return ExpungeWhenClosedOf(name) != 0; }

    [Native("es_lib_expunge_when_closed")]
    private static extern int ExpungeWhenClosedOf(string name);

    /// <summary>
    /// Asks exec to expunge the libraries nobody has open: an allocation larger than any Amiga's memory, which exec answers by flushing
    /// unused libraries (each one's Expunge) before it fails. AmigaOS 4 does not: there a library nobody has open stays in memory (use
    /// <see cref="ExpungeWhenClosed"/> while it is open).
    /// </summary>
    public static void FlushUnused()
    {
        byte* p = Exec.AllocVec(0x7FFFFFF0u, 0);
        if (p != null) Exec.FreeVec(p);
    }
#elif SDL
    // sdl: the library's code is part of the program. Each opener has its own copy of the library's static data, set up and torn down
    // by the compiler's es_ol_* functions (CEmitter.Openers.cs, from the binding's [LibraryBinding]); Enter and Leave around each call
    // make the library's code use the calling opener's copy. The open count is kept here, per library name.
    private static string[] _names = new string[16];
    private static int[] _counts = new int[16];
    private static bool[] _expungeAsked = new bool[16];

    public static void* Open(string name, int version, int libraryVersion, string types)
    {
        if (version > libraryVersion) return null;
        int slot = -1;
        for (int i = 0; i < 16; i++)
        {
            if (_names[i] != null && _names[i] == name) { slot = i; break; }
        }
        bool loaded = false;
        if (slot < 0)
        {
            for (int i = 0; i < 16; i++)
            {
                if (_names[i] == null) { _names[i] = name; slot = i; loaded = true; break; }
            }
        }
        if (slot < 0) return null;
        // Counted first, as the Amiga library's Open does; the opener's data is set up (its static constructors run) in OpenerOpen.
        _counts[slot]++;
        _expungeAsked[slot] = false;
        void* opener = OpenerOpen(name, slot);
        if (opener == null)
        {
            _counts[slot]--;
            if (loaded) _names[slot] = null;   // as if it had not loaded (no memory)
        }
        return opener;
    }

    public static void Close(void* library)
    {
        if (library == null) return;
        int slot = OpenerSlot(library);
        OpenerClose(library);   // the opener's [AtExit] methods, its objects and its data
        if (slot < 0 || slot >= 16 || _counts[slot] == 0) return;
        _counts[slot]--;
        if (_counts[slot] == 0 && _expungeAsked[slot]) { _names[slot] = null; _expungeAsked[slot] = false; }
    }

    public static int OpenCount(string name)
    {
        for (int i = 0; i < 16; i++)
        {
            if (_names[i] != null && _names[i] == name) return _counts[i];
        }
        return -1;
    }

    /// <summary>Before a call into the library: its code uses this opener's data, and the objects it makes are this opener's.</summary>
    public static void Enter(void* library) { OpenerEnter(library); }

    /// <summary>After the call: no opener's data is in use.</summary>
    public static void Leave(void* library) { OpenerLeave(library); }

    public static bool ExpungeWhenClosed(string name)
    {
        for (int i = 0; i < 16; i++)
        {
            if (_names[i] != null && _names[i] == name && _counts[i] > 0) { _expungeAsked[i] = true; return true; }
        }
        return false;
    }

    /// <summary>Forgets the libraries nobody has open (as an expunge would).</summary>
    public static void FlushUnused()
    {
        for (int i = 0; i < 16; i++)
        {
            if (_names[i] != null && _counts[i] == 0) _names[i] = null;
        }
    }

    [Native("es_ol_open")]
    private static extern void* OpenerOpen(string name, int slot);

    [Native("es_ol_slot")]
    private static extern int OpenerSlot(void* opener);

    [Native("es_ol_close")]
    private static extern void OpenerClose(void* opener);

    [Native("es_ol_enter")]
    private static extern void OpenerEnter(void* opener);

    [Native("es_ol_leave")]
    private static extern void OpenerLeave(void* opener);
#else
    // The host: each opener has its own copy of the static fields of the library's types (as on the Amiga, where each opener has its own
    // data block). The first Open of a library takes the fields' values after the types' static constructors as the start values, and every
    // opener gets a deep copy of them (arrays and objects too, with shared references kept shared), so its objects are its own. Enter puts
    // an opener's values into the fields and Leave takes them back. A readonly array field keeps its array: its elements are swapped instead.
    // Delegates and strings are not copied.
    private sealed class Library
    {
        public readonly System.Collections.Generic.List<FieldInfo> Fields = new();
        public readonly System.Collections.Generic.Dictionary<FieldInfo, object?> Start = new();
        public int Count;
        public bool Loaded;   // false after FlushUnused: "not in memory" until the next Open (the start values stay)
        public bool ExpungeAsked;   // FlushUnused while it was open: it goes at the last Close, as with exec's LIBF_DELEXP
    }

    private sealed class Opener
    {
        public Library Of = null!;
        public readonly System.Collections.Generic.Dictionary<FieldInfo, object?> Values = new();
    }

    private static readonly System.Collections.Generic.Dictionary<string, Library> _libraries = new();
    private static readonly System.Collections.Generic.Dictionary<nint, Opener> _openers = new();
    private static nint _next = 0x1000;
    private static readonly object _lock = new();

    public static void* Open(string name, int version, int libraryVersion, string types)
    {
        if (version > libraryVersion) return null;
        lock (_lock)
        {
            if (!_libraries.TryGetValue(name, out var lib))
            {
                lib = new Library();
                foreach (var typeName in types.Split(';', StringSplitOptions.RemoveEmptyEntries)) AddFields(lib, FindType(typeName));
                var memo = new System.Collections.Generic.Dictionary<object, object>(ReferenceEqualityComparer.Instance);
                foreach (var f in lib.Fields) lib.Start[f] = DeepCopy(f.GetValue(null), memo);
                _libraries[name] = lib;
            }
            lib.Loaded = true;
            lib.ExpungeAsked = false;
            var opener = new Opener { Of = lib };
            var copies = new System.Collections.Generic.Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            foreach (var f in lib.Fields) opener.Values[f] = DeepCopy(lib.Start[f], copies);
            lib.Count++;
            var handle = _next;
            _next += 16;
            _openers[handle] = opener;
            return (void*)handle;
        }
    }

    public static void Close(void* library)
    {
        lock (_lock)
        {
            if (library == null || !_openers.Remove((nint)library, out var opener)) return;
            opener.Of.Count--;
            if (opener.Of.Count == 0 && opener.Of.ExpungeAsked) { opener.Of.Loaded = false; opener.Of.ExpungeAsked = false; }
        }
    }

    public static int OpenCount(string name)
    {
        lock (_lock) return _libraries.TryGetValue(name, out var lib) && lib.Loaded ? lib.Count : -1;
    }

    /// <summary>While the library is open: it goes (not in memory) when its last opener closes.</summary>
    public static bool ExpungeWhenClosed(string name)
    {
        lock (_lock)
        {
            if (!_libraries.TryGetValue(name, out var lib) || !lib.Loaded || lib.Count == 0) return false;
            lib.ExpungeAsked = true;
            return true;
        }
    }

    /// <summary>Forgets the libraries nobody has open (as an expunge would): the next Open starts from their start values again.</summary>
    public static void FlushUnused()
    {
        lock (_lock)
        {
            foreach (var lib in _libraries.Values)
                if (lib.Count == 0) lib.Loaded = false;
        }
    }

    /// <summary>Before a call into the library: its static fields get this opener's values.</summary>
    public static void Enter(void* library)
    {
        lock (_lock)
        {
            if (!_openers.TryGetValue((nint)library, out var opener)) return;
            foreach (var f in opener.Of.Fields)
            {
                if (f.IsInitOnly) { if (f.GetValue(null) is Array target && opener.Values[f] is Array mine) Array.Copy(mine, target, Math.Min(mine.Length, target.Length)); }
                else f.SetValue(null, opener.Values[f]);
            }
        }
    }

    /// <summary>After the call: the opener keeps what the call left in the fields.</summary>
    public static void Leave(void* library)
    {
        lock (_lock)
        {
            if (!_openers.TryGetValue((nint)library, out var opener)) return;
            foreach (var f in opener.Of.Fields)
            {
                if (f.IsInitOnly) { if (f.GetValue(null) is Array live && opener.Values[f] is Array mine) Array.Copy(live, mine, Math.Min(mine.Length, live.Length)); }
                else opener.Values[f] = f.GetValue(null);
            }
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            if (asm.GetType(name) is { } t) return t;
        return null;
    }

    private static void AddFields(Library lib, Type? t)
    {
        if (t == null || t.ContainsGenericParameters) return;
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
        foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (f.IsLiteral) continue;
            if (f.IsInitOnly && !f.FieldType.IsArray) continue;   // a readonly value or object reference cannot change
            lib.Fields.Add(f);
        }
        foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) AddFields(lib, nested);
    }

    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>A copy of an opener's value: arrays and objects copied all the way down, shared references kept shared (<paramref name="memo"/>).</summary>
    private static object? DeepCopy(object? value, System.Collections.Generic.Dictionary<object, object> memo)
    {
        if (value == null || value is string || value is Delegate || value is Pointer) return value;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum) return value;
        if (memo.TryGetValue(value, out var done)) return done;
        if (value is Array array)
        {
            var copy = (Array)array.Clone();
            memo[value] = copy;
            if (!type.GetElementType()!.IsPrimitive)
                for (var i = 0; i < copy.Length; i++) copy.SetValue(DeepCopy(copy.GetValue(i), memo), i);
            return copy;
        }
        var clone = MemberwiseCloneMethod.Invoke(value, null)!;   // a struct comes boxed: the box is copied
        memo[value] = clone;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!f.FieldType.IsPrimitive && !f.FieldType.IsPointer && !f.FieldType.IsEnum) f.SetValue(clone, DeepCopy(f.GetValue(clone), memo));
        return clone;
    }
#endif
}
