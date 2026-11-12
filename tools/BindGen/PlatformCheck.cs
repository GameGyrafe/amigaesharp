using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>A hand-written <c>[AmigaLib]</c> declaration in src/ESharp.Platform.</summary>
internal sealed record Declared(string Lib, int Offset, int Since, string Ret, string Name, IReadOnlyList<(string Type, string Name)> Params, string File);

/// <summary>One difference between a hand-written declaration and the generated one.</summary>
internal sealed record Difference(string Lib, string Name, string File, string What, bool Problem)
{
    public override string ToString() => $"{(Problem ? "problem" : "note")}: {Lib}.{Name} ({File}): {What}";
}

/// <summary>
/// Compares the hand-written bindings with what the generator makes from the fd file and prototypes: name, vector,
/// argument count, first version and each type. Differences that change the call are problems; harmless ones
/// (a widened result, void* for a typed pointer, a lower Since that is still within Kickstart 3.1) are notes.
/// </summary>
internal static class PlatformCheck
{
    // [AmigaLib("lib", -30), Since(36)] (doc comments may not sit between) public static extern RET Name(args);
    private static readonly Regex Decl = new(
        @"\[AmigaLib\(""(?<lib>\w+)"",\s*(?<off>-?\d+)\)(?:\s*,\s*Since\((?<since>\d+)\))?\]\s*(?:(?:public|internal|static|extern|unsafe)\s+)+(?<ret>[\w\*]+)\s+(?<name>\w+)\s*\((?<args>[^)]*)\)\s*;",
        RegexOptions.Compiled);

    /// <summary>The highest library version the m68k build may call without ES0101 (Kickstart 3.1).</summary>
    public const int ClassicVersion = 40;

    public static List<Declared> Parse(string source, string file)
    {
        var list = new List<Declared>();
        foreach (Match m in Decl.Matches(source))
        {
            var ps = new List<(string, string)>();
            string args = Regex.Replace(m.Groups["args"].Value, @"\s+", " ").Trim();
            if (args.Length > 0)
                foreach (var p in args.Split(','))
                {
                    var t = p.Trim();
                    int sp = t.LastIndexOf(' ');
                    ps.Add(sp < 0 ? (t, "") : (t.Substring(0, sp).Replace(" ", ""), t.Substring(sp + 1)));
                }
            list.Add(new Declared(m.Groups["lib"].Value, int.Parse(m.Groups["off"].Value, CultureInfo.InvariantCulture),
                m.Groups["since"].Success ? int.Parse(m.Groups["since"].Value, CultureInfo.InvariantCulture) : 0,
                m.Groups["ret"].Value, m.Groups["name"].Value, ps, file));
        }
        return list;
    }

    public static List<Declared> ParseDir(string platformDir) =>
        Directory.EnumerateFiles(platformDir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => Parse(File.ReadAllText(f), Path.GetFileName(f))).ToList();

    /// <summary>
    /// Differences for one library. <paramref name="generated"/> are the fd functions merged with prototypes (types are
    /// empty when no prototype was given; then only name, vector, count and version are compared).
    /// </summary>
    /// <param name="fromFd">False when the facts come from AROS headers alone: their versions are not the classic ones
    /// (docs/BINDINGS_SPIKE.md), so a Since difference is only a note, and messages say "SDK" instead of "fd file".</param>
    public static List<Difference> Diff(string lib, IEnumerable<Declared> mine, IReadOnlyList<Func> generated, bool fromFd = true)
    {
        string src = fromFd ? "fd file" : "SDK";
        var diffs = new List<Difference>();
        var byName = generated.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
        var byVector = generated.GroupBy(f => -6 * f.Lvo).ToDictionary(g => g.Key, g => g.First());
        foreach (var d in mine.Where(x => x.Lib == lib))
        {
            void Add(string what, bool problem) => diffs.Add(new Difference(lib, d.Name, d.File, what, problem));
            if (!byName.TryGetValue(d.Name, out var f))
            {
                Add(byVector.TryGetValue(d.Offset, out var at) ? $"not in the {src}; vector {d.Offset} is {at.Name}" : $"not in the {src} (vector {d.Offset})", true);
                continue;
            }
            if (-6 * f.Lvo != d.Offset) Add($"vector {d.Offset}, {src} {-6 * f.Lvo}", true);
            if (f.Args.Count != d.Params.Count) { Add($"{d.Params.Count} arguments, {src} {f.Args.Count}", true); continue; }
            if (f.Since != d.Since)
            {
                // A wrong Since matters only across the 3.1 line: it would let through a call 3.1 lacks, or block one it has.
                bool problem = fromFd && Math.Max(f.Since, d.Since) > ClassicVersion;
                Add(d.Since == 0 ? $"no Since, {src} says v{f.Since}" : f.Since == 0 ? $"Since({d.Since}), {src} has no version (the library's first)" : $"Since({d.Since}), {src} v{f.Since}", problem);
            }
            if (f.Ret.Length > 0)
            {
                var r = BindTypes.Compare(f.Ret, d.Ret, true, out bool rp);
                if (r != null) Add("return: " + r, rp);
                for (int i = 0; i < f.Args.Count; i++)
                {
                    var a = BindTypes.Compare(f.Args[i].Type, d.Params[i].Type, false, out bool ap);
                    if (a != null) Add($"argument {i + 1} ({d.Params[i].Name}): {a}", ap);
                }
            }
        }
        return diffs;
    }
}
