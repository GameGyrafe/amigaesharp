// Amiga-only (GadTools, ASL, iffparse, diskfont, lowlevel): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// asl.library bindings (v36 and up). The library is on the Workbench disk (<c>Libs:</c>), not in ROM, so <see cref="Asl"/> opens it
/// first and only then calls these. Vectors from the classic fd file (checked with tools/BindGen).
/// </summary>
[LibraryBase("asl", "AslBase", "struct Library")]
public static unsafe class AslLib
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    [AmigaLib("asl", -48), Since(36)] public static extern void* AllocAslRequest(uint reqType, void* tags);
    [AmigaLib("asl", -54), Since(36)] public static extern void FreeAslRequest(void* requester);
    /// <summary>Shows the requester and waits. True if the user chose OK, false for Cancel.</summary>
    [AmigaLib("asl", -60), Since(36)] public static extern int AslRequest(void* requester, void* tags);
}
#endif

/// <summary>
/// The system file requester (asl.library): <see cref="PickFile"/> asks the user for a file and returns its path, or null if
/// they cancel. asl.library is on the Workbench disk, not in ROM: if it is missing <see cref="Open"/> returns false and
/// <see cref="PickFile"/> returns null, so a program should say so. With a <see cref="Video"/> screen open the requester
/// appears on it (double buffering is turned off first, see <see cref="Video.EnableDoubleBuffer"/>), otherwise on the default public screen (Workbench). On the host nothing is shown: <see cref="HostPick"/> is the fake answer.
/// </summary>
public static unsafe class Asl
{
#if AMIGA
    private static void* _base;
    private static byte[] _path = new byte[256];

    /// <summary>Opens asl.library (v39). False if it is not installed.</summary>
    public static bool Open()
    {
        if (_base != null) return true;
        _base = Exec.OpenLibrary("asl.library", 39);
        if (_base == null) return false;
        AslLib.SetBase(_base);
        return true;
    }

    public static void Close()
    {
        if (_base == null) return;
        AslLib.SetBase(null);
        Exec.CloseLibrary(_base);
        _base = null;
    }

    /// <summary>Allocates a file requester and frees it again without showing it: a check that the library works and there is memory for one.</summary>
    public static bool CanAllocate()
    {
        if (!Open()) return false;
        void* req = AslLib.AllocAslRequest(Layout.AslFileRequest, null);
        if (req == null) return false;
        AslLib.FreeAslRequest(req);
        return true;
    }

    /// <summary>
    /// Shows a file requester titled <paramref name="title"/>, starting in <paramref name="drawer"/> (for example <c>"RAM:"</c>), and waits.
    /// Returns the chosen path (drawer and file name joined), or null if the user cancelled or asl.library is missing.
    /// The string is only valid until the next call.
    /// </summary>
    public static string? PickFile(string title, string drawer)
    {
        return Pick(title, drawer, "", "", false);
    }

    /// <summary>
    /// The general call behind <see cref="PickFile"/> and <see cref="FileRequester"/>: <paramref name="file"/> is the file name the requester
    /// starts with and <paramref name="pattern"/> an AmigaDOS pattern such as <c>"#?.png"</c> (both may be empty), and
    /// <paramref name="save"/> asks for a name to write (save mode) rather than a file to read. The path is drawer and file joined with dos.library AddPart.
    /// The string is only valid until the next call.
    /// </summary>
    public static string? Pick(string title, string drawer, string file, string pattern, bool save)
    {
        if (!Open()) return null;
        // Intuition draws the requester into the screen's own bitmap, which is not on display while double buffered (and the next
        // Flip would overwrite it): show the frame and draw straight to the screen again. The program can enable it again after.
        if (Video.IsDoubleBuffered()) Video.DisableDoubleBuffer();
        void* req = AslLib.AllocAslRequest(Layout.AslFileRequest, null);
        if (req == null) return null;
        string? result = null;
        TagList w = new TagList();
        w.Add(Layout.AslFrTitleText, Cast.StringToPtr(title));
        w.Add(Layout.AslFrInitialDrawer, Cast.StringToPtr(drawer));
        if (Cast.StringToPtr(file)[0] != 0) w.Add(Layout.AslFrInitialFile, Cast.StringToPtr(file));
        if (Cast.StringToPtr(pattern)[0] != 0)
        {
            w.Add(Layout.AslFrInitialPattern, Cast.StringToPtr(pattern));
            w.Add(Layout.AslFrDoPatterns, true);
        }
        if (save) w.Add(Layout.AslFrDoSaveMode, true);
        if (Video.IsOpen()) w.Add(Layout.AslFrWindow, Video.Window);
        nuint* t = w.End();
        if (t != null && AslLib.AslRequest(req, t) != 0)
        {
            byte* r = (byte*)req;
            byte* d = *(byte**)(r + Layout.FrDrawer);
            byte* f = *(byte**)(r + Layout.FrFile);
            // The requester owns drawer and file: copy them out (into _path) before it is freed.
            fixed (byte* p = _path)
            {
                int n = 0;
                if (d != null) for (; d[n] != 0 && n < 254; n++) p[n] = d[n];
                p[n] = 0;
                if (f != null && f[0] != 0) Dos.AddPart(p, f, 256);
                result = Cast.PtrToString(p);
            }
        }
        AslLib.FreeAslRequest(req);
        return result;
    }
#else
    /// <summary>Host: whether the fake asl.library is "installed". Set false to test the missing-library path.</summary>
    public static bool HostAvailable = true;

    /// <summary>Host: what <see cref="PickFile"/> returns (null is Cancel).</summary>
    public static string? HostPick;

    /// <summary>Host: the title and drawer of the last <see cref="PickFile"/> call.</summary>
    public static string? HostLastTitle, HostLastDrawer, HostLastFile, HostLastPattern;

    /// <summary>Host: whether the last <see cref="Pick"/> call asked for save mode.</summary>
    public static bool HostLastSave;

    public static bool Open() { return HostAvailable; }

    public static void Close() { }

    public static bool CanAllocate() { return HostAvailable; }

    public static string? PickFile(string title, string drawer)
    {
        return Pick(title, drawer, "", "", false);
    }

    public static string? Pick(string title, string drawer, string file, string pattern, bool save)
    {
        if (!HostAvailable) return null;
        HostLastTitle = title;
        HostLastDrawer = drawer;
        HostLastFile = file;
        HostLastPattern = pattern;
        HostLastSave = save;
        return HostPick;
    }
#endif
}
#endif
