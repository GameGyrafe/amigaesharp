using System.Globalization;
using System.Text;

// BindGen: reads the fd files and C prototype headers of an SDK that YOU provide (the m68k toolchain's NDK, the AROS
// SDK include directory) and extracts interface facts: function names, argument types, names and registers, library
// vector numbers and the library version each function first appeared in. It never copies header text, and nothing
// from an SDK is stored in this repository.
//
//   bindgen gen <fd-dir> <lib...> [--include <dir>] [--out <file or dir>] [--docs] [--class Name] [--namespace N]
//                                   write C# declarations in the style of src/ESharp.Platform (one class per library)
//   bindgen gen <fd-dir> <lib...> [--include <dir>] --check <ESharp.Platform dir>
//                                   compare the hand-written [AmigaLib] declarations with the generated ones (name,
//                                   vector, argument count, Since, types); exit 1 on a problem, notes are informational
//   bindgen check <sdk-include-or-fd-dir> <ESharp.Platform dir> [lib...] [--include <dir>]
//                                   the same comparison for every library we bind (types only with prototypes)
//   bindgen facts <sdk-include-or-fd-dir> <lib>                         list name, vector, first version
//   bindgen offsets <spec> <target> --docker <image> | --cc <compiler> [--cflags "..."] [--zune <AROS libraries/mui.h>]
//                                   measure struct offsets with the target's C compiler (MUI structs: "zune NAME" lines,
//                                   the struct's definition read from the AROS Zune header)
//   bindgen layout <aros-offsets.txt> <aros-i386-offsets.txt> <m68k-offsets.txt> [out.cs]   write Layout.g.cs from three measured lists
//   bindgen compare <sdk-include-dir> <fd-dir> <lib...>                 where the AROS headers and the classic fd files disagree
//
// <fd-dir> is ndk/lib/fd of the m68k toolchain; --include is an include dir with clib/<lib>_protos.h (the classic
// NDK form or the AROS macro form), used for types and parameter names. With an AROS include dir as the first
// argument instead, gen takes vectors and versions from the AROS headers (--fd <fd-dir> to use the fd ones).
// The fd files carry the classic first-version markers, which the AROS headers sometimes get wrong.
//
// See docs/BINDINGS_SPIKE.md and issue #18 for why the SDK is read from your own copy.

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length >= 3 && args[0] == "offsets") return Offsets.Run(args.Skip(1).ToArray());
        if (args.Length >= 4 && args[0] == "compare") return Compare(args[1], args[2], args.Skip(3).ToArray());
        if (args.Length >= 4 && args[0] == "layout") return Layout(args.Skip(1).ToArray());
        if (args.Length < 3) return Usage();
        var (pos, opt) = Options(args.Skip(1).ToArray());
        if (pos.Count < 2) return Usage();
        string source = pos[0];
        switch (args[0])
        {
            case "facts":
                foreach (var f in Load(source, pos[1]))
                    Console.WriteLine($"{f.Name}\t{-6 * f.Lvo}\tsince {f.Since}\t{f.Args.Count} args");
                return 0;
            case "gen":
                if (opt.TryGetValue("--check", out var platform)) return Check(source, platform, pos.Skip(1).ToArray(), opt);
                return Gen(source, pos.Skip(1).ToList(), opt);
            case "check":
                return Check(source, pos[1], pos.Skip(2).ToArray(), opt);
            default:
                return Usage();
        }
    }

    private static readonly HashSet<string> Flags = new() { "--docs" };

    private static (List<string> Positional, Dictionary<string, string> Options) Options(string[] args)
    {
        var pos = new List<string>();
        var opt = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (Flags.Contains(args[i])) opt[args[i]] = "";
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length) opt[args[i]] = args[++i];
            else pos.Add(args[i]);
        }
        return (pos, opt);
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: bindgen gen|check|facts|offsets|layout|compare ...   (see the header of Program.cs)");
        return 2;
    }

    public static List<Func> Load(string source, string lib) => Fd.IsFdDir(source) ? Fd.Parse(source, lib) : Protos.Load(source, lib);

    private static bool Has(string source, string lib) =>
        Fd.IsFdDir(source) ? File.Exists(Fd.FilePath(source, lib)) : Protos.Has(source, lib);

    /// <summary>What the generator works from for one library: fd facts with prototype types, or AROS prototypes alone.</summary>
    private static List<Func> Build(string source, string lib, Dictionary<string, string> opt)
    {
        if (Fd.IsFdDir(source))
        {
            var funcs = Fd.Parse(source, lib);
            if (opt.TryGetValue("--include", out var inc))
                funcs = Protos.Has(inc, lib) ? Protos.Merge(funcs, Protos.Load(inc, lib)) : Protos.Merge(funcs, new List<Func>());
            return funcs;
        }
        var protos = Protos.Load(source, lib);
        if (!Protos.IsAros(File.ReadAllText(Protos.PathFor(source, lib), Encoding.Latin1)))
            throw new InvalidOperationException($"{Protos.PathFor(source, lib)} has no vectors: pass the fd directory first and this one as --include");
        return opt.TryGetValue("--fd", out var fd) ? Protos.Merge(Fd.Parse(fd, lib), protos) : protos;
    }

    private static int Gen(string source, List<string> libs, Dictionary<string, string> opt)
    {
        opt.TryGetValue("--out", out var output);
        bool toDir = output != null && (Directory.Exists(output) || libs.Count > 1);
        if (toDir) Directory.CreateDirectory(output!);
        foreach (var lib in libs)
        {
            var o = new BindingWriter.Options(opt.GetValueOrDefault("--class"), opt.ContainsKey("--docs"),
                Fd.IsFdDir(source) ? Fd.BaseNameOf(source, lib) : null, opt.GetValueOrDefault("--namespace") ?? "ESharp.Platform");
            string text = BindingWriter.Write(lib, Build(source, lib, opt), o);
            if (output == null) Console.Write(text);
            else File.WriteAllText(toDir ? Path.Combine(output, BindingWriter.ClassNameFor(lib) + ".g.cs") : output, text);
        }
        return 0;
    }

    private static int Check(string source, string platformDir, string[] only, Dictionary<string, string> opt)
    {
        var mine = PlatformCheck.ParseDir(platformDir);
        int problems = 0, notes = 0, checkedCount = 0;
        foreach (var lib in mine.Select(x => x.Lib).Distinct().Where(l => only.Length == 0 || only.Contains(l)))
        {
            if (!Has(source, lib)) { Console.WriteLine($"{lib}: no fd file or prototypes, skipped"); continue; }
            List<Func> generated;
            try { generated = Build(source, lib, opt); }
            catch (InvalidOperationException e) { Console.WriteLine($"{lib}: skipped ({e.Message})"); continue; }
            var diffs = PlatformCheck.Diff(lib, mine, generated, Fd.IsFdDir(source) || opt.ContainsKey("--fd"));
            int n = mine.Count(x => x.Lib == lib);
            checkedCount += n;
            foreach (var d in diffs.OrderBy(d => !d.Problem)) Console.WriteLine(d);
            problems += diffs.Count(d => d.Problem);
            notes += diffs.Count(d => !d.Problem);
            Console.WriteLine($"{lib}: {n} declarations, {diffs.Count(d => d.Problem)} problem(s), {diffs.Count(d => !d.Problem)} note(s)");
        }
        Console.WriteLine($"checked {checkedCount} declarations, {problems} problem(s), {notes} note(s)");
        return problems == 0 ? 0 : 1;
    }

    private static int Layout(string[] args)
    {
        var text = LayoutWriter.Write(LayoutWriter.ParseMeasured(File.ReadAllText(args[0])), LayoutWriter.ParseMeasured(File.ReadAllText(args[1])),
            LayoutWriter.ParseMeasured(File.ReadAllText(args[2])));
        if (args.Length > 3) File.WriteAllText(args[3], text); else Console.Write(text);
        return 0;
    }

    private static int Compare(string include, string fdDir, string[] libs)
    {
        int problems = 0;
        foreach (var lib in libs)
        {
            if (!Has(fdDir, lib) || !Has(include, lib)) { Console.WriteLine($"{lib}: missing from one side, skipped"); continue; }
            var fd = Fd.Parse(fdDir, lib).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
            var aros = Protos.Load(include, lib).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
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
}
