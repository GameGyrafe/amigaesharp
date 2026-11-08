using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// BindGen: reads the library prototype headers of an SDK that YOU provide (the AROS SDK include
// directory, for example) and extracts interface facts: function names, argument types and registers,
// library vector numbers and the library version each function first appeared in.
// It never copies header text and nothing from the SDK is stored in this repository.
//
//   bindgen check <sdk-include-dir> <ESharp.Platform dir> [lib...]   compare our [AmigaLib] vectors with the SDK
//   bindgen gen   <sdk-include-dir> <lib> [out.cs]                   write C# declarations (a candidate binding)
//   bindgen facts <sdk-include-dir> <lib>                            list name, vector, first version
//   bindgen offsets <spec> <target> --docker <image> | --cc <compiler> [--cflags "..."]
//                                                                    measure struct offsets with the target's C compiler
//   bindgen compare <sdk-include-dir> <fd-dir> <lib...>              where the AROS headers and the classic fd files disagree
//
// Wherever <sdk-include-dir> is accepted, an fd directory (m68k toolchain, ndk/lib/fd) works too. The fd files carry
// the classic first-version markers, which the AROS headers sometimes get wrong (datatypes is all "45" there).
// gen takes --fd <fd-dir> to use the fd vectors and versions with the AROS argument types.
//
// See docs/BINDINGS_SPIKE.md and issue #18 for why the SDK is read from your own copy.

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length >= 3 && args[0] == "offsets") return Offsets.Run(args.Skip(1).ToArray());
        if (args.Length >= 4 && args[0] == "compare") return Compare(args[1], args[2], args.Skip(3).ToArray());
        if (args.Length < 3) return Usage();
        string include = args[1];
        switch (args[0])
        {
            case "facts":
                foreach (var f in Load(include, args[2]))
                    Console.WriteLine($"{f.Name}\t{-6 * f.Lvo}\tsince {f.Since}\t{f.Args.Count} args");
                return 0;
            case "gen":
                int fdAt = Array.IndexOf(args, "--fd");
                var funcs = Load(include, args[2]);
                if (fdAt > 0 && fdAt + 1 < args.Length) funcs = Merge(Fd.Parse(args[fdAt + 1], args[2]), funcs);
                var rest = args.Where((_, i) => i > 2 && (fdAt < 0 || (i != fdAt && i != fdAt + 1))).ToArray();
                var text = Generate(args[2], funcs);
                if (rest.Length > 0) File.WriteAllText(rest[0], text); else Console.Write(text);
                return 0;
            case "check":
                return Check(include, args[2], args.Skip(3).ToArray());
            default:
                return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: bindgen check|gen|facts <sdk-include-dir> ...   (see the header of Program.cs)");
        return 2;
    }

    // "#if !defined(__DOS_LIBAPI__) || (36 <= __DOS_LIBAPI__)" then "AROS_LPn(ret, Name, AROS_LPA(type, name, REG)..., LIBBASETYPEPTR, Base, lvo, Lib);"
    private static readonly Regex Entry = new(
        @"#if !defined\(__\w+_LIBAPI__\) \|\| \((\d+) <= __\w+_LIBAPI__\)\s*\n(AROS_LP\d+\w*)\((.*?)\n\);",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Head = new(@"\s*(.+?),\s*(\w+),\s*(?=AROS_LPA|LIBBASETYPEPTR)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ArgRe = new(@"AROS_LPAM?\(\s*(.+?)\s*,\s*(\w+)\s*,\s*(\w+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Tail = new(@"LIBBASETYPEPTR,\s*\w+,\s*(\d+),\s*\w+", RegexOptions.Compiled);

    public static List<Func> Load(string source, string lib) => Fd.IsFdDir(source) ? Fd.Parse(source, lib) : Parse(source, lib);

    private static bool Has(string source, string lib) =>
        File.Exists(Fd.IsFdDir(source) ? Fd.FilePath(source, lib) : Path.Combine(source, "clib", lib + "_protos.h"));

    // fd facts (vector, version, registers) with the argument and return types of the matching AROS prototype.
    private static List<Func> Merge(List<Func> fd, List<Func> aros)
    {
        var byName = aros.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
        return fd.Select(f =>
        {
            if (!byName.TryGetValue(f.Name, out var a) || a.Args.Count != f.Args.Count)
                return f with { Ret = "IPTR", Args = f.Args.Select(x => x with { Type = "IPTR /* untyped */" }).ToList() };
            return f with { Ret = a.Ret, Args = f.Args.Select((x, i) => x with { Type = a.Args[i].Type }).ToList() };
        }).ToList();
    }

    private static int Compare(string include, string fdDir, string[] libs)
    {
        int problems = 0;
        foreach (var lib in libs)
        {
            if (!Has(fdDir, lib) || !Has(include, lib)) { Console.WriteLine($"{lib}: missing from one side, skipped"); continue; }
            var fd = Fd.Parse(fdDir, lib).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
            var aros = Parse(include, lib).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
            int n = 0;
            foreach (var f in fd.Values.OrderBy(f => f.Lvo))
            {
                if (!aros.TryGetValue(f.Name, out var a)) { Console.WriteLine($"{lib}.{f.Name}: in the fd file (vector {-6 * f.Lvo}), not in the AROS headers"); n++; continue; }
                if (a.Lvo != f.Lvo) { Console.WriteLine($"{lib}.{f.Name}: vector {-6 * f.Lvo} in the fd file, {-6 * a.Lvo} in AROS"); n++; }
                if (a.Args.Count != f.Args.Count) { Console.WriteLine($"{lib}.{f.Name}: {f.Args.Count} arguments in the fd file, {a.Args.Count} in AROS"); n++; }
                if (Math.Max(a.Since, 0) != f.Since && (a.Since <= 40 || f.Since <= 40)) { Console.WriteLine($"{lib}.{f.Name}: first version {f.Since} in the fd file, {a.Since} in AROS"); n++; }
            }
            foreach (var a in aros.Values.Where(a => !fd.ContainsKey(a.Name) && a.Since <= 40).OrderBy(a => a.Lvo))
            { Console.WriteLine($"{lib}.{a.Name}: AROS says version {a.Since} (vector {-6 * a.Lvo}), not in the fd file"); n++; }
            Console.WriteLine($"{lib}: {fd.Count} fd functions, {aros.Count} AROS functions, {n} difference(s)");
            problems += n;
        }
        return problems == 0 ? 0 : 1;
    }

    public static List<Func> Parse(string include, string lib)
    {
        string path = Path.Combine(include, "clib", lib + "_protos.h");
        if (!File.Exists(path)) throw new FileNotFoundException($"no {path}: pass the SDK's include directory");
        string text = File.ReadAllText(path, Encoding.Latin1);
        var list = new List<Func>();
        foreach (Match m in Entry.Matches(text))
        {
            string body = m.Groups[3].Value;
            var head = Head.Match(body);
            var tail = Tail.Match(body);
            if (!head.Success || !tail.Success) continue;
            var args = ArgRe.Matches(body).Select(a => new Arg(a.Groups[1].Value, a.Groups[2].Value, a.Groups[3].Value)).ToList();
            list.Add(new Func(head.Groups[2].Value, head.Groups[1].Value.Trim(), args,
                int.Parse(tail.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        }
        return list;
    }

    // Our declarations: [AmigaLib("lib", -30)] ... Name(args);
    private static readonly Regex Ours = new(@"\[AmigaLib\(""(\w+)"",\s*(-?\d+)\)\][^;{]*?\b(\w+)\s*\(([^)]*)\)\s*;", RegexOptions.Compiled);

    private static int Check(string include, string platformDir, string[] only)
    {
        var mine = new List<(string Lib, string Name, int Lvo, int ArgCount, string File)>();
        foreach (var file in Directory.EnumerateFiles(platformDir, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Ours.Matches(File.ReadAllText(file)))
            {
                string a = m.Groups[4].Value.Trim();
                int n = a.Length == 0 ? 0 : a.Split(',').Length;   // no generics in our extern parameter lists
                mine.Add((m.Groups[1].Value, m.Groups[3].Value, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), n, Path.GetFileName(file)));
            }
        int problems = 0, checkedCount = 0;
        foreach (var lib in mine.Select(x => x.Lib).Distinct().Where(l => only.Length == 0 || only.Contains(l)))
        {
            if (!Has(include, lib)) { Console.WriteLine($"{lib}: no prototypes in the SDK, skipped"); continue; }
            var sdk = Load(include, lib).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
            foreach (var d in mine.Where(x => x.Lib == lib))
            {
                checkedCount++;
                if (!sdk.TryGetValue(d.Name, out var f)) { Console.WriteLine($"{lib}.{d.Name} ({d.File}): not in the SDK"); problems++; continue; }
                if (-6 * f.Lvo != d.Lvo) { Console.WriteLine($"{lib}.{d.Name} ({d.File}): vector {d.Lvo}, SDK says {-6 * f.Lvo}"); problems++; }
                if (f.Args.Count != d.ArgCount) { Console.WriteLine($"{lib}.{d.Name} ({d.File}): {d.ArgCount} arguments, SDK has {f.Args.Count}"); problems++; }
            }
        }
        Console.WriteLine($"checked {checkedCount} declarations, {problems} problem(s)");
        return problems == 0 ? 0 : 1;
    }

    private static readonly Dictionary<string, string> Types = new()
    {
        ["BPTR"] = "nuint", ["LONG"] = "int", ["ULONG"] = "uint", ["WORD"] = "short", ["UWORD"] = "ushort", ["BYTE"] = "sbyte", ["UBYTE"] = "byte",
        ["BOOL"] = "int", ["VOID"] = "void", ["APTR"] = "void*", ["CONST_APTR"] = "void*", ["STRPTR"] = "byte*", ["CONST_STRPTR"] = "string",
        ["IPTR"] = "nuint", ["SIPTR"] = "nint", ["int"] = "int", ["void"] = "void",
    };

    private static string Map(string t)
    {
        t = t.Trim();
        if (Types.TryGetValue(t, out var m)) return m;
        return t.Contains('*') ? "void*" : "nuint /* " + t + " */";   // unknown typedefs are marked for a human to look at
    }

    private static string Generate(string lib, List<Func> funcs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Candidate binding generated by tools/BindGen from an SDK the user provided. Vectors, types and first-version facts only.");
        sb.AppendLine("// Review before use: argument types are mapped coarsely and unknown typedefs are marked.");
        sb.AppendLine("namespace ESharp.Platform;");
        sb.AppendLine();
        sb.AppendLine($"public static unsafe class {char.ToUpperInvariant(lib[0])}{lib.Substring(1)}Candidate");
        sb.AppendLine("{");
        foreach (var f in funcs.OrderBy(f => f.Lvo))
            sb.AppendLine($"    [AmigaLib(\"{lib}\", {-6 * f.Lvo}){(f.Since > 0 ? $", Since({f.Since})" : "")}]\n    public static extern {Map(f.Ret)} {f.Name}({string.Join(", ", f.Args.Select(a => Map(a.Type) + " " + a.Name))});");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
