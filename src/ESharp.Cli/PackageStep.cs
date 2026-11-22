using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ESharp.Compiler;

namespace ESharp.Cli;

/// <summary>
/// The E# library packages of a project (issue #31): after <c>dotnet restore</c>, <c>obj/project.assets.json</c> names every package
/// the csproj references (directly or through another package); those whose DLL carries an E# manifest are compiled with the program.
/// The program's <c>EsAllowCopyleft</c> property (literal value in the csproj, as for the icon properties) allows GPL-family packages.
/// </summary>
public static class PackageStep
{
    public sealed record Result(IReadOnlyList<EsPackage> Packages, bool AllowCopyleft, IReadOnlyList<string> Warnings);

    /// <summary>The packages of the project at <paramref name="path"/> (a directory; a single file has none), or an error message.</summary>
    public static string? Load(string path, out Result result)
    {
        result = new Result(Array.Empty<EsPackage>(), false, Array.Empty<string>());
        if (!Directory.Exists(path)) return null;
        var csproj = Directory.GetFiles(path, "*.csproj").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (csproj == null) return null;

        XDocument doc;
        try { doc = XDocument.Load(csproj); }
        catch (System.Xml.XmlException e) { return $"{csproj}: {e.Message}"; }
        var allow = false;
        var allowText = doc.Descendants().Where(e => e.Name.LocalName == "EsAllowCopyleft").Select(e => e.Value.Trim()).LastOrDefault(v => v.Length > 0);
        if (allowText != null && !bool.TryParse(allowText, out allow))
            return $"{csproj}: EsAllowCopyleft '{allowText}' must be true or false (a literal value: esharp reads the csproj as plain XML)";
        var references = doc.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "").Where(n => n.Length > 0).ToList();
        var others = references.Where(r => !string.Equals(r, SourceTree.SdkPackage, StringComparison.OrdinalIgnoreCase)).ToList();

        var assets = Path.Combine(path, "obj", "project.assets.json");
        var warnings = new List<string>();
        if (!File.Exists(assets))
        {
            if (others.Count == 0) { result = result with { AllowCopyleft = allow }; return null; }
            return $"{Path.GetFileName(csproj)} references {string.Join(", ", others)} but the project has not been restored " +
                   $"(no obj/project.assets.json): run dotnet restore {path} first, so esharp can find the E# library packages";
        }
        if (File.GetLastWriteTimeUtc(csproj) > File.GetLastWriteTimeUtc(assets))
            warnings.Add($"{Path.GetFileName(csproj)} is newer than obj/project.assets.json: run dotnet restore if you changed its package references");

        var errors = new List<string>();
        var packages = PackageLoader.FromAssetsFile(assets, errors);
        if (errors.Count > 0) return string.Join("\n", errors);
        foreach (var name in others.Where(o => !packages.Any(p => string.Equals(p.Id, o, StringComparison.OrdinalIgnoreCase))))
            warnings.Add($"package {name} carries no E# library (no esharp/library.json in its DLL): only its dotnet build is used, nothing of it is compiled into the program");
        result = new Result(packages, allow, warnings);
        return null;
    }

    /// <summary>
    /// ES0112 for the packages that need a newer esharp than <paramref name="toolVersion"/>, as the manifest says: `esharp test` checks this
    /// before the host run loads a package assembly (a library built against a newer Sdk can fail at load before the compile reports it).
    /// </summary>
    public static List<string> SdkErrors(IEnumerable<EsPackage> packages, string toolVersion) =>
        packages.Where(p => p.Manifest != null && SdkVersions.NeedsNewer(p.Manifest.MinSdk, toolVersion))
            .Select(p => Microsoft.CodeAnalysis.Diagnostic.Create(ESharp.Analyzers.Descriptors.PackageSdk, Microsoft.CodeAnalysis.Location.None,
                p.Id, p.Version, p.Manifest!.MinSdk, toolVersion).ToString()).ToList();

    /// <summary>The lines the build prints for the packages: id, version, licence, files, and the platform-level features each one declares.</summary>
    public static IEnumerable<string> Describe(IEnumerable<EsPackage> packages)
    {
        foreach (var p in packages)
        {
            var license = p.License.Length > 0 ? p.License : "no licence expression";
            yield return $"Package {p.Id} {p.Version} ({license}): {p.Sources.Count} C# file(s)";
            if (p.Manifest is { Features.Count: > 0 } m)
                yield return $"  platform-level features: {string.Join(", ", m.Features)}";
            if (p.Manifest is { Symbols.Count: > 0 } d)
                yield return $"  its own symbols: {string.Join(", ", d.Symbols)}";
        }
    }
}
