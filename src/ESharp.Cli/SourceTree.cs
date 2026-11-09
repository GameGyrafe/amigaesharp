using System;
using System.IO;

namespace ESharp.Cli;

/// <summary>Finds the E# source tree, which `esharp test` and the csproj from `esharp new` build against.</summary>
public static class SourceTree
{
    /// <summary>
    /// The directory holding src/ESharp.Target.Host and src/ESharp.Analyzers. Looked up from ESHARP_ROOT (ESHARP_REPO is a
    /// deprecated alias), then by walking up from <paramref name="start"/> (default: the CLI's own location, then the
    /// current directory). Null when it cannot be found.
    /// </summary>
    public static string? Find(string? start = null)
    {
        var env = Environment.GetEnvironmentVariable("ESHARP_ROOT");
        if (string.IsNullOrEmpty(env)) env = Environment.GetEnvironmentVariable("ESHARP_REPO");
        if (!string.IsNullOrEmpty(env) && IsRoot(env)) return Path.GetFullPath(env);
        foreach (var s in start != null ? new[] { start } : new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(s); d != null; d = d.Parent)
                if (IsRoot(d.FullName)) return d.FullName;
        return null;
    }

    public static bool IsRoot(string dir) =>
        File.Exists(Path.Combine(dir, "src", "ESharp.Target.Host", "ESharp.Target.Host.csproj")) &&
        File.Exists(Path.Combine(dir, "src", "ESharp.Analyzers", "ESharp.Analyzers.csproj"));
}
