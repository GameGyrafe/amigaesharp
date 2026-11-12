using System;
using System.Collections.Generic;

namespace ESharp.Analyzers;

/// <summary>
/// The one place that says which library version each target's system libraries are assumed to have, for the
/// ES0101 check of <c>[Since(n)]</c> bindings. Chosen from the conditional symbols the source is parsed with.
/// </summary>
public static class LibraryVersions
{
    /// <summary>Classic AmigaOS 3.x: Kickstart 3.1 (v40) is the minimum system.</summary>
    public const int AmigaOs3 = 40;

    /// <summary>AROS: the highest library version in its 9.1 headers.</summary>
    public const int Aros = 51;

    /// <summary>
    /// The version to check against, or null when the target is not checked: AmigaOS 4, MorphOS and the sdl
    /// target (their library versions are not verified, or there are no Amiga libraries).
    /// </summary>
    public static int? For(IEnumerable<string> symbols)
    {
        var set = new HashSet<string>(symbols, StringComparer.Ordinal);
        if (set.Contains("AROS")) return Aros;
        if (set.Contains("M68K") && set.Contains("AMIGAOS3")) return AmigaOs3;
        return null;
    }

    /// <summary>"graphics" and "Graphics.library" name the same library.</summary>
    public static bool SameLibrary(string a, string b) =>
        string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);

    private static string Strip(string n) => n.EndsWith(".library", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 8) : n;
}
