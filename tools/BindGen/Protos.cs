using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Reads the C prototype headers (<c>clib/&lt;lib&gt;_protos.h</c>) of an SDK YOU provide, for argument and return
/// types and parameter names. Two forms are understood: the classic NDK form (<c>RET Name( TYPE name, ... );</c>,
/// no vectors) and the AROS macro form (<c>AROS_LPn(...)</c>, with vectors and versions). Only facts come out.
/// </summary>
internal static class Protos
{
    public static string PathFor(string include, string lib) => Path.Combine(include, "clib", lib + "_protos.h");

    public static bool Has(string include, string lib) => File.Exists(PathFor(include, lib));

    public static List<Func> Load(string include, string lib)
    {
        string path = PathFor(include, lib);
        if (!File.Exists(path)) throw new FileNotFoundException($"no {path}: pass the SDK's include directory");
        string text = File.ReadAllText(path, Encoding.Latin1);
        return IsAros(text) ? ParseAros(text) : ParseClassic(text);
    }

    public static bool IsAros(string text) => text.Contains("AROS_LP", StringComparison.Ordinal);

    // "#if !defined(__DOS_LIBAPI__) || (36 <= __DOS_LIBAPI__)" then "AROS_LPn(ret, Name, AROS_LPA(type, name, REG)..., LIBBASETYPEPTR, Base, lvo, Lib);"
    private static readonly Regex Entry = new(
        @"#if !defined\(__\w+_LIBAPI__\) \|\| \((\d+) <= __\w+_LIBAPI__\)\s*\n(AROS_LP\d+\w*)\((.*?)\n\);",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Head = new(@"\s*(.+?),\s*(\w+),\s*(?=AROS_LPA|LIBBASETYPEPTR)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ArgRe = new(@"AROS_LPAM?\(\s*(.+?)\s*,\s*(\w+)\s*,\s*(\w+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Tail = new(@"LIBBASETYPEPTR,\s*\w+,\s*(\d+),\s*\w+", RegexOptions.Compiled);

    public static List<Func> ParseAros(string text)
    {
        var list = new List<Func>();
        foreach (Match m in Entry.Matches(text.Replace("\r\n", "\n")))
        {
            string body = m.Groups[3].Value;
            var head = Head.Match(body);
            var tail = Tail.Match(body);
            if (!head.Success || !tail.Success) continue;
            var args = ArgRe.Matches(body).Select(a => new Arg(a.Groups[1].Value.Trim(), a.Groups[2].Value, a.Groups[3].Value.ToUpperInvariant())).ToList();
            list.Add(new Func(head.Groups[2].Value, head.Groups[1].Value.Trim(), args,
                int.Parse(tail.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        }
        return list;
    }

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"//[^\n]*", RegexOptions.Compiled);
    private static readonly Regex Preprocessor = new(@"^[ \t]*#[^\n]*(\\\n[^\n]*)*", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Attribute = new(@"\b(__stdargs|__regargs|__asm|__saveds|__far|__near|extern|register|__reg\(""\w+""\)|__[ad][0-7])\b", RegexOptions.Compiled);

    /// <summary>
    /// Classic prototypes: <c>RET [__stdargs] Name( TYPE name, ... );</c>, one per declaration, possibly over several
    /// lines. Varargs stubs (<c>...</c>) are left out: they are not library vectors. Vector and version are 0 here.
    /// </summary>
    public static List<Func> ParseClassic(string text)
    {
        text = text.Replace("\r\n", "\n");
        text = BlockComment.Replace(text, " ");
        text = LineComment.Replace(text, " ");
        text = Preprocessor.Replace(text, " ");
        text = text.Replace("extern \"C\" {", " ").Replace("extern \"C\"", " ");
        var list = new List<Func>();
        foreach (var raw in text.Split(';'))
        {
            string decl = Regex.Replace(raw.Replace('{', ' ').Replace('}', ' '), @"\s+", " ").Trim();
            int open = decl.IndexOf('(');
            if (open <= 0) continue;
            int close = Matching(decl, open);
            if (close < 0) continue;
            var nameMatch = Regex.Match(decl.Substring(0, open), @"(\w+)\s*$");
            if (!nameMatch.Success) continue;
            string name = nameMatch.Groups[1].Value;
            string ret = Attribute.Replace(decl.Substring(0, nameMatch.Index), " ").Trim();
            if (ret.Length == 0 || ret.Contains('(') || ret.Contains('=')) continue;
            string inside = decl.Substring(open + 1, close - open - 1).Trim();
            if (inside.Contains("...")) continue;
            var args = new List<Arg>();
            if (inside.Length > 0 && inside != "VOID" && inside != "void")
            {
                bool ok = true;
                foreach (var p in SplitTop(inside))
                {
                    var a = Param(Attribute.Replace(p, " ").Trim(), args.Count);
                    if (a == null) { ok = false; break; }
                    args.Add(a);
                }
                if (!ok) continue;
            }
            list.Add(new Func(name, Regex.Replace(ret, @"\s+", " "), args, 0, 0));
        }
        return list;
    }

    private static int Matching(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static List<string> SplitTop(string s)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == ',' && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
        }
        parts.Add(s.Substring(start));
        return parts.Select(p => p.Trim()).ToList();
    }

    // "struct Screen *screen", "ULONG (*func)(VOID)", "CONST_STRPTR name", "LONG" (no name).
    private static Arg? Param(string p, int index)
    {
        if (p.Length == 0) return null;
        var fp = Regex.Match(p, @"\(\s*\*\s*(\w*)\s*\)");
        if (fp.Success) return new Arg("void (*)()", fp.Groups[1].Value.Length > 0 ? fp.Groups[1].Value : "arg" + index, "");
        var m = Regex.Match(p, @"^(.*?[\s\*])(\w+)\s*(\[\s*\w*\s*\])?$");
        if (!m.Success || BindTypes.IsTypeWord(m.Groups[2].Value))
            return new Arg(p, "arg" + index, "");   // a type without a parameter name
        string type = m.Groups[1].Value.Trim() + (m.Groups[3].Success ? " *" : "");
        return new Arg(type, m.Groups[2].Value, "");
    }

    /// <summary>
    /// The fd facts (name, vector, registers, first version) with the prototype's types and parameter names. A function
    /// without a prototype, or whose argument count differs, keeps the fd names and gets empty (unknown) types.
    /// </summary>
    public static List<Func> Merge(List<Func> fd, List<Func> protos)
    {
        var byName = protos.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
        return fd.Select(f =>
        {
            if (!byName.TryGetValue(f.Name, out var p) || p.Args.Count != f.Args.Count)
                return f with { Ret = "", Args = f.Args.Select(x => x with { Type = "" }).ToList() };
            return f with { Ret = p.Ret, Args = f.Args.Select((x, i) => x with { Type = p.Args[i].Type, Name = p.Args[i].Name }).ToList() };
        }).ToList();
    }
}
