namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// keymap.library bindings (v36). The library is in the Kickstart ROM, but <see cref="Keys"/> still opens it
/// itself (its own base, so the startup code does not have to know about it). Vectors from the classic fd file.
/// </summary>
[LibraryBase("keymap", "KeymapBase", "struct Library")]
public static unsafe class Keymap
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    /// <summary>Turns a raw key InputEvent into characters with the given keymap (null: the user's default). Returns how many bytes were written, or -1 if the buffer was too small.</summary>
    [AmigaLib("keymap", -42), Since(36)] public static extern int MapRawKey(void* inputEvent, byte* buffer, int length, void* keyMap);

    /// <summary>The reverse: which raw key and qualifier pairs type the given characters. Returns the number of pairs, 0 or negative if a character cannot be typed or the buffer is too small.</summary>
    [AmigaLib("keymap", -48), Since(36)] public static extern int MapANSI(byte* text, int count, byte* buffer, int length, void* keyMap);
}
#endif

/// <summary>
/// Turns Amiga raw key codes into characters with the user's keymap (keymap.library), so typed text follows the
/// keyboard the user has (German, French, ...), not a US layout. Characters are ISO-8859-1 codes (0..255); 0 means the
/// key does not type a single character (cursor and function keys, which send escape sequences, or a dead key
/// waiting for the next one). <see cref="Input.LastChar"/> uses this for keys pressed in the <see cref="Video"/> window.
/// On SDL and on the host a fixed US layout is used.
/// </summary>
public static unsafe class Keys
{
#if AMIGA
    // Qualifier bits (which modifier keys were held), measured from the system headers.
    public const int LeftShift = (int)Layout.QualLShift, RightShift = (int)Layout.QualRShift, CapsLock = (int)Layout.QualCapsLock;
    public const int Control = (int)Layout.QualControl, LeftAlt = (int)Layout.QualLAlt, RightAlt = (int)Layout.QualRAlt;
    public const int LeftAmiga = (int)Layout.QualLCommand, RightAmiga = (int)Layout.QualRCommand;
    /// <summary>Set when the key is auto-repeating (held down).</summary>
    public const int Repeat = (int)Layout.QualRepeat;
#else
    public const int LeftShift = 1, RightShift = 2, CapsLock = 4, Control = 8, LeftAlt = 16, RightAlt = 32;
    public const int LeftAmiga = 64, RightAmiga = 128, Repeat = 512;
#endif
    public const int Shift = LeftShift | RightShift, Alt = LeftAlt | RightAlt;

#if AMIGA
    private static void* _base;

    /// <summary>Opens keymap.library. Called on first use; false only if it cannot be opened (it is in ROM).</summary>
    public static bool Open()
    {
        if (_base != null) return true;
        _base = Exec.OpenLibrary("keymap.library", 37);   // the ROM copy is v37 on Kickstart 3.0 (v40 on 3.1); every call used here is v36
        if (_base == null) return false;
        Keymap.SetBase(_base);
        return true;
    }

    public static void Close()
    {
        if (_base == null) return;
        Keymap.SetBase(null);
        Exec.CloseLibrary(_base);
        _base = null;
    }

    /// <summary>The character that raw key <paramref name="rawKey"/> types with modifiers <paramref name="qualifier"/>, or 0.</summary>
    public static int Char(int rawKey, int qualifier) { return Map(rawKey, qualifier, null); }

    /// <summary>
    /// As <see cref="Char"/>, with the previous-keys data an IDCMP_RAWKEY message carries (its IAddress), which
    /// lets dead keys combine with the next key (on a German keymap, ´ then e gives é).
    /// </summary>
    internal static int Map(int rawKey, int qualifier, void* previous)
    {
        if ((rawKey & 0x80) != 0 || !Open()) return 0;     // key-up codes type nothing
        uint[] ev = new uint[(Layout.InputEventSize + 3) / 4];   // whole words: room for the struct on 32- and 64-bit targets
        byte[] text = new byte[8];
        fixed (uint* e = ev)
        fixed (byte* t = text)
        {
            byte* ie = (byte*)e;
            ie[Layout.IeClass] = (byte)Layout.IeClassRawKey;
            *(ushort*)(ie + Layout.IeCode) = (ushort)rawKey;
            *(ushort*)(ie + Layout.IeQualifier) = (ushort)qualifier;
            *(void**)(ie + Layout.IeEventAddress) = previous;
            int n = Keymap.MapRawKey(ie, t, 8, null);
            return n == 1 ? t[0] : 0;
        }
    }

    /// <summary>The raw key and qualifier that type <paramref name="ch"/> on the user's keymap, or (-1, 0) if it needs more than one key or none.</summary>
    public static (int Code, int Qualifier) Find(int ch)
    {
        if (ch <= 0 || ch > 255 || !Open()) return (-1, 0);
        byte[] one = new byte[1];
        byte[] pairs = new byte[8];
        fixed (byte* s = one)
        fixed (byte* p = pairs)
        {
            s[0] = (byte)ch;
            if (Keymap.MapANSI(s, 1, p, 8, null) != 1) return (-1, 0);
            return (p[0], p[1]);
        }
    }
#elif SDL
    // SDL: the same US layout as the host, in the C runtime (es_std_keychar).
    public static bool Open() { return true; }

    public static void Close() { }

    public static int Char(int rawKey, int qualifier) { return Crt.es_std_keychar(rawKey, qualifier); }

    public static (int Code, int Qualifier) Find(int ch)
    {
        if (ch <= 0 || ch > 255) return (-1, 0);
        for (int k = 0; k < 0x48; k++) if (Char(k, 0) == ch) return (k, 0);
        for (int k = 0; k < 0x48; k++) if (Char(k, LeftShift) == ch) return (k, LeftShift);
        return (-1, 0);
    }
#else
    // Host: a US layout for the keys that type something (index = raw key code).
    private const string Plain = "`1234567890-=\\\0" + "0qwertyuiop[]\0" + "123asdfghjkl;'\0\0" + "456\0zxcvbnm,./\0" + ".789 ";
    private const string Shifted = "~!@#$%^&*()_+|\0" + "0QWERTYUIOP{}\0" + "123ASDFGHJKL:\"\0\0" + "456\0ZXCVBNM<>?\0" + ".789 ";

    public static bool Open() { return true; }

    public static void Close() { }

    public static int Char(int rawKey, int qualifier)
    {
        if (rawKey < 0 || (rawKey & 0x80) != 0) return 0;
        if (rawKey == 0x41) return 8;      // backspace
        if (rawKey == 0x42) return 9;      // tab
        if (rawKey == 0x43 || rawKey == 0x44) return 13;   // enter, return
        if (rawKey == 0x45) return 27;     // escape
        if (rawKey == 0x46) return 127;    // del
        if (rawKey >= Plain.Length) return 0;
        bool shift = (qualifier & Shift) != 0;
        char c = shift ? Shifted[rawKey] : Plain[rawKey];
        if ((qualifier & CapsLock) != 0 && !shift && c >= 'a' && c <= 'z') c = (char)(c - 32);
        if ((qualifier & Control) != 0 && c >= 'a' - 32 && c <= 'z') return c & 31;
        return c;
    }

    public static (int Code, int Qualifier) Find(int ch)
    {
        if (ch <= 0 || ch > 255) return (-1, 0);
        for (int k = 0; k < 0x48; k++) if (Char(k, 0) == ch) return (k, 0);
        for (int k = 0; k < 0x48; k++) if (Char(k, LeftShift) == ch) return (k, LeftShift);
        return (-1, 0);
    }
#endif
}
