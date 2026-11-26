namespace ESharp.Platform;

/// <summary>
/// A file requester on every target: <see cref="Open"/> asks for an existing file to read, <see cref="Save"/> for a name to write.
/// Both wait for the answer and return the chosen path, or null when the user cancels or no requester can be shown.
/// <para>
/// <c>drawer</c> is the folder the requester starts in (<c>"RAM:"</c> on the Amiga, <c>"/tmp"</c> on sdl; empty is the default),
/// <c>file</c> the name it starts with (may be empty) and <c>pattern</c> filters the listing (may be empty for all files). The pattern is
/// AmigaDOS style: <c>"#?"</c> is everything, <c>"#?.png"</c> files ending in .png, <c>"#?.(png|jpg)"</c> either. Other patterns show every file on sdl.
/// The returned string is only valid until the next call: copy it to keep it.
/// </para>
/// <para>
/// Amiga targets (m68k, os4, morphos, aros): asl.library (<see cref="Asl.Pick"/>), on the <see cref="Video"/> screen when one is open, else
/// on Workbench; null if asl.library is not installed. The path is drawer and file joined with dos.library AddPart.
/// SDL: SDL_ShowOpenFileDialog / SDL_ShowSaveFileDialog through SDL_ShowFileDialogWithProperties. They are asynchronous and show the
/// system's dialog (on Linux through the desktop portal or zenity), so the call pumps events until the dialog's callback fires. Without a
/// desktop (SDL_VIDEODRIVER dummy or offscreen, as in headless runs) there is no dialog: the call returns null at once and prints the reason to stderr. Cancel and an error are null too.
/// Host: the answer set with <see cref="HostPick"/> (null, a cancel, until then).
/// </para>
/// </summary>
public static unsafe class FileRequester
{
    /// <summary>Shows a file requester to choose a file to read. Returns its path, or null when cancelled or unavailable.</summary>
    public static string? Open(string title, string drawer, string file, string pattern)
    {
        return Show(title, drawer, file, pattern, false);
    }

    /// <summary>Shows a file requester to choose where to write (the name may be a new file). Returns the path, or null when cancelled or unavailable.</summary>
    public static string? Save(string title, string drawer, string file, string pattern)
    {
        return Show(title, drawer, file, pattern, true);
    }

#if AMIGA
    private static string? Show(string title, string drawer, string file, string pattern, bool save)
    {
        return Asl.Pick(title, drawer, file, pattern, save);
    }
#elif SDL
    private static byte[] _path = new byte[1024];

    private static string? Show(string title, string drawer, string file, string pattern, bool save)
    {
        fixed (byte* p = _path)
        {
            int n = Crt.es_sdl_file_dialog(title, drawer, file, pattern, save, p, 1024);
            if (n < 0) return null;
            return Cast.PtrToString(p);
        }
    }
#else
    /// <summary>Host: what <see cref="Open"/> and <see cref="Save"/> return (null is Cancel).</summary>
    public static string? HostPick;

    /// <summary>Host: the arguments of the last call, and how many calls there were.</summary>
    public static string? HostLastTitle, HostLastDrawer, HostLastFile, HostLastPattern;
    public static bool HostLastSave;
    public static int HostShown;

    private static string? Show(string title, string drawer, string file, string pattern, bool save)
    {
        HostShown++;
        HostLastTitle = title;
        HostLastDrawer = drawer;
        HostLastFile = file;
        HostLastPattern = pattern;
        HostLastSave = save;
        return HostPick;
    }
#endif
}
