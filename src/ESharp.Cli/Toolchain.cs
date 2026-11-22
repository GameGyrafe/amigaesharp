using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ESharp.Compiler;

namespace ESharp.Cli;

/// <summary>A target's C compiler on this machine (not in Docker): its path, the sysroot to pass, and where the setting came from.</summary>
public sealed record ToolchainChoice(string Compiler, string? Sysroot, string Source, string? Dir);

/// <summary>
/// A local toolchain: where `esharp build` finds a target's compiler when it does not run it in a Docker image.
/// The folder is the toolchain's install prefix, the one that holds <c>bin/&lt;compiler&gt;</c> (the pinned images keep it
/// under /opt, for example /opt/m68k-amigaos). It comes from, in this order: <c>--toolchain &lt;dir&gt;</c>, the per-target
/// variable (<c>ESHARP_TOOLCHAIN_M68K</c>, <c>_OS4</c>, <c>_MORPHOS</c>, <c>_AROS</c>, <c>_AROS_I386</c>, <c>_SDL</c>), then
/// <c>ESHARP_TOOLCHAIN</c> (Amiga targets). Without any of them the compiler is looked up on PATH, as before.
/// An empty value counts as not set, so a VS Code task can pass <c>--toolchain ${config:esharp.toolchain}</c> unconditionally.
/// </summary>
public static class Toolchain
{
    /// <summary>The kind of machine esharp runs on; a parameter so the Windows and macOS rules (.exe, PATH separator, advice) are tested on any host.</summary>
    public enum HostOs { Linux, MacOS, Windows }

    public static HostOs CurrentOs => OperatingSystem.IsWindows() ? HostOs.Windows : OperatingSystem.IsMacOS() ? HostOs.MacOS : HostOs.Linux;

    public const string EnvAll = "ESHARP_TOOLCHAIN";

    /// <summary>The per-target variable, ESHARP_TOOLCHAIN_M68K for m68k, ESHARP_TOOLCHAIN_AROS_I386 for aros-i386.</summary>
    public static string EnvFor(TargetInfo target) => EnvAll + "_" + Suffix(target);

    /// <summary>The per-target sysroot variable (AROS: ESHARP_SYSROOT_AROS, ESHARP_SYSROOT_AROS_I386).</summary>
    public static string SysrootEnvFor(TargetInfo target) => "ESHARP_SYSROOT_" + Suffix(target);

    private static string Suffix(TargetInfo target) => target.Name.ToUpperInvariant().Replace('-', '_');

    /// <summary>The sysroot inside a toolchain folder laid out as in the pinned image (aros: the folder itself, aros-i386: its sdk folder).</summary>
    public static string? SysrootUnder(TargetInfo target, string dir) =>
        target.Sysroot.Length == 0 ? null
        : target == Targets.ArosI386 ? Path.Combine(dir, "sdk")
        : dir;

    private static string Expand(string path) =>
        Path.GetFullPath(path.StartsWith("~") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..] : path);

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>The file name of a program: <c>.exe</c> added on Windows unless it is already there.</summary>
    public static string Exe(string name, HostOs? os = null) =>
        (os ?? CurrentOs) == HostOs.Windows && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;

    /// <summary>
    /// What to do when no local compiler is found or it has the wrong version, for this kind of machine: where the setup steps are
    /// (docs/GUIDE.md "A local toolchain instead of Docker"). The Linux route needs Docker once; macOS and Windows build the
    /// toolchain (Homebrew, MSYS2 or WSL2) and say where its install prefix is.
    /// </summary>
    public static string SetupAdvice(TargetInfo target, HostOs os)
    {
        if (target != Targets.M68k) return "";
        return os switch
        {
            HostOs.MacOS => " On macOS, build AmigaPorts m68k-amigaos-gcc with Homebrew's tools and pass its install prefix (docs/GUIDE.md, \"Local m68k toolchain on macOS and Windows\").",
            HostOs.Windows => " On Windows, build AmigaPorts m68k-amigaos-gcc in MSYS2 (or use WSL2 and the Linux route) and pass its install prefix, the folder that holds bin\\m68k-amigaos-gcc.exe (docs/GUIDE.md, \"Local m68k toolchain on macOS and Windows\").",
            _ => "",
        };
    }

    /// <summary>The first of <paramref name="name"/> on PATH (with .exe on Windows), or null.</summary>
    public static string? FindOnPath(string name, Func<string, string?>? env = null, HostOs? os = null)
    {
        var path = (env ?? Environment.GetEnvironmentVariable)("PATH") ?? "";
        var windows = (os ?? CurrentOs) == HostOs.Windows;
        var sep = windows ? ';' : ':';
        foreach (var d in path.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            // Windows PATH entries may be quoted or padded; on Linux and macOS a quote or a space is part of the name.
            var dir = windows ? d.Trim().Trim('"') : d;
            if (dir.Length == 0) continue;
            var f = Path.Combine(dir, Exe(name, os));
            if (File.Exists(f)) return f;
        }
        return null;
    }

    /// <summary>
    /// Where <paramref name="target"/>'s compiler is on this machine. <paramref name="option"/> and <paramref name="sysrootOption"/>
    /// are --toolchain and --sysroot (null or empty: not given); <paramref name="cc"/> is --cc (a name looked up in the toolchain
    /// folder or on PATH, or a path). Returns an error message, or null with <paramref name="choice"/> set.
    /// </summary>
    public static string? Resolve(TargetInfo target, string? option, string? sysrootOption, Func<string, string?> env,
        out ToolchainChoice? choice, string? cc = null, HostOs? hostOs = null, bool forBinutils = false)
    {
        var os = hostOs ?? CurrentOs;
        choice = null;
        // "C:\x y\" reaches .NET as C:\x y" (the backslash escapes the closing quote): on Windows drop padding and stray quotes from
        // --toolchain, --sysroot, ESHARP_TOOLCHAIN_<TARGET>, ESHARP_TOOLCHAIN and ESHARP_SYSROOT_*, before the empty check, so a value
        // that is only a quote counts as not given.
        string? Given(string? s) => NonEmpty(os == HostOs.Windows ? s?.Trim().Trim('"').Trim() : s);
        string? dir = null, source = null;
        if (Given(option) is { } o) { dir = o; source = "--toolchain"; }
        else if (Given(env(EnvFor(target))) is { } t) { dir = t; source = EnvFor(target); }
        else if (target.IsAmiga && Given(env(EnvAll)) is { } a) { dir = a; source = EnvAll; }

        var name = cc ?? target.Compiler;
        string compiler;
        if (dir != null)
        {
            dir = Expand(dir);
            if (!Directory.Exists(dir))
                return $"toolchain folder {dir} (from {source}) does not exist; it should be the toolchain's install prefix, the folder " +
                       $"that holds bin/{target.Compiler} (in the pinned image: {ImageDir(target)})" + SetupAdvice(target, os);
            if (Path.IsPathRooted(name)) compiler = name;
            else
            {
                var found = new[] { Path.Combine(dir, "bin", Exe(name, os)), Path.Combine(dir, Exe(name, os)) }.FirstOrDefault(File.Exists);
                if (found == null)
                    return $"no {Exe(name, os)} in {Path.Combine(dir, "bin")} (toolchain folder from {source}): give the toolchain's install " +
                           $"prefix, the folder that holds bin/{target.Compiler} (in the pinned image: {ImageDir(target)}), or {(forBinutils ? "pass" : "build with")} --docker [image]" + SetupAdvice(target, os);
                compiler = found;
            }
        }
        else
        {
            if (Path.IsPathRooted(name) || name.Contains('/') || name.Contains('\\'))
            {
                compiler = Expand(name);
                if (!File.Exists(compiler))
                {
                    // On Windows a --cc without its .exe is still the program.
                    if (os == HostOs.Windows && File.Exists(Exe(compiler, os))) compiler = Exe(compiler, os);
                    else return $"--cc {name}: {compiler} not found";
                }
            }
            else
            {
                var found = FindOnPath(name, env, os);
                if (found == null)
                    return $"{name} not found on PATH. {(forBinutils ? "Pass" : "Build with")} --docker [image] (the default way: the pinned toolchain image), " +
                           $"point --toolchain <dir> or {EnvFor(target)}" + (target.IsAmiga ? $" / {EnvAll}" : "") +
                           " at a local toolchain (the folder that holds bin/" + name + ")" + (forBinutils ? "." : ", or use --emit-only.") + SetupAdvice(target, os);
                compiler = found;
                source = "PATH";
            }
            source ??= "--cc";
        }

        string? sysroot = null;
        if (target.Sysroot.Length > 0 || Given(sysrootOption) != null)
        {
            var given = Given(sysrootOption) is { } so ? (so, "--sysroot") : Given(env(SysrootEnvFor(target))) is { } se ? (se, SysrootEnvFor(target)) : default;
            if (given.Item1 != null)
            {
                sysroot = Expand(given.Item1);
                if (!Directory.Exists(sysroot)) return $"sysroot {sysroot} (from {given.Item2}) does not exist";
            }
            else if (dir != null)
            {
                sysroot = SysrootUnder(target, dir);
                if (sysroot != null && !Directory.Exists(sysroot))
                    return $"no sysroot at {sysroot} for {target.Name} (expected inside the toolchain folder, as {target.Sysroot} in the image): pass --sysroot <dir> or set {SysrootEnvFor(target)}";
            }
            else sysroot = target.Sysroot;   // a compiler on PATH: the image's path, as before (an install under /opt matches it)
        }
        choice = new ToolchainChoice(compiler, sysroot, source!, dir);
        return null;
    }

    /// <summary>Where the pinned image keeps the toolchain (the folder to unpack with docker cp).</summary>
    public static string ImageDir(TargetInfo target) => target.Name switch
    {
        "m68k" => "/opt/m68k-amigaos",
        "os4" => "/opt/ppc-amigaos",
        "morphos" => "/opt/ppc-morphos",
        "aros" => "/opt/x86_64-aros",
        "aros-i386" => "/opt/i386-aros",
        _ => "/usr",
    };

    /// <summary>
    /// Why the version a compiler reported does not do for <paramref name="target"/>, or null. E# is tested with the pinned images'
    /// compilers, so another version is an error unless --any-cc-version is given.
    /// </summary>
    public static string? VersionProblem(TargetInfo target, string compiler, string? reported, HostOs? os = null)
    {
        if (target.CompilerVersion.Length == 0) return null;
        if (reported == null)
            return $"{compiler} did not report a version (-dumpversion); is it a gcc for {target.Name}? Pass --any-cc-version to build anyway" + SetupAdvice(target, os ?? CurrentOs);
        var v = reported.Trim();
        if (v == target.CompilerVersion) return null;
        return $"{compiler} is version {v}, but E# is built and tested with {target.CompilerVersion} for {target.Name} (the pinned Docker image's " +
               "compiler, docs/design/TOOLCHAINS.md). Install that version, or pass --any-cc-version to build anyway (not tested)" + SetupAdvice(target, os ?? CurrentOs);
    }

    /// <summary>What <paramref name="compiler"/> prints for -dumpversion, or null when it cannot be run or fails.</summary>
    public static string? ReadVersion(string compiler)
    {
        try
        {
            var psi = new ProcessStartInfo(compiler, "-dumpversion") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(20000)) { try { p.Kill(true); } catch (InvalidOperationException) { } return null; }
            var text = output.Result.Trim();
            return p.ExitCode == 0 && text.Length > 0 ? text.Split('\n')[0].Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
