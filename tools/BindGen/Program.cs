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
//
// See docs/BINDINGS_SPIKE.md and issue #18 for why the SDK is read from your own copy.

return Cli.Run(args);

internal sealed record Arg(string Type, string Name, string Register);
internal sealed record Func(string Name, string Ret, IReadOnlyList<Arg> Args, int Lvo, int Since);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length < 3) return Usage();
        string include = args[1];
        switch (args[0])
        {
            case "facts":
                foreach (var f in Parse(include, args[2]))
                    Console.WriteLine($"{f.Name}\t{-6 * f.Lvo}\tsince {f.Since}\t{f.Args.Count} args");
                return 0;
            case "gen":
                var text = Generate(args[2], Parse(include, args[2]));
                if (args.Length > 3) File.WriteAllText(args[3], text); else Console.Write(text);
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
            if (!File.Exists(Path.Combine(include, "clib", lib + "_protos.h"))) { Console.WriteLine($"{lib}: no prototypes in the SDK, skipped"); continue; }
            var sdk = Parse(include, lib).ToDictionary(f => f.Name);
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
            sb.AppendLine($"    [AmigaLib(\"{lib}\", {-6 * f.Lvo})] // since v{f.Since}\n    public static extern {Map(f.Ret)} {f.Name}({string.Join(", ", f.Args.Select(a => Map(a.Type) + " " + a.Name))});");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
