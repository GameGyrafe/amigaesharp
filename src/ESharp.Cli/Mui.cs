using System;
using System.Collections.Generic;
using System.IO;

namespace ESharp.Cli;

/// <summary>
/// MUI for classic FS-UAE runs (<c>esharp run|test --mui 3.8|5</c>). MUI is third-party shareware whose licence allows only
/// verbatim copies of the whole archive, so E# never ships it: <c>tools/mui/fetch-mui.sh</c> downloads the official archive into
/// a per-user cache and this class only finds the unpacked drawer there and wires it into the emulated machine.
/// </summary>
internal static class Mui
{
    public const string FetchScript = "tools/mui/fetch-mui.sh";

    /// <summary>
    /// The fetch script to tell the user about: the copy shipped in the tool package (mui/ next to esharp.dll, which a checkout
    /// build also has), else the repository path. Run with bash, so it does not depend on the executable bit surviving packing.
    /// </summary>
    public static string FetchCommand(string? baseDir = null)
    {
        var shipped = Path.Combine(baseDir ?? AppContext.BaseDirectory, "mui", "fetch-mui.sh");
        return File.Exists(shipped) ? $"bash \"{shipped}\"" : FetchScript;
    }

    /// <summary>Normalises a <c>--mui</c> value: "3.8" or "5" (also "5.0"); null for anything else.</summary>
    public static string? ParseVersion(string? text) => text?.Trim() switch
    {
        "3.8" => "3.8",
        "5" or "5.0" => "5",
        _ => null,
    };

    /// <summary>The cache root: ESHARP_MUI_CACHE, else $XDG_CACHE_HOME/esharp/mui, else ~/.cache/esharp/mui.</summary>
    public static string CacheRoot(Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var over = env("ESHARP_MUI_CACHE");
        if (!string.IsNullOrEmpty(over)) return over;
        var xdg = env("XDG_CACHE_HOME");
        var home = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(env("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(home, "esharp", "mui");
    }

    /// <summary>Where fetch-mui.sh leaves the unpacked MUI drawer (the one that holds Libs, Locale, Images) for a version.</summary>
    public static string DrawerPath(string version, string cacheRoot) => Path.Combine(cacheRoot, version, "files", "MUI");

    /// <summary>Resolves the drawer for <c>--mui</c>; an error message (with the fetch command) if it is not in the cache.</summary>
    public static string? Resolve(string version, string cacheRoot, out string? error)
    {
        var drawer = DrawerPath(version, cacheRoot);
        if (File.Exists(Path.Combine(drawer, "Libs", "muimaster.library"))) { error = null; return drawer; }
        error = $"MUI {version} is not in the cache ({cacheRoot}). Download it once with: {FetchCommand()} {version}   (MUI is shareware: it is fetched per user and never shipped; the script and its README come with the esharp tool, mui folder)";
        return null;
    }

    /// <summary>
    /// The lines the generated S:Startup-sequence runs before the program, which MUI's installer would otherwise leave behind.
    /// A bare emulated boot volume has no C: and muimaster.library opens asl, commodities, icon, locale, iffparse and more
    /// from the Workbench disk's LIBS:, so the user's own Workbench disk (DF0:, read-only) supplies C: and LIBS:; ENV: goes to
    /// RAM: (MUI keeps its settings there). Then MUI's own drawers (the volume MUI:) are added: LIBS: (muimaster.library
    /// and the classes), LOCALE: and IMAGES:.
    /// </summary>
    public static IReadOnlyList<string> StartupLines() => new[]
    {
        "DF0:C/Assign C: DF0:C",
        "Makedir RAM:T",
        "Assign T: RAM:T",
        "Assign LIBS: DF0:Libs ADD",
        "Makedir RAM:Env",
        "Assign ENV: RAM:Env",
        "Assign LIBS: MUI:Libs ADD",
        "Assign LOCALE: MUI:Locale ADD",
        "Assign IMAGES: MUI:Images",
    };

    /// <summary>FS-UAE config lines: the MUI drawer read-only as the volume MUI:, the user's Workbench disk read-only as DF0:.</summary>
    public static string ConfigLines(string drawer, string workbench) =>
        $"hard_drive_1 = {drawer}\n" +
        "hard_drive_1_label = MUI\n" +
        "hard_drive_1_read_only = 1\n" +
        $"floppy_drive_0 = {workbench}\n" +
        "floppy_drive_0_read_only = 1\n" +
        "hard_drive_0_priority = 20\n";   // boot from the hard disk, not the Workbench floppy

    /// <summary>The Workbench disk for --mui: --workbench or ESHARP_WORKBENCH; an error message if it is missing.</summary>
    public static string? ResolveWorkbench(string? option, out string? error)
    {
        var path = option ?? Environment.GetEnvironmentVariable("ESHARP_WORKBENCH");
        if (string.IsNullOrEmpty(path))
        {
            error = "--mui also needs your own Workbench disk (muimaster.library opens asl, commodities, icon, locale, iffparse from it, and a bare boot has no C:): pass --workbench <Workbench 3.x disk 2 .adf> or set ESHARP_WORKBENCH. It is mounted read-only and never copied.";
            return null;
        }
        path = Path.GetFullPath(path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        if (!File.Exists(path)) { error = $"Workbench disk not found: {path}"; return null; }
        error = null;
        return path;
    }
}
