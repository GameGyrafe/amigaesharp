using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ESharp.Compiler;

namespace ESharp.Cli;

/// <summary>
/// `esharp doctor`: what this machine can do. Per target, whether it builds here by Docker (the image is present locally) and/or
/// by a local toolchain (compiler found, its version against the pinned image's), then the emulators and VMs (FS-UAE, Xvfb,
/// a Kickstart, the OS4 VM, the hosted AROS images, the MUI cache). Read-only and offline: it runs `docker image inspect`,
/// `-dumpversion` and `--version`, never a pull or a download. Always exits 0.
/// </summary>
public static class Doctor
{
    /// <summary>What the report looks at, so tests can give fakes.</summary>
    public sealed class Probe
    {
        /// <summary>Runs a program and returns (exit code, stdout), or null when it cannot be started.</summary>
        public Func<string, string[], (int Code, string Output)?> Run { get; init; } = RunProcess;
        public Func<string, string?> Env { get; init; } = Environment.GetEnvironmentVariable;
        public Func<string, bool> FileExists { get; init; } = File.Exists;
        public Func<string, string?> VersionOf { get; init; } = Toolchain.ReadVersion;
    }

    public static int Run(string[] args, TextWriter? output = null)
    {
        string? toolchain = null, only = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--toolchain" when i + 1 < args.Length: toolchain = args[++i]; break;
                case "--target" when i + 1 < args.Length: only = args[++i]; break;
                default:
                    Console.Error.WriteLine($"esharp: doctor: unknown option '{args[i]}' (usage: esharp doctor [--target <name> [--toolchain <dir>]])");
                    return 0;
            }
        }
        var o = output ?? Console.Out;
        IEnumerable<TargetInfo> targets = Targets.All;
        if (only != null)
        {
            var t = Targets.Find(only);
            if (t == null) { o.WriteLine($"unknown target '{only}' (known: {string.Join(", ", Targets.All.Select(x => x.Name))})"); return 0; }
            targets = new[] { t };
        }
        else if (toolchain != null) { o.WriteLine("--toolchain needs --target <name> (one toolchain folder is for one target)"); return 0; }
        foreach (var line in Report(new Probe(), targets, toolchain)) o.WriteLine(line);
        return 0;
    }

    /// <summary>The report's lines.</summary>
    public static List<string> Report(Probe p, IEnumerable<TargetInfo> targets, string? toolchainOption = null)
    {
        var lines = new List<string> { $"esharp {SourceTree.ToolVersion} on {Platform()}", "" };

        var docker = p.Run("docker", new[] { "version", "--format", "{{.Server.Version}}" });
        bool dockerOk = docker is { Code: 0 };
        lines.Add("Docker: " + (docker == null ? "not found (builds need --toolchain, a local compiler, or --emit-only)"
            : dockerOk ? $"server {docker.Value.Output.Trim()}"
            : "found, but the daemon does not answer (start Docker, or check that this user may use it)"));
        var imageCache = new Dictionary<string, bool>();
        bool Image(string name)
        {
            if (!dockerOk) return false;
            if (!imageCache.TryGetValue(name, out var have))
                imageCache[name] = have = p.Run("docker", new[] { "image", "inspect", "--format", "{{.Id}}", name }) is { Code: 0 };
            return have;
        }
        string ImageText(string name) => dockerOk ? $"{Short(name)} {(Image(name) ? "present" : "not present")}" : $"{Short(name)} (no Docker)";

        lines.Add("");
        lines.Add("Targets (docker: the image is here; local: a compiler on this machine, --toolchain / ESHARP_TOOLCHAIN_<TARGET> / ESHARP_TOOLCHAIN / PATH):");
        foreach (var t in targets)
        {
            var images = new List<string> { Program.DefaultImage(t) };
            if (t == Targets.M68k) images.Add(ProjectTemplate.DefaultImage);
            var anyImage = images.Any(Image);
            lines.Add($"  {t.Name,-10} docker: {string.Join(", ", images.Select(ImageText))}" + (dockerOk && !anyImage ? $"; {BuildHint(t)}" : ""));

            var error = Toolchain.Resolve(t, toolchainOption, null, p.Env, out var choice);
            string local;
            bool localOk = false;
            if (error != null)
                local = error.Contains("not found on PATH") ? $"not set up ({t.Compiler} not on PATH; set {Toolchain.EnvFor(t)} or pass --toolchain <dir>)" : error;
            else
            {
                var v = p.VersionOf(choice!.Compiler);
                var problem = Toolchain.VersionProblem(t, choice.Compiler, v);
                localOk = problem == null && v != null;
                local = $"{choice.Compiler} {v ?? "(no version)"} (from {choice.Source})" +
                        (t.CompilerVersion.Length == 0 ? "" : problem == null ? ", as in the pinned image" : $", the pinned image has {t.CompilerVersion}: builds need --any-cc-version (not tested)");
                if (choice.Sysroot != null) local += $"; sysroot {choice.Sysroot}" + (Directory.Exists(choice.Sysroot) ? "" : " (missing: pass --sysroot or set " + Toolchain.SysrootEnvFor(t) + ")");
                if (t.PkgConfig.Length > 0)
                {
                    // sdl links SDL 3 through pkg-config: without it the local build cannot link.
                    var pc = p.Run("pkg-config", new[] { "--modversion", t.PkgConfig });
                    if (pc is { Code: 0 }) local += $"; {t.PkgConfig} {pc.Value.Output.Trim()}";
                    else { local += $"; pkg-config {t.PkgConfig}: not found (a local sdl build needs SDL 3 and pkg-config)"; localOk = false; }
                }
            }
            lines.Add($"  {"",-10} local:  {local}");
            lines.Add($"  {"",-10} builds here: {(anyImage || localOk ? string.Join(" and ", new[] { anyImage ? "by Docker" : null, localOk ? "by local toolchain" : null }.Where(x => x != null)) : "no (--emit-only only)")}");
        }

        lines.Add("");
        lines.Add("Emulators and VMs:");
        var fsuae = p.Env("ESHARP_FSUAE") is { Length: > 0 } fe ? fe : Toolchain.FindOnPath("fs-uae", p.Env);
        var fsv = fsuae == null ? null : p.Run(fsuae, new[] { "--version" });
        lines.Add("  FS-UAE (m68k run/test): " + (fsuae == null ? "not found (install FS-UAE, or set ESHARP_FSUAE / pass --fsuae)"
            : $"{fsuae}" + (fsv is { Code: 0 } ? $" {fsv.Value.Output.Trim().Split('\n')[0]}" : fsv == null ? " (cannot be started)" : "")));
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            lines.Add("  Xvfb (headless runs): " + (Toolchain.FindOnPath("Xvfb", p.Env) != null ? "found" : "not found (headless runs need Xvfb, or a DISPLAY)"));
            lines.Add("  xwd (--screenshot): " + (Toolchain.FindOnPath("xwd", p.Env) != null ? "found" : "not found"));
        }
        var ks = p.Env("ESHARP_KICKSTART");
        lines.Add("  Kickstart: " + (string.IsNullOrWhiteSpace(ks) ? "ESHARP_KICKSTART not set (FS-UAE's built-in AROS ROM replacement is used, or pass --kickstart)"
            : p.FileExists(ks) ? "ESHARP_KICKSTART set, the file is there" : "ESHARP_KICKSTART set, but the file is not there"));
        var vm = Os4Vm.FindVm(p.Env("ESHARP_OS4_VM"), out var vmError);
        var qemu = Toolchain.FindOnPath("qemu-system-ppc", p.Env);
        lines.Add("  OS4 VM (os4 run/test): " + (vm == null ? vmError : "ESHARP_OS4_VM set up") + "; qemu-system-ppc " + (qemu != null ? "found" : "not found"));
        foreach (var img in new[] { ArosHosted.ImageX86_64, ArosHosted.ImageI386 })
            lines.Add($"  Hosted AROS ({(img == ArosHosted.ImageI386 ? "aros-i386" : "aros")} test): " + (!dockerOk ? $"{img} (no Docker)"
                : Image(img) ? $"{img} present" : $"{img} not present (docker build {(img == ArosHosted.ImageI386 ? "--build-arg AROS_ARCH=i386 " : "")}-t {img} docker/aros-hosted, in the E# repository)"));
        var cache = Mui.CacheRoot(p.Env);
        var mui = new[] { "3.8", "5" }.Where(v => Directory.Exists(Mui.DrawerPath(v, cache))).ToList();
        lines.Add("  MUI for FS-UAE (--mui): " + (mui.Count > 0 ? "cached: " + string.Join(", ", mui) : "none cached (optional; fetch-mui.sh <3.8|5>)"));
        lines.Add("");
        lines.Add("Nothing was changed or downloaded. docs/GUIDE.md (Building) says how to set up either way.");
        return lines;
    }

    private static string Short(string image) => image.Contains("@sha256:") ? image[..(image.IndexOf("@sha256:") + 20)] + "..." : image;

    private static string BuildHint(TargetInfo t) =>
        t == Targets.ArosI386 ? "docker build -t esharp-aros-i386 docker/aros-i386"
        : t == Targets.Sdl ? "docker build -t esharp-sdl docker/sdl"
        : t == Targets.M68k ? $"docker pull {ProjectTemplate.DefaultImage} (or docker build -t esharp-amiga-toolchain docker)"
        : "docker build -t esharp-amiga-toolchain docker";

    private static string Platform() =>
        (OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "this OS") +
        " " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    private static (int Code, string Output)? RunProcess(string file, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(30000)) { try { proc.Kill(true); } catch (InvalidOperationException) { } return (-1, ""); }
            return (proc.ExitCode, stdout.Result);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
