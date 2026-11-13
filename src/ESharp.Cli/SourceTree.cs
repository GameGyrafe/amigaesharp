using System;
using System.IO;
using System.Reflection;

namespace ESharp.Cli;

/// <summary>
/// Finds what the host side builds against: an E# source tree (a checkout, for contributors) or the host assemblies packed
/// with the tool (AmigaESharp.Tool, folder host/), so an installed tool needs no checkout.
/// </summary>
public static class SourceTree
{
    /// <summary>The NuGet package a game's host project references (runtime, platform, host fakes and the analyzer).</summary>
    public const string SdkPackage = "AmigaESharp.Sdk";

    /// <summary>
    /// The directory holding src/ESharp.Target.Host and src/ESharp.Analyzers. Looked up from ESHARP_ROOT (ESHARP_REPO is a
    /// deprecated alias), then by walking up from <paramref name="start"/> (default: the CLI's own location, then the
    /// current directory). Null when it cannot be found.
    /// </summary>
    public static string? Find(string? start = null)
    {
        if (FromEnvironment() is { } env) return env;
        foreach (var s in start != null ? new[] { start } : new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(s); d != null; d = d.Parent)
                if (IsRoot(d.FullName)) return d.FullName;
        return null;
    }

    /// <summary>The checkout named by ESHARP_ROOT (or the deprecated ESHARP_REPO), when it is one.</summary>
    public static string? FromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ESHARP_ROOT");
        if (string.IsNullOrEmpty(env)) env = Environment.GetEnvironmentVariable("ESHARP_REPO");
        return !string.IsNullOrEmpty(env) && IsRoot(env) ? Path.GetFullPath(env) : null;
    }

    public static bool IsRoot(string dir) =>
        File.Exists(Path.Combine(dir, "src", "ESharp.Target.Host", "ESharp.Target.Host.csproj")) &&
        File.Exists(Path.Combine(dir, "src", "ESharp.Analyzers", "ESharp.Analyzers.csproj"));

    /// <summary>The host assemblies (ESharp.Runtime, ESharp.Platform, ESharp.Target.Host) shipped next to the CLI, or null.</summary>
    public static string? BundledHostLibs(string? cliDir = null)
    {
        var dir = Path.Combine(cliDir ?? AppContext.BaseDirectory, "host");
        return IsHostLibs(dir) ? dir : null;
    }

    public static bool IsHostLibs(string dir) => File.Exists(Path.Combine(dir, "ESharp.Target.Host.dll"));

    /// <summary>
    /// What `esharp test` builds the host run against: ESHARP_ROOT when set, else the assemblies packed with the tool, else a
    /// checkout found from the CLI's location or the current directory. A source tree or a host/ directory; null if none.
    /// </summary>
    public static string? HostSide() => FromEnvironment() ?? BundledHostLibs() ?? Find();

    /// <summary>The CLI's own version (the package version, without a +commit suffix), e.g. 0.1.0 or 0.1.0-dev.</summary>
    public static string ToolVersion
    {
        get
        {
            var asm = typeof(SourceTree).Assembly;
            var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString(3) ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }
}
