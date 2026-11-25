using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ESharp.Compiler;

/// <summary>A library base that a precompiled package's code uses: the program defines the variable (and its platform code opens it).</summary>
public sealed record PackageBase(string Lib, string Name, string CType);

/// <summary>A file of a precompiled binary folder and its SHA-256 (lower-case hex).</summary>
public sealed record BinaryFile(string Name, string Sha256);

/// <summary>
/// A precompiled per-target binary of an E# library package (issue #154): the static library <c>library.a</c> that the pack step
/// (<c>esharp precompile</c>, run by the AmigaESharp.Sdk targets at <c>dotnet pack</c>) built from the package's own sources, and its
/// manifest <c>binary.json</c>, in <c>esharp-bin/&lt;binary target&gt;/</c> beside the package DLL. A program's build links it instead
/// of compiling the package's sources when <see cref="Stamp"/> is this E#'s and <see cref="Config"/> is the build's.
/// </summary>
public sealed record PackageBinary(
    string Id,
    string Version,
    string Target,
    string BinaryTarget,
    string Stamp,
    string Config,
    string Init,
    IReadOnlyList<string> Generics,
    IReadOnlyList<string> Runtime,
    IReadOnlyList<PackageBase> Bases,
    IReadOnlyList<string> Objects,
    IReadOnlyList<BinaryFile> Files,
    string SourcesHash)
{
    /// <summary>The folder the binary was read from (null for one that was just built).</summary>
    public string? Folder { get; init; }

    /// <summary>
    /// The delegate wrappers of methods used as delegates (<c>F__dg</c>) that the binary defines: of the package's own methods. Their
    /// address is the delegate's identity (an event's -= compares it), so the whole link has one of each; the program declares these.
    /// </summary>
    public IReadOnlyList<string> Delegates { get; init; } = Array.Empty<string>();

    /// <summary>The delegate wrappers of the platform's methods that the binary declares only: the program defines them.</summary>
    public IReadOnlyList<string> DelegateUses { get; init; } = Array.Empty<string>();

    /// <summary>The archive's path in <see cref="Folder"/>.</summary>
    public string? Archive => Folder == null ? null : Path.Combine(Folder, PackageBinaries.ArchiveName);
}

/// <summary>What a library build (<see cref="CompileOptions.LibraryBuild"/>) found out: what the binary's manifest records.</summary>
public sealed record LibraryBuildInfo(string Init, IReadOnlyList<string> Generics, IReadOnlyList<string> Runtime, IReadOnlyList<PackageBase> Bases)
{
    /// <summary>See <see cref="PackageBinary.Delegates"/>.</summary>
    public IReadOnlyList<string> Delegates { get; init; } = Array.Empty<string>();

    /// <summary>See <see cref="PackageBinary.DelegateUses"/>.</summary>
    public IReadOnlyList<string> DelegateUses { get; init; } = Array.Empty<string>();
}

/// <summary>Names, files, the E# stamp and the reading and writing of <c>binary.json</c> (issue #154).</summary>
public static class PackageBinaries
{
    /// <summary>The folder beside the package DLL that holds one folder per binary target.</summary>
    public const string FolderName = "esharp-bin";
    public const string ManifestName = "binary.json";
    public const string ArchiveName = "library.a";
    public const int CurrentFormat = 1;

    /// <summary>Extensions of prebuilt code: a package may carry such a file only as a binary the pack step built and listed.</summary>
    public static readonly IReadOnlyList<string> PrebuiltExtensions = new[] { ".o", ".obj", ".a", ".lib", ".so", ".dylib" };

    /// <summary>
    /// The folder name of a target's binaries: the target name, except for sdl, which is a native platform per OS and CPU (like
    /// the runtimes/&lt;rid&gt;/native folders of .NET packages). Only the Linux x86-64 build exists today (the esharp-sdl image).
    /// </summary>
    public static string BinaryTarget(string target) => target == "sdl" ? "sdl-linux-x64" : target;

    /// <summary>
    /// The E# that generated a binary's C: the tool version and the identity of this compiler assembly (its MVID, which the
    /// deterministic .NET build derives from its content, embedded Runtime and Platform sources included). The generated C depends on
    /// both (names, layouts, the runtime), so a binary links only into a program built by the same E#.
    /// </summary>
    public static string Stamp(string toolVersion) => toolVersion + "+" + typeof(PackageBinaries).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>The build configuration a binary is made for: DEBUG (bounds checks) and TRACE change the C, so they are part of it.</summary>
    public static string Config(IEnumerable<string> defines)
    {
        var set = new HashSet<string>(defines, StringComparer.Ordinal);
        return set.Contains("DEBUG") ? "debug" : set.Contains("TRACE") ? "trace" : "release";
    }

    /// <summary>The C function that runs a package's static initializers and static constructors; main calls it.</summary>
    public static string InitName(string id)
    {
        var sb = new StringBuilder("es_pkginit_");
        foreach (var c in id.ToLowerInvariant()) sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_');
        return sb.ToString();
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256File(string path) => Sha256(File.ReadAllBytes(path));

    /// <summary>A hash of what a binary is built from: the package's C# sources and C files (paths and contents).</summary>
    public static string SourcesHash(EsPackage package)
    {
        var sb = new StringBuilder();
        foreach (var s in package.Sources.OrderBy(s => s.Path, StringComparer.Ordinal)) sb.Append("cs ").Append(s.Path).Append('\n').Append(Sha256(Encoding.UTF8.GetBytes(s.Text))).Append('\n');
        foreach (var n in package.Native.OrderBy(n => n.Path, StringComparer.Ordinal)) sb.Append("c ").Append(n.Path).Append('\n').Append(Sha256(Encoding.UTF8.GetBytes(n.Text))).Append('\n');
        return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>The manifest of a binary just built by a library build (<see cref="CompileResult.Library"/>).</summary>
    public static PackageBinary FromBuild(EsPackage package, string target, string stamp, string config, LibraryBuildInfo info,
        IReadOnlyList<string> objects, IReadOnlyList<BinaryFile> files) =>
        new(package.Id, package.Version, target, BinaryTarget(target), stamp, config, info.Init, info.Generics, info.Runtime, info.Bases,
            objects, files, SourcesHash(package)) { Delegates = info.Delegates, DelegateUses = info.DelegateUses };

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(PackageBinary b)
    {
        static string Q(string s) => JsonSerializer.Serialize(s, Json);
        static string L(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(Q)) + "]";
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"format\": {CurrentFormat},\n");
        sb.Append($"  \"id\": {Q(b.Id)},\n  \"version\": {Q(b.Version)},\n");
        sb.Append($"  \"target\": {Q(b.Target)},\n  \"binaryTarget\": {Q(b.BinaryTarget)},\n");
        sb.Append($"  \"esharp\": {Q(b.Stamp)},\n  \"config\": {Q(b.Config)},\n");
        sb.Append($"  \"sources\": {Q(b.SourcesHash)},\n");
        sb.Append($"  \"init\": {Q(b.Init)},\n");
        sb.Append($"  \"generics\": {L(b.Generics)},\n");
        sb.Append($"  \"runtime\": {L(b.Runtime)},\n");
        sb.Append($"  \"delegates\": {L(b.Delegates)},\n");
        sb.Append($"  \"delegateUses\": {L(b.DelegateUses)},\n");
        sb.Append("  \"bases\": [" + string.Join(", ", b.Bases.Select(x => $"{{ \"lib\": {Q(x.Lib)}, \"name\": {Q(x.Name)}, \"ctype\": {Q(x.CType)} }}")) + "],\n");
        sb.Append($"  \"objects\": {L(b.Objects)},\n");
        sb.Append("  \"files\": [" + string.Join(", ", b.Files.Select(f => $"{{ \"name\": {Q(f.Name)}, \"sha256\": {Q(f.Sha256)} }}")) + "]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Reads a <c>binary.json</c>; null with <paramref name="problem"/> set when it cannot be used.</summary>
    public static PackageBinary? Read(string json, out string? problem)
    {
        problem = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { problem = "it is not a JSON object"; return null; }
            string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            List<string> List(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>();
            var format = root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var n) ? n : 0;
            if (format < 1) { problem = "it has no format number"; return null; }
            if (format > CurrentFormat) { problem = $"it has format {format}; this E# reads format {CurrentFormat}"; return null; }
            var bases = new List<PackageBase>();
            if (root.TryGetProperty("bases", out var bv) && bv.ValueKind == JsonValueKind.Array)
                foreach (var x in bv.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    bases.Add(new PackageBase(Str(x, "lib"), Str(x, "name"), Str(x, "ctype")));
            var files = new List<BinaryFile>();
            if (root.TryGetProperty("files", out var fv) && fv.ValueKind == JsonValueKind.Array)
                foreach (var x in fv.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    files.Add(new BinaryFile(Str(x, "name"), Str(x, "sha256").ToLowerInvariant()));
            var b = new PackageBinary(Str(root, "id"), Str(root, "version"), Str(root, "target"), Str(root, "binaryTarget"), Str(root, "esharp"),
                Str(root, "config"), Str(root, "init"), List("generics"), List("runtime"), bases, List("objects"), files, Str(root, "sources"))
                { Delegates = List("delegates"), DelegateUses = List("delegateUses") };
            if (b.Target.Length == 0 || b.BinaryTarget.Length == 0 || b.Stamp.Length == 0 || !IsCName(b.Init))
                problem = "it lacks the target, the E# stamp or the init function";
            else if (!files.Any(x => x.Name == ArchiveName))
                problem = $"it does not list {ArchiveName}";
            else if (b.Generics.Concat(b.Delegates).Concat(b.DelegateUses).Concat(bases.Select(x => x.Name)).Any(x => !IsCName(x)) || bases.Any(x => !IsCName(x.Lib) || x.CType.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '_'))))
                problem = "it names a C symbol that is not a C name";
            return problem == null ? b : null;
        }
        catch (JsonException e)
        {
            problem = "it is not valid JSON: " + e.Message;
            return null;
        }
    }

    private static bool IsCName(string s) => s.Length > 0 && (char.IsAsciiLetter(s[0]) || s[0] == '_') && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>
    /// The binaries in the <see cref="FolderName"/> folder beside a package DLL, checked: every file there is listed in its folder's
    /// <c>binary.json</c> with the SHA-256 it has, the archive holds exactly the objects the manifest names, and the binary is the
    /// package's (id, version, the sources the package carries). Anything else is a problem (ES0118): a package carries no prebuilt
    /// code other than the binaries the E# pack step made from its own sources (which <c>esharp verify-package</c> rebuilds).
    /// </summary>
    public static List<PackageBinary> ReadFolder(string dllDir, EsPackage package, List<string> problems)
    {
        var result = new List<PackageBinary>();
        var root = Path.Combine(dllDir, FolderName);
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.GetFiles(root))
            problems.Add($"it carries '{FolderName}/{Path.GetFileName(file)}', which is not in a target's folder");
        var sources = SourcesHash(package);
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(dir);
            var where = $"{FolderName}/{name}";
            var manifest = Path.Combine(dir, ManifestName);
            if (!File.Exists(manifest)) { problems.Add($"its binary folder '{where}' has no {ManifestName}"); continue; }
            var b = Read(File.ReadAllText(manifest), out var problem);
            if (b == null) { problems.Add($"its {where}/{ManifestName} cannot be used: {problem}"); continue; }
            var bad = new List<string>();
            if (b.BinaryTarget != name) bad.Add($"it is for '{b.BinaryTarget}', not '{name}'");
            if (!string.Equals(b.Id, package.Id, StringComparison.OrdinalIgnoreCase) || b.Version != package.Version) bad.Add($"it is for {b.Id} {b.Version}");
            if (b.SourcesHash != sources) bad.Add("it was not built from the sources this package carries");
            foreach (var f in Directory.GetFiles(dir).Select(Path.GetFileName).Where(f => f != ManifestName).OrderBy(f => f, StringComparer.Ordinal))
            {
                var listed = b.Files.FirstOrDefault(x => x.Name == f);
                if (listed == null) bad.Add($"it carries '{f}', which {ManifestName} does not list (hand-made objects are refused)");
                else if (Sha256File(Path.Combine(dir, f!)) != listed.Sha256) bad.Add($"'{f}' is not the file {ManifestName} lists (SHA-256 differs)");
            }
            foreach (var missing in b.Files.Where(x => !File.Exists(Path.Combine(dir, x.Name))))
                bad.Add($"'{missing.Name}' is missing");
            var archive = Path.Combine(dir, ArchiveName);
            if (File.Exists(archive))
            {
                var members = ArchiveMembers(File.ReadAllBytes(archive));
                if (members == null) bad.Add($"{ArchiveName} is not an ar archive");
                else if (!members.OrderBy(m => m, StringComparer.Ordinal).SequenceEqual(b.Objects.OrderBy(m => m, StringComparer.Ordinal)))
                    bad.Add($"{ArchiveName} holds {string.Join(", ", members)}, not the objects {ManifestName} lists");
            }
            if (bad.Count > 0) { problems.Add($"its binary {where} cannot be used: {string.Join("; ", bad)}"); continue; }
            result.Add(b with { Folder = dir });
        }
        return result;
    }

    /// <summary>
    /// Every file of a restored package folder with a prebuilt-code extension that is not one of the listed binaries: refused (ES0118).
    /// </summary>
    public static IEnumerable<string> StrayPrebuilt(string packageDir, string dllDir, IEnumerable<PackageBinary> binaries)
    {
        // The binary folders beside the DLL are checked by ReadFolder (a file there that binary.json does not list is reported there).
        var binRoot = Path.GetFullPath(Path.Combine(dllDir, FolderName)) + Path.DirectorySeparatorChar;
        var listed = new HashSet<string>(binaries.Where(b => b.Folder != null).SelectMany(b => b.Files.Select(f => Path.GetFullPath(Path.Combine(b.Folder!, f.Name)))),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (!Directory.Exists(packageDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(packageDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!PrebuiltExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            if (listed.Contains(Path.GetFullPath(file)) || Path.GetFullPath(file).StartsWith(binRoot, StringComparison.Ordinal)) continue;
            yield return Path.GetRelativePath(packageDir, file).Replace('\\', '/');
        }
    }

    /// <summary>
    /// The object names in an ar archive (GNU or BSD names; the symbol index and the long-name table are left out), or null when it
    /// is not one.
    /// </summary>
    public static List<string>? ArchiveMembers(byte[] data)
    {
        // The m68k-amigaos binutils write the Amiga hunk form of a link library: one HUNK_UNIT per object, one after the other.
        if (data.Length >= 4 && BigEndian(data, 0) == 0x3E7) return HunkUnits(data);
        const string magic = "!<arch>\n";
        if (data.Length < magic.Length || Encoding.ASCII.GetString(data, 0, magic.Length) != magic) return null;
        var result = new List<string>();
        string? longNames = null;
        var pos = magic.Length;
        while (pos + 60 <= data.Length)
        {
            var header = Encoding.ASCII.GetString(data, pos, 60);
            if (header.Substring(58, 2) != "`\n") return null;
            if (!long.TryParse(header.Substring(48, 10).Trim(), out var size) || size < 0 || pos + 60 + size > data.Length) return null;
            var raw = header.Substring(0, 16).TrimEnd();
            var body = pos + 60;
            string? name;
            if (raw == "/" || raw == "/SYM64/" || raw.StartsWith("__.SYMDEF", StringComparison.Ordinal)) name = null;
            else if (raw == "//") { longNames = Encoding.ASCII.GetString(data, body, (int)size); name = null; }
            else if (raw.StartsWith("/", StringComparison.Ordinal) && int.TryParse(raw.Substring(1), out var offset) && longNames != null && offset < longNames.Length)
            {
                var end = longNames.IndexOf('\n', offset);
                name = (end < 0 ? longNames.Substring(offset) : longNames.Substring(offset, end - offset)).TrimEnd('/');
            }
            else if (raw.StartsWith("#1/", StringComparison.Ordinal) && int.TryParse(raw.Substring(3), out var len) && len <= size)
                name = Encoding.ASCII.GetString(data, body, len).TrimEnd('\0');
            else name = raw.TrimEnd('/');
            if (name is { Length: > 0 } && !name.StartsWith("__.SYMDEF", StringComparison.Ordinal)) result.Add(name);
            pos = body + (int)size;
            if ((pos & 1) != 0) pos++;
        }
        return pos >= data.Length ? result : null;
    }

    private static uint BigEndian(byte[] d, int at) => (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]);

    /// <summary>The unit names of an Amiga hunk link library (HUNK_UNIT blocks and the hunks after each), or null when it does not read as one.</summary>
    private static List<string>? HunkUnits(byte[] d)
    {
        var result = new List<string>();
        var pos = 0;
        bool Has(int longs) => longs >= 0 && pos + 4L * longs <= d.Length;
        uint Next() { var v = BigEndian(d, pos); pos += 4; return v; }
        string Name(int longs) { var s = Encoding.ASCII.GetString(d, pos, longs * 4).TrimEnd('\0'); pos += longs * 4; return s; }
        while (pos < d.Length)
        {
            if (!Has(1)) return null;
            var type = Next() & 0x3FFFFFFF;
            switch (type)
            {
                case 0x3E7:   // HUNK_UNIT
                case 0x3E8:   // HUNK_NAME
                {
                    if (!Has(1)) return null;
                    var n = (int)Next();
                    if (!Has(n)) return null;
                    var name = Name(n);
                    if (type == 0x3E7) result.Add(name);
                    break;
                }
                case 0x3E9: case 0x3EA: case 0x3F1:   // HUNK_CODE, HUNK_DATA, HUNK_DEBUG: a length in longs, then the data
                {
                    if (!Has(1)) return null;
                    var n = (int)(Next() & 0x3FFFFFFF);
                    if (!Has(n)) return null;
                    pos += n * 4;
                    break;
                }
                case 0x3EB:   // HUNK_BSS: a length only
                    if (!Has(1)) return null;
                    pos += 4;
                    break;
                case 0x3EC: case 0x3ED: case 0x3EE: case 0x3F7: case 0x3F8: case 0x3F9:   // HUNK_RELOC32/16/8, HUNK_DREL32/16/8
                    while (true)
                    {
                        if (!Has(1)) return null;
                        var n = (int)Next();
                        if (n == 0) break;
                        if (!Has(n + 1)) return null;
                        pos += (n + 1) * 4;
                    }
                    break;
                case 0x3FC:   // HUNK_RELOC32SHORT: 16-bit counts, hunk numbers and offsets, padded to a long
                {
                    var start = pos;
                    while (true)
                    {
                        if (pos + 2 > d.Length) return null;
                        var n = d[pos] << 8 | d[pos + 1];
                        pos += 2;
                        if (n == 0) break;
                        pos += (n + 1) * 2;
                        if (pos > d.Length) return null;
                    }
                    if (((pos - start) & 3) != 0) pos += 2;
                    break;
                }
                case 0x3EF:   // HUNK_EXT
                    while (true)
                    {
                        if (!Has(1)) return null;
                        var head = Next();
                        if (head == 0) break;
                        var kind = head >> 24;
                        var n = (int)(head & 0xFFFFFF);
                        if (!Has(n)) return null;
                        pos += n * 4;
                        if (kind < 128) { if (!Has(1)) return null; pos += 4; }   // a definition: its value
                        else
                        {
                            if (kind is 130 or 137) { if (!Has(1)) return null; pos += 4; }   // a common symbol: its size
                            if (!Has(1)) return null;
                            var refs = (int)Next();
                            if (!Has(refs)) return null;
                            pos += refs * 4;
                        }
                    }
                    break;
                case 0x3F0:   // HUNK_SYMBOL
                    while (true)
                    {
                        if (!Has(1)) return null;
                        var n = (int)Next();
                        if (n == 0) break;
                        if (!Has(n + 1)) return null;
                        pos += (n + 1) * 4;
                    }
                    break;
                case 0x3F2:   // HUNK_END
                    break;
                default:
                    return null;
            }
        }
        return result;
    }
}
