// Amiga-only (GadTools, ASL, iffparse, diskfont, lowlevel): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// diskfont.library bindings (v34 and up). The library is on the Workbench disk (<c>Libs:</c>), not in ROM, so
/// <see cref="Fonts"/> opens it first and only then calls these. Vectors from the classic fd file.
/// </summary>
[LibraryBase("diskfont", "DiskfontBase", "struct Library")]
public static unsafe class DiskFontLib
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    /// <summary>Opens a font from the FONTS: drawer (or one already in memory). Close it with graphics CloseFont.</summary>
    [AmigaLib("diskfont", -30), Since(34)] public static extern void* OpenDiskFont(void* textAttr);
}
#endif

/// <summary>An opened font. <see cref="Height"/> and <see cref="Width"/> are what the system gave, which can differ from the size asked for.</summary>
public unsafe struct FontHandle
{
    /// <summary>False if the font could not be opened.</summary>
    public bool IsOpen;
    /// <summary>Pixel height of a line (the font's YSize).</summary>
    public int Height;
    /// <summary>Pixel width of one character (the font's XSize; a proportional font's average).</summary>
    public int Width;
    /// <summary>Distance from the top of a line to the baseline.</summary>
    public int Baseline;
    internal nuint Handle;
}

/// <summary>
/// Opens fonts by name and size: <c>Fonts.Open("topaz", 8)</c> is in ROM and always works; others (<c>"ruby"</c>,
/// <c>"helvetica"</c>) come from the <c>FONTS:</c> drawer through diskfont.library, which is on the Workbench disk
/// and may be missing, so a program should check <see cref="DiskFontsAvailable"/> or the <see cref="FontHandle.IsOpen"/> of the result.
/// <see cref="Draw"/> writes text on the <see cref="Video"/> screen with a font. On the host a small fake list is
/// used (topaz 8 and 9, plus whatever <see cref="HostAdd"/> adds), and text is drawn with the built-in <see cref="Font"/>.
/// </summary>
public static unsafe class Fonts
{
#if AMIGA
    private static void* _disk;

    /// <summary>Opens diskfont.library (v39). False if it is not installed. Called by <see cref="Open"/> when a font is not in memory.</summary>
    public static bool OpenDiskFontLibrary()
    {
        if (_disk != null) return true;
        _disk = Exec.OpenLibrary("diskfont.library", 39);
        if (_disk == null) return false;
        DiskFontLib.SetBase(_disk);
        return true;
    }

    /// <summary>True if diskfont.library can be opened, so fonts that are not in ROM can be loaded.</summary>
    public static bool DiskFontsAvailable { get { return OpenDiskFontLibrary(); } }

    /// <summary>Closes diskfont.library. Close the fonts first.</summary>
    public static void Close()
    {
        if (_disk == null) return;
        DiskFontLib.SetBase(null);
        Exec.CloseLibrary(_disk);
        _disk = null;
    }

    /// <summary>
    /// Opens the font <paramref name="name"/> (with or without ".font") at <paramref name="size"/> pixels. With diskfont.library
    /// available it asks diskfont, which looks in memory and ROM first, then FONTS:, prefers the exact size and otherwise
    /// takes the closest one the font has, so check <see cref="FontHandle.Height"/>. Without diskfont.library only a font
    /// in ROM or memory at exactly <paramref name="size"/> opens. The result has <see cref="FontHandle.IsOpen"/> false if there is no such font.
    /// </summary>
    public static FontHandle Open(string name, int size)
    {
        FontHandle h = new FontHandle();
        int n = name.Length;
        if (size <= 0 || size > 255 || n == 0 || n > 40) return h;
        nuint[] attr = new nuint[16];                     // pointer-aligned: the TextAttr, then the name from byte 16 (room for 48 bytes on m68k)
        fixed (nuint* ap = attr)
        {
            byte* b = (byte*)ap;
            byte* nameCopy = b + 16;
            for (int i = 0; i < n; i++) nameCopy[i] = (byte)name[i];
            int len = n;
            bool hasSuffix = n >= 5 && nameCopy[n - 5] == '.' && nameCopy[n - 4] == 'f' && nameCopy[n - 3] == 'o' && nameCopy[n - 2] == 'n' && nameCopy[n - 1] == 't';
            if (!hasSuffix)
            {
                if (n + 6 > 47) return h;
                nameCopy[len] = (byte)'.'; nameCopy[len + 1] = (byte)'f'; nameCopy[len + 2] = (byte)'o'; nameCopy[len + 3] = (byte)'n'; nameCopy[len + 4] = (byte)'t';
                len += 5;
            }
            nameCopy[len] = 0;
            *(byte**)(b + Layout.TaName) = nameCopy;
            *(ushort*)(b + Layout.TaYSize) = (ushort)size;
            b[Layout.TaStyle] = 0;
            b[Layout.TaFlags] = 0;
            void* f;
            if (OpenDiskFontLibrary()) f = DiskFontLib.OpenDiskFont(b);   // searches memory and ROM first, prefers the exact size, else the closest
            else
            {
                f = Graphics.OpenFont(b);                                  // OpenFont alone returns the closest size in memory, so only an exact one counts
                if (f != null && *(ushort*)((byte*)f + Layout.TfYSize) != (ushort)size) { Graphics.CloseFont(f); f = null; }
            }
            if (f == null) return h;
            byte* fb = (byte*)f;
            h.Handle = (nuint)f;
            h.Height = *(ushort*)(fb + Layout.TfYSize);
            h.Width = *(ushort*)(fb + Layout.TfXSize);
            h.Baseline = *(ushort*)(fb + Layout.TfBaseline);
            h.IsOpen = true;
        }
        return h;
    }

    /// <summary>Closes a font opened with <see cref="Open"/>. Safe to call twice.</summary>
    public static void Close(ref FontHandle font)
    {
        if (!font.IsOpen) return;
        Graphics.CloseFont((void*)font.Handle);
        font.IsOpen = false;
        font.Handle = 0;
    }

    private static void* RastPort()
    {
        if (!Video.IsOpen()) return null;
        return *(void**)((byte*)Video.Window + Layout.WindowRPort);
    }

    /// <summary>Width in pixels of <paramref name="text"/> in <paramref name="font"/> (exact with a <see cref="Video"/> screen open, else the character width times the length).</summary>
    public static int TextWidth(FontHandle font, string text)
    {
        void* rp = RastPort();
        if (!font.IsOpen || rp == null) return font.Width * text.Length;
        Graphics.SetFont(rp, (void*)font.Handle);
        return Graphics.TextLength(rp, text, text.Length);
    }

    /// <summary>Draws <paramref name="text"/> with its top-left corner at (x, y) in palette colour <paramref name="color"/> on the <see cref="Video"/> window. Nothing if no screen is open.</summary>
    public static void Draw(FontHandle font, string text, int x, int y, int color)
    {
        void* rp = RastPort();
        if (!font.IsOpen || rp == null) return;
        Graphics.SetFont(rp, (void*)font.Handle);
        Graphics.SetAPen(rp, color);
        Graphics.Move(rp, x, y + font.Baseline);
        Graphics.Text(rp, text, text.Length);
    }
#else
    private static readonly System.Collections.Generic.List<(string Name, int Size, int Width, int Baseline)> HostList = new()
    {
        ("topaz", 8, 8, 6), ("topaz", 9, 8, 7),
    };
    private static int _hostOpen;

    /// <summary>Host: whether the fake diskfont.library is "installed". Set false to test the missing-library path.</summary>
    public static bool HostDiskFonts = true;

    /// <summary>Host: adds a font that <see cref="Open"/> will find (like one in FONTS:). It needs <see cref="HostDiskFonts"/> true.</summary>
    public static void HostAdd(string name, int size, int width, int baseline) { HostList.Add((name, size, width, baseline)); }

    /// <summary>Host: how many fonts are open now.</summary>
    public static int HostOpenCount => _hostOpen;

    public static bool OpenDiskFontLibrary() { return HostDiskFonts; }

    public static bool DiskFontsAvailable { get { return HostDiskFonts; } }

    public static void Close() { }

    public static FontHandle Open(string name, int size)
    {
        FontHandle h = new FontHandle();
        if (name.EndsWith(".font", System.StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 5);
        bool topaz = Strings.EqualsIgnoreCase(name, "topaz");
        int best = -1;
        for (int i = 0; i < HostList.Count; i++)
        {
            var e = HostList[i];
            if (!Strings.EqualsIgnoreCase(e.Name, name)) continue;
            if (!HostDiskFonts && !(topaz && e.Size == size)) continue;   // no diskfont.library: only exact ROM topaz
            if (best < 0 || System.Math.Abs(e.Size - size) < System.Math.Abs(HostList[best].Size - size)) best = i;
        }
        if (best < 0 || size <= 0) return h;
        var f = HostList[best];
        h.IsOpen = true; h.Height = f.Size; h.Width = f.Width; h.Baseline = f.Baseline; h.Handle = 1;
        _hostOpen++;
        return h;
    }

    public static void Close(ref FontHandle font)
    {
        if (!font.IsOpen) return;
        font.IsOpen = false; font.Handle = 0;
        _hostOpen--;
    }

    public static int TextWidth(FontHandle font, string text) { return font.Width * text.Length; }

    public static void Draw(FontHandle font, string text, int x, int y, int color)
    {
        if (!font.IsOpen || !Video.IsOpen()) return;
        Font.Draw(text, x, y, color, 1);      // the host has no outline or disk fonts: the built-in 5x7 stands in
    }
#endif
}
#endif
