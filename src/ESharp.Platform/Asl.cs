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
/// appears on it, otherwise on the default public screen (Workbench). On the host nothing is shown: <see cref="HostPick"/> is the fake answer.
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

    private static int Append(byte* dest, int at, byte* text)
    {
        if (text == null) return at;
        for (int i = 0; text[i] != 0 && at < 254; i++) dest[at++] = text[i];
        return at;
    }

    /// <summary>
    /// Shows a file requester titled <paramref name="title"/>, starting in <paramref name="drawer"/> (for example <c>"RAM:"</c>), and waits.
    /// Returns the chosen path (drawer and file name joined), or null if the user cancelled or asl.library is missing.
    /// The string is only valid until the next call.
    /// </summary>
    public static string? PickFile(string title, string drawer)
    {
        if (!Open()) return null;
        void* req = AslLib.AllocAslRequest(Layout.AslFileRequest, null);
        if (req == null) return null;
        string? result = null;
        nuint[] tags = new nuint[12];
        fixed (nuint* t = tags)
        {
            TagWriter w = new TagWriter(t, 12);
            w.Add(Layout.AslFrTitleText, Cast.StringToPtr(title));
            w.Add(Layout.AslFrInitialDrawer, Cast.StringToPtr(drawer));
            if (Video.IsOpen()) w.Add(Layout.AslFrWindow, Video.Window);
            if (w.End() && AslLib.AslRequest(req, t) != 0)
            {
                byte* r = (byte*)req;
                byte* d = *(byte**)(r + Layout.FrDrawer);
                byte* f = *(byte**)(r + Layout.FrFile);
                fixed (byte* p = _path)
                {
                    int n = Append(p, 0, d);
                    if (n > 0 && p[n - 1] != ':' && p[n - 1] != '/' && f != null && f[0] != 0) p[n++] = (byte)'/';
                    n = Append(p, n, f);
                    p[n] = 0;
                    result = Cast.PtrToString(p);
                }
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
    public static string? HostLastTitle, HostLastDrawer;

    public static bool Open() { return HostAvailable; }

    public static void Close() { }

    public static bool CanAllocate() { return HostAvailable; }

    public static string? PickFile(string title, string drawer)
    {
        if (!HostAvailable) return null;
        HostLastTitle = title;
        HostLastDrawer = drawer;
        return HostPick;
    }
#endif
}
#endif
