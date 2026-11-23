using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

namespace ESharp.Compiler;

/// <summary>
/// The manifest an E# library carries as an embedded resource (<see cref="PackageReader.ManifestResource"/>), written by the
/// AmigaESharp.Sdk MSBuild step (buildTransitive/AmigaESharp.Sdk.targets) when the library project sets <c>EsLibrary</c>.
/// </summary>
public sealed record LibraryManifest(
    int Format,
    string Id,
    string Version,
    IReadOnlyList<string> Targets,
    string MinSdk,
    IReadOnlyList<string> Features,
    string License,
    IReadOnlyList<string>? Defines = null,
    PackageAssets? Assets = null,
    PackageNative? Native = null)
{
    /// <summary>
    /// The library's own preprocessor symbols (its <c>DefineConstants</c> without DEBUG, TRACE, RELEASE and HOST): defined for the
    /// library's files only, next to the target's symbols.
    /// </summary>
    public IReadOnlyList<string> Symbols => Defines ?? Array.Empty<string>();

    /// <summary>Symbols that E# sets itself (the target's, and DEBUG/TRACE per build): a library may not define them.</summary>
    public static readonly IReadOnlyList<string> ReservedSymbols = new[]
    {
        "AMIGA", "M68K", "PPC", "X86_64", "I386", "AMIGAOS3", "AMIGAOS4", "MORPHOS", "AROS", "SDL", "HOST", "DEBUG", "TRACE",
    };

    /// <summary>The manifest format this E# reads; a library made for a newer one is refused.</summary>
    public const int CurrentFormat = 1;

    /// <summary>
    /// The platform-level features a library can declare (<c>EsFeatures</c>): raw library bindings, [Native] externs, inline assembly,
    /// interrupt servers, system hook entries, and C files of its own (NativeC: the native/ folder, see <see cref="PackageNative"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> KnownFeatures = new[] { "AmigaLib", "Native", "InlineAsm", "Interrupt", "HookEntry", "NativeC" };

    public bool Declares(string feature) => Features.Any(f => string.Equals(f, feature, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The assets of an E# library (the manifest's <c>"assets"</c>): the library's <c>assets.json</c> was turned into C# by <c>esharp assets</c>
/// before the library was built, and that generated file (<see cref="Source"/>, one of the embedded sources) is the package's asset data:
/// planar <c>[ChipRam]</c> images, palettes and samples in the class <see cref="Namespace"/>.<see cref="Class"/>, compiled with the program
/// on every target as a program's own <c>Assets.g.cs</c> is.
/// </summary>
public sealed record PackageAssets(string Source, string Namespace, string Class)
{
    /// <summary>The asset class with its namespace, as programs name it.</summary>
    public string FullName => Namespace.Length > 0 ? Namespace + "." + Class : Class;
}

/// <summary>
/// The C of an E# library (the manifest's <c>"native"</c>; feature NativeC): the files of the library project's <c>native/common/</c>
/// and <c>native/&lt;target&gt;/</c> folders (<see cref="Files"/>, as "folder/name"), embedded as <c>esharp/native/folder/name</c>.
/// Every function and variable they export, and the C name of each [Native] extern meant for them, starts with <see cref="Prefix"/>.
/// The game's build compiles the common files and those of the chosen target (a target file replaces a common one of the same
/// name) with the program, as C source: prebuilt code is refused (see <see cref="PackageNatives"/>).
/// </summary>
public sealed record PackageNative(string Prefix, IReadOnlyList<string> Files);

/// <summary>One C file or header of a package: <see cref="Folder"/> is <c>common</c> or a target name.</summary>
public sealed record NativeFile(string Folder, string Name, string Text)
{
    public string Path => Folder + "/" + Name;
    public bool IsSource => Name.EndsWith(".c", StringComparison.Ordinal);
}

/// <summary>An E# library package: its manifest and C# sources, read from the package DLL.</summary>
public sealed class EsPackage
{
    public EsPackage(string id, string version, LibraryManifest? manifest, IReadOnlyList<SourceFile> sources, string? dllPath = null,
        IReadOnlyList<string>? problems = null)
    {
        Id = id;
        Version = version;
        Manifest = manifest;
        Sources = sources;
        DllPath = dllPath;
        Problems = problems ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string Version { get; }

    /// <summary>Null when the manifest could not be read (then <see cref="Problems"/> says why).</summary>
    public LibraryManifest? Manifest { get; }

    /// <summary>The embedded sources, with paths relative to the library project (forward slashes).</summary>
    public IReadOnlyList<SourceFile> Sources { get; }

    /// <summary>The package DLL (the HOST build of the library), for the dotnet host run.</summary>
    public string? DllPath { get; }

    /// <summary>What is wrong with the package (each one is an ES0118 error in the build).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The C files and headers the package carries (all folders; <see cref="PackageNatives.Select"/> picks a target's).</summary>
    public IReadOnlyList<NativeFile> Native { get; init; } = Array.Empty<NativeFile>();

    /// <summary>The licence expression: the manifest's, else the one of the package's nuspec (set by the loader).</summary>
    public string License { get; init; } = "";

    /// <summary>Prefix of the paths the package's files are parsed under: diagnostics and #line directives show the package id and version.</summary>
    public string PathPrefix => $"<package {Id} {Version}>/";

    public override string ToString() => $"{Id} {Version}";
}

/// <summary>Reads an E# library from a package DLL: the manifest and the C# sources embedded as resources.</summary>
public static class PackageReader
{
    public const string ManifestResource = "esharp/library.json";
    public const string SourcePrefix = "esharp/src/";
    public const string NativePrefix = "esharp/native/";

    /// <summary>
    /// The E# library in <paramref name="dllPath"/>, or null when the DLL carries no E# manifest (an ordinary .NET assembly, such as
    /// the AmigaESharp.Sdk host libraries). A library that cannot be used comes back with <see cref="EsPackage.Problems"/>.
    /// </summary>
    public static EsPackage? ReadDll(string dllPath, string packageId, string packageVersion)
    {
        Dictionary<string, byte[]> resources;
        try
        {
            resources = Resources(dllPath);
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new EsPackage(packageId, packageVersion, null, Array.Empty<SourceFile>(), dllPath,
                new[] { $"cannot read {Path.GetFileName(dllPath)}: {e.Message}" });
        }
        if (!resources.TryGetValue(ManifestResource, out var manifestBytes)) return null;
        return FromResources(resources, manifestBytes, packageId, packageVersion, dllPath);
    }

    /// <summary>The package from its resources (resource name to bytes); used by <see cref="ReadDll"/> and by tests.</summary>
    public static EsPackage FromResources(IReadOnlyDictionary<string, byte[]> resources, byte[] manifestBytes, string packageId, string packageVersion, string? dllPath = null)
    {
        var problems = new List<string>();
        LibraryManifest? manifest = null;
        try
        {
            manifest = ParseManifest(Encoding.UTF8.GetString(manifestBytes), problems);
        }
        catch (JsonException e)
        {
            problems.Add($"its manifest ({ManifestResource}) is not valid JSON: {e.Message}");
        }
        if (manifest != null && !string.Equals(manifest.Id, packageId, StringComparison.OrdinalIgnoreCase))
            problems.Add($"its manifest names the library '{manifest.Id}', not '{packageId}'");

        var sources = new List<SourceFile>();
        var native = new List<NativeFile>();
        foreach (var (name, bytes) in resources.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (name == ManifestResource || !name.StartsWith("esharp/", StringComparison.Ordinal)) continue;
            if (name.StartsWith(NativePrefix, StringComparison.Ordinal))
            {
                var nativePath = name.Substring(NativePrefix.Length);
                if (PackageNatives.PathProblem(nativePath) is { } bad) problems.Add($"it carries '{name}': {bad}");
                else
                {
                    var slash = nativePath.IndexOf('/');
                    native.Add(new NativeFile(nativePath.Substring(0, slash), nativePath.Substring(slash + 1), Text(bytes)));
                }
                continue;
            }
            if (name.StartsWith("esharp/assets/", StringComparison.Ordinal))
            {
                problems.Add($"it carries the asset input '{name}'; a package's assets are the Assets.g.cs that esharp assets wrote, " +
                             "named in its manifest (images and sounds are not packed; a library made by another tool?)");
                continue;
            }
            if (!name.StartsWith(SourcePrefix, StringComparison.Ordinal))
            {
                problems.Add($"it carries '{name}', which this E# does not know (a library made for a newer E#?)");
                continue;
            }
            var rel = name.Substring(SourcePrefix.Length).Replace('\\', '/');
            if (rel.Length == 0 || rel.StartsWith("/", StringComparison.Ordinal) || rel.Contains(':') || rel.Split('/').Any(s => s is "" or "." or ".."))
            {
                problems.Add($"it carries a source with a bad path '{rel}'");
                continue;
            }
            if (!rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(rel.EndsWith(".c", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
                    ? $"it carries the C file '{rel}' with its C# sources; a package's C goes in native/common/ or native/<target>/ (esharp/native/ in the DLL)"
                    : $"it carries '{rel}', which is not a C# source");
                continue;
            }
            sources.Add(new SourceFile(rel, Text(bytes)));
        }
        if (manifest?.Assets is { } assets && !sources.Any(src => src.Path == assets.Source))
            problems.Add($"its manifest names the asset source '{assets.Source}', which the package does not carry");
        // The C files the manifest lists and those the DLL carries must be the same set: the build prints the list, and a file
        // the manifest does not name would be compiled into the game unannounced.
        var listed = manifest?.Native?.Files ?? Array.Empty<string>();
        foreach (var missing in listed.Where(f => !native.Any(n => n.Path == f)))
            problems.Add($"its manifest lists the C file '{missing}', which the package does not carry");
        foreach (var extra in native.Where(n => !listed.Contains(n.Path)))
            problems.Add($"it carries the C file '{extra.Path}', which its manifest does not list");
        return new EsPackage(packageId, packageVersion, manifest, sources, dllPath, problems) { License = manifest?.License ?? "", Native = native };
    }

    /// <summary>Parses the manifest JSON; what is wrong with it goes to <paramref name="problems"/> (a missing field, an unknown format).</summary>
    public static LibraryManifest? ParseManifest(string json, List<string> problems)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) { problems.Add("its manifest is not a JSON object"); return null; }
        string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        List<string> List(string name)
        {
            if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return new List<string>();
            return v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0).ToList();
        }
        var format = root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var n) ? n : 0;
        if (format < 1) { problems.Add("its manifest has no format number"); return null; }
        if (format > LibraryManifest.CurrentFormat)
        {
            problems.Add($"its manifest has format {format}; this E# reads format {LibraryManifest.CurrentFormat} (update the esharp tool)");
            return null;
        }
        var manifest = new LibraryManifest(format, Str("id"), Str("version"), List("targets"), Str("minSdk"), List("features"), Str("license"),
            List("defines").Distinct(StringComparer.Ordinal).ToList());
        foreach (var symbol in manifest.Symbols)
        {
            if (!IsSymbol(symbol)) problems.Add($"its manifest defines '{symbol}', which is not a preprocessor symbol");
            else if (LibraryManifest.ReservedSymbols.Contains(symbol, StringComparer.Ordinal))
                problems.Add($"its manifest defines '{symbol}', a symbol esharp build sets itself (DefineConstants of the library)");
        }
        if (manifest.Targets.Count == 0) problems.Add("its manifest lists no targets (EsTargets)");
        foreach (var feature in manifest.Features.Where(x => !LibraryManifest.KnownFeatures.Contains(x, StringComparer.OrdinalIgnoreCase)))
            problems.Add($"its manifest declares the unknown feature '{feature}' (known: {string.Join(", ", LibraryManifest.KnownFeatures)})");
        var nativeDeclared = root.TryGetProperty("native", out var native) && native.ValueKind is not (JsonValueKind.Null or JsonValueKind.False);
        if (nativeDeclared)
        {
            string NativeStr(string name) => native.ValueKind == JsonValueKind.Object && native.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            var files = native.ValueKind == JsonValueKind.Object && native.TryGetProperty("files", out var fv) && fv.ValueKind == JsonValueKind.Array
                ? fv.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim().Replace('\\', '/')).ToList() : null;
            var prefix = NativeStr("prefix");
            if (native.ValueKind != JsonValueKind.Object || files == null)
                problems.Add("its manifest declares native C in a form this E# does not know (a library made for a newer E#?)");
            else if (PackageNatives.PrefixProblem(prefix) is { } bad)
                problems.Add($"its manifest gives the native prefix '{prefix}': {bad}");
            else if (files.Count == 0)
                problems.Add("its manifest declares native C without any file");
            else if (files.Select(PackageNatives.PathProblem).FirstOrDefault(x => x != null) is { } badPath)
                problems.Add($"its manifest lists the C file '{files.First(x => PackageNatives.PathProblem(x) != null)}': {badPath}");
            else
                manifest = manifest with { Native = new PackageNative(prefix, files.Distinct(StringComparer.Ordinal).ToList()) };
        }
        if (manifest.Native != null && !manifest.Declares("NativeC"))
            problems.Add("its manifest declares native C but not the NativeC feature (EsFeatures)");
        else if (!nativeDeclared && manifest.Declares("NativeC"))
            problems.Add("its manifest declares the NativeC feature but no C files (a native/ folder)");
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
        {
            string AssetStr(string name) => assets.ValueKind == JsonValueKind.Object && assets.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            var source = AssetStr("source").Replace('\\', '/');
            var ns = AssetStr("namespace");
            var cls = AssetStr("class");
            if (assets.ValueKind != JsonValueKind.Object)
                problems.Add("its manifest declares assets in a form this E# does not know (a library made for a newer E#?)");
            else if (source.Length == 0 || !source.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                problems.Add("its manifest declares assets without the C# source they were generated into");
            else if (!IsSymbol(cls) || ns.Length > 0 && !ns.Split('.').All(IsSymbol))
                problems.Add($"its manifest declares the asset class '{ns}.{cls}', which is not a C# namespace and class name");
            else
                manifest = manifest with { Assets = new PackageAssets(source, ns, cls) };
        }
        return manifest;
    }

    private static bool IsSymbol(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string Text(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
    }

    /// <summary>The managed resources of an assembly, read from its metadata (the assembly is not loaded).</summary>
    public static Dictionary<string, byte[]> Resources(string dllPath)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return result;
        var md = pe.GetMetadataReader();
        var corHeader = pe.PEHeaders.CorHeader;
        if (corHeader == null) return result;
        foreach (var handle in md.ManifestResources)
        {
            var res = md.GetManifestResource(handle);
            if (!res.Implementation.IsNil) continue;   // in another file or assembly
            var name = md.GetString(res.Name);
            if (!name.StartsWith("esharp/", StringComparison.Ordinal)) continue;
            var section = pe.GetSectionData(corHeader.ResourcesDirectory.RelativeVirtualAddress);
            var reader = section.GetReader();
            reader.Offset = checked((int)res.Offset);
            var length = reader.ReadInt32();
            result[name] = reader.ReadBytes(length);
        }
        return result;
    }
}

/// <summary>Finds the E# libraries a restored project references, from its <c>obj/project.assets.json</c>.</summary>
public static class PackageLoader
{
    /// <summary>
    /// Every package of the restore (direct and transitive references) whose DLL (runtime or compile asset) carries an E# manifest. Packages without one
    /// (the AmigaESharp.Sdk host libraries, analyzers, test frameworks) are not compiled into the program and are skipped.
    /// </summary>
    public static List<EsPackage> FromAssetsFile(string assetsJson, List<string> errors)
    {
        var result = new List<EsPackage>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(assetsJson)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            errors.Add($"cannot read {assetsJson}: {e.Message}");
            return result;
        }
        using (doc)
        {
            var root = doc.RootElement;
            var folders = root.TryGetProperty("packageFolders", out var pf) && pf.ValueKind == JsonValueKind.Object
                ? pf.EnumerateObject().Select(p => p.Name).ToList() : new List<string>();
            if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object) return result;
            if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object) return result;
            // One target framework (net10.0); a RID-specific target, if any, holds the same packages.
            var target = targets.EnumerateObject().Select(t => t.Value).FirstOrDefault();
            if (target.ValueKind != JsonValueKind.Object) return result;
            foreach (var entry in target.EnumerateObject().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!entry.Value.TryGetProperty("type", out var type) || type.GetString() != "package") continue;
                var slash = entry.Name.IndexOf('/');
                if (slash <= 0) continue;
                var id = entry.Name.Substring(0, slash);
                var version = entry.Name.Substring(slash + 1);
                if (!libraries.TryGetProperty(entry.Name, out var lib) || !lib.TryGetProperty("path", out var pathElement)) continue;
                var packageDir = folders.Select(f => Path.Combine(f, pathElement.GetString()!.Replace('/', Path.DirectorySeparatorChar)))
                    .FirstOrDefault(Directory.Exists);
                if (packageDir == null) continue;
                // One package, one E# library: a package can list several DLLs that all carry the manifest (lib/ and ref/ for the same
                // assembly, or one per target framework). A real restore lists lib/ under "runtime" and ref/ under "compile", so the
                // runtime assets come first: lib/ is the real assembly the host run loads (a ref/ one may be a reference assembly with the
                // resources stripped). Compile assets are the fallback (a package with only ref/, or lib/ listed there as well).
                var names = new List<string>();
                foreach (var kind in new[] { "runtime", "compile" })
                {
                    if (!entry.Value.TryGetProperty(kind, out var section) || section.ValueKind != JsonValueKind.Object) continue;
                    names.AddRange(section.EnumerateObject().Select(f => f.Name)
                        .Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))   // "_._" placeholders are skipped
                        .OrderBy(n => n.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal));
                }
                var dlls = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var name in dlls)
                {
                    var dll = Path.Combine(packageDir, name.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(dll)) { errors.Add($"package {id} {version}: {dll} is missing (run dotnet restore again)"); continue; }
                    var package = PackageReader.ReadDll(dll, id, version);
                    if (package == null) continue;
                    if (package.License.Length == 0 && NuspecLicense(packageDir, id) is { } nuspecLicense)
                        package = new EsPackage(package.Id, package.Version, package.Manifest, package.Sources, package.DllPath, package.Problems) { License = nuspecLicense, Native = package.Native };
                    result.Add(package);
                    break;   // the first DLL with a manifest is the package
                }
            }
        }
        return result;
    }

    /// <summary>The licence expression of the package's nuspec (as restored into the package folder), or null.</summary>
    public static string? NuspecLicense(string packageDir, string id)
    {
        var nuspec = Path.Combine(packageDir, id.ToLowerInvariant() + ".nuspec");
        if (!File.Exists(nuspec)) return null;
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(nuspec);
            var license = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "license" && (string?)e.Attribute("type") == "expression");
            return license?.Value.Trim() is { Length: > 0 } v ? v : null;
        }
        catch (System.Xml.XmlException) { return null; }
    }
}

/// <summary>SPDX licence expressions: is the code under a GPL-family (GPL, LGPL, AGPL) licence with no other choice?</summary>
public static class Licenses
{
    /// <summary>
    /// True when every choice the expression offers includes a GPL, LGPL or AGPL licence: <c>GPL-3.0-only</c>,
    /// <c>MIT AND LGPL-2.1-or-later</c>, <c>GPL-2.0-only WITH Classpath-exception-2.0</c> are; <c>MIT OR GPL-3.0-only</c> is not
    /// (the MIT choice is free of it). An expression that cannot be parsed counts as copyleft when it names a GPL-family id at all.
    /// </summary>
    public static bool IsCopyleft(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var tokens = Tokenize(expression);
        var pos = 0;
        try
        {
            var result = ParseOr(tokens, ref pos);
            if (pos != tokens.Count) throw new FormatException();
            return result;
        }
        catch (FormatException)
        {
            return tokens.Any(IsGplId);
        }
    }

    public static bool IsGplId(string id)
    {
        var s = id.ToUpperInvariant();
        return s.StartsWith("GPL", StringComparison.Ordinal) || s.StartsWith("LGPL", StringComparison.Ordinal) || s.StartsWith("AGPL", StringComparison.Ordinal);
    }

    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        void Flush() { if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); } }
        foreach (var c in s)
        {
            if (c is '(' or ')') { Flush(); tokens.Add(c.ToString()); }
            else if (char.IsWhiteSpace(c)) Flush();
            else sb.Append(c);
        }
        Flush();
        return tokens;
    }

    private static bool Is(List<string> t, int pos, string word) => pos < t.Count && string.Equals(t[pos], word, StringComparison.OrdinalIgnoreCase);

    // or := and ('OR' and)*: copyleft when every alternative is
    private static bool ParseOr(List<string> t, ref int pos)
    {
        var result = ParseAnd(t, ref pos);
        while (Is(t, pos, "OR")) { pos++; result &= ParseAnd(t, ref pos); }
        return result;
    }

    // and := term ('AND' term)*: copyleft when any part is
    private static bool ParseAnd(List<string> t, ref int pos)
    {
        var result = ParseTerm(t, ref pos);
        while (Is(t, pos, "AND")) { pos++; result |= ParseTerm(t, ref pos); }
        return result;
    }

    // term := '(' or ')' | id ['WITH' exception]
    private static bool ParseTerm(List<string> t, ref int pos)
    {
        if (pos >= t.Count) throw new FormatException();
        if (t[pos] == "(")
        {
            pos++;
            var inner = ParseOr(t, ref pos);
            if (!Is(t, pos, ")")) throw new FormatException();
            pos++;
            return inner;
        }
        if (t[pos] is ")" || Is(t, pos, "AND") || Is(t, pos, "OR") || Is(t, pos, "WITH")) throw new FormatException();
        var id = t[pos++];
        if (Is(t, pos, "WITH")) { pos++; if (pos >= t.Count) throw new FormatException(); pos++; }
        return IsGplId(id);
    }
}

/// <summary>E# SDK versions (major.minor.patch with an optional -suffix).</summary>
public static class SdkVersions
{
    /// <summary>The numeric part, or null when it is not a version.</summary>
    public static Version? Numeric(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(p => !int.TryParse(p, out var n) || n < 0)) return null;
        var nums = parts.Select(int.Parse).Concat(Enumerable.Repeat(0, 3)).Take(3).ToArray();
        return new Version(nums[0], nums[1], nums[2]);
    }

    /// <summary>A development build of E# (0.1.0-dev, a checkout or a local pack without a version): its number says nothing.</summary>
    public static bool IsDev(string version) => version.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a library that needs <paramref name="minSdk"/> (its manifest) is too new for this esharp (<paramref name="have"/>); never on a dev build.</summary>
    public static bool NeedsNewer(string minSdk, string have) =>
        minSdk.Length > 0 && !IsDev(have) && Numeric(minSdk) is { } need && Numeric(have) is { } got && need > got;
}
