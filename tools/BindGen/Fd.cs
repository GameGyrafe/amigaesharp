using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Reads the classic function-description files (<c>fd/&lt;lib&gt;_lib.fd</c>) that ship with an m68k toolchain YOU provide.
/// Only facts are taken: name, argument names and registers, vector offset and the first library version
/// (from the "functions in Vnn or higher" markers). Private slots advance the vector but are not returned.
/// </summary>
internal static class Fd
{
    private static readonly Regex Version = new(@"^\*.*\bV(\d+)\b", RegexOptions.Compiled);
    private static readonly Regex FuncRe = new(@"^(\w+)\(([^)]*)\)\(([^)]*)\)", RegexOptions.Compiled);

    public static string FilePath(string dir, string lib) => Path.Combine(dir, lib + "_lib.fd");

    public static bool IsFdDir(string dir) => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*_lib.fd").Any();

    public static List<Func> Parse(string dir, string lib)
    {
        string path = FilePath(dir, lib);
        if (!File.Exists(path)) throw new FileNotFoundException($"no {path}: pass the toolchain's fd directory");
        return ParseText(File.ReadAllText(path, Encoding.Latin1));
    }

    public static List<Func> ParseText(string text)
    {
        var list = new List<Func>();
        int bias = 30, since = 0;
        bool isPrivate = false;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("##bias", StringComparison.Ordinal)) { bias = int.Parse(line.Substring(6).Trim(), CultureInfo.InvariantCulture); continue; }
            if (line == "##private") { isPrivate = true; continue; }
            if (line == "##public") { isPrivate = false; continue; }
            if (line.StartsWith("##", StringComparison.Ordinal)) continue;
            if (line[0] == '*')
            {
                var v = Version.Match(line);
                if (v.Success && line.Contains("higher", StringComparison.OrdinalIgnoreCase)) since = int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }
            var m = FuncRe.Match(line);
            if (!m.Success) continue;
            if (!isPrivate)
            {
                var names = Split(m.Groups[2].Value);
                var regs = Split(m.Groups[3].Value);
                var args = names.Select((n, i) => new Arg("", n, i < regs.Count ? regs[i].ToUpperInvariant() : "")).ToList();
                list.Add(new Func(m.Groups[1].Value, "", args, bias / 6, since));
            }
            bias += 6;
        }
        return list;
    }

    private static List<string> Split(string s) =>
        s.Split(',', '/').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
}
