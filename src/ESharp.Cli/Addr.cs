using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ESharp.Cli;

/// <summary>
/// <c>esharp addr</c>: maps a crash address of a built m68k program (hunk number and offset, as Guru/Enforcer/SegTracker
/// report it) back to the C# file and line, and the C function. See docs/DEBUGGING.md.
/// <para>
/// The build keeps <c>&lt;name&gt;.o</c> next to the exe: the same C compiled with -g. The hunk exe's own debug section is
/// not readable by the binutils, but the object's DWARF line table is, and it carries the <c>#line</c> information. Hunk 0 of
/// the exe is .text and its offsets equal its addresses; the object's .text sits at a fixed distance inside it, found by
/// comparing where both put <c>_main</c>.
/// </para>
/// </summary>
internal static class Addr
{
    internal sealed record Symbol(long Address, string Section, string Name);

    internal sealed record LineEntry(long Address, string File, int Line);   // Line -1: end of a sequence

    internal sealed record Location(string File, int Line, string? Function);

    private static readonly Regex SymbolRe = new(@"^([0-9a-f]{8})\s+(?:\S+\s+)*?(\.\w+)\s+[0-9a-f]{4}\s+\S+\s+(\S+)\s*$", RegexOptions.Compiled);
    private static readonly Regex LineRe = new(@"^(\S+)\s+(\d+|-)\s+0x([0-9a-f]+)(?:\s+\S+)*\s*$", RegexOptions.Compiled);
    private static readonly Regex HashLineRe = new(@"^#line\s+\d+\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The symbols of <c>objdump -t</c> output (undefined ones are left out).</summary>
    internal static List<Symbol> ParseSymbols(string objdumpT)
    {
        var list = new List<Symbol>();
        foreach (var raw in objdumpT.Split('\n'))
        {
            var m = SymbolRe.Match(raw.TrimEnd('\r'));
            if (m.Success) list.Add(new Symbol(long.Parse(m.Groups[1].Value, NumberStyles.HexNumber), m.Groups[2].Value, m.Groups[3].Value));
        }
        return list;
    }

    /// <summary>The line table of <c>objdump --dwarf=decodedline</c> output.</summary>
    internal static List<LineEntry> ParseLines(string decoded)
    {
        var list = new List<LineEntry>();
        foreach (var raw in decoded.Split('\n'))
        {
            var m = LineRe.Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            list.Add(new LineEntry(long.Parse(m.Groups[3].Value, NumberStyles.HexNumber), m.Groups[1].Value,
                m.Groups[2].Value == "-" ? -1 : int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        }
        return list;
    }

    /// <summary>The file paths named by <c>#line</c> directives in the generated C, to give the line table's bare file names their directory.</summary>
    internal static List<string> SourcePaths(string c) =>
        HashLineRe.Matches(c).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Where the object's .text starts inside the exe's code hunk: _main is in both.</summary>
    internal static long? ObjectBase(List<Symbol> exe, List<Symbol> obj)
    {
        var a = exe.FirstOrDefault(s => s.Name == "_main" && s.Section == ".text");
        var b = obj.FirstOrDefault(s => s.Name == "_main" && s.Section == ".text");
        return a == null || b == null ? null : a.Address - b.Address;
    }

    /// <summary>The source location of an offset in the object's .text, or null if the line table has nothing for it.</summary>
    internal static Location? Find(long offset, List<LineEntry> lines, List<Symbol> objSymbols, List<string> sourcePaths)
    {
        LineEntry? best = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var e = lines[i];
            if (e.Line < 0 || e.Address > offset) continue;
            // Inside a sequence the entry holds until the next one; a sequence ends at its end marker.
            var next = lines.Skip(i + 1).FirstOrDefault(n => n.File == e.File || n.Line < 0);
            if (next != null && next.Address <= offset) continue;
            if (best == null || e.Address >= best.Address) best = e;
        }
        if (best == null) return null;
        var function = objSymbols.Where(s => s.Section == ".text" && s.Address <= offset).OrderByDescending(s => s.Address).FirstOrDefault()?.Name;
        if (function is { Length: > 1 } && function[0] == '_') function = function.Substring(1);
        var path = sourcePaths.Where(p => Path.GetFileName(p) == best.File).ToList();
        return new Location(path.Count == 1 ? path[0] : best.File, best.Line, function);
    }

    private static (int Code, string Output) RunTool(string? docker, string workDir, string program, params string[] args)
    {
        var psi = docker == null ? new ProcessStartInfo(program) : new ProcessStartInfo("docker");
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        if (docker != null)
            foreach (var a in new[] { "run", "--rm", "-v", $"{Path.GetFullPath(workDir)}:/work", "-w", "/work", docker, program })
                psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (docker == null) psi.WorkingDirectory = workDir;
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + (p.ExitCode != 0 ? err.Result : ""));
    }

    internal static bool TryParseAddress(string[] words, out int hunk, out long offset)
    {
        hunk = 0; offset = 0;
        var text = string.Join(":", words.Select(w => w.Trim().TrimEnd(':', ',')));
        var parts = text.Split(new[] { ':', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        var o = parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[1].Substring(2) : parts[1].TrimStart('$');
        return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hunk) && long.TryParse(o, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
    }

    public static int Run(string[] args, TextWriter? output = null)
    {
        var o = output ?? Console.Out;
        string? docker = null, path = null;
        var words = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--docker") docker = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[++i] : "esharp-amiga-toolchain";
            else if (args[i].StartsWith("-") && args[i].Length > 1 && !char.IsDigit(args[i][1])) return Fail($"unknown option '{args[i]}'");
            else if (path == null) path = args[i];
            else words.Add(args[i]);
        }
        if (path == null || !TryParseAddress(words.ToArray(), out var hunk, out var offset))
            return Fail("usage: esharp addr <exe | project dir> <hunk> <offset in hex> [--docker [image]]   (for example: esharp addr obj/m68k/hello 0 e8)");

        var exe = path;
        if (Directory.Exists(path))
        {
            var name = new DirectoryInfo(Path.GetFullPath(path)).Name;
            exe = Path.Combine(path, "obj", "m68k", name);
        }
        exe = Path.GetFullPath(exe);
        var dir = Path.GetDirectoryName(exe)!;
        var obj = exe + ".o";
        var c = exe + ".c";
        if (!File.Exists(exe)) return Fail($"{exe} not found: build for m68k first");
        if (!File.Exists(obj)) return Fail($"{obj} not found: it is written by 'esharp build' for m68k; rebuild");

        var exeName = Path.GetFileName(exe);
        var objName = Path.GetFileName(obj);
        const string tool = "m68k-amigaos-objdump";
        try
        {
            var exeSyms = RunTool(docker, dir, tool, "-t", exeName);
            var objSyms = RunTool(docker, dir, tool, "-t", objName);
            var decoded = RunTool(docker, dir, tool, "--dwarf=decodedline", objName);
            if (exeSyms.Code != 0 || objSyms.Code != 0 || decoded.Code != 0)
                return Fail($"{tool} failed: {exeSyms.Output}{objSyms.Output}{decoded.Output}".TrimEnd());

            var exeList = ParseSymbols(exeSyms.Output);
            var objList = ParseSymbols(objSyms.Output);
            var section = hunk switch { 0 => ".text", 1 => ".data", 2 => ".bss", _ => null };
            o.WriteLine($"hunk {hunk} offset 0x{offset:x}");
            if (section != ".text")
            {
                var near = section == null ? null : exeList.Where(s => s.Section == section && s.Address <= offset).OrderByDescending(s => s.Address).FirstOrDefault();
                o.WriteLine(section == null ? "  no such hunk (the exe has 3: 0 code, 1 data, 2 bss)" : $"  in the {section.Substring(1)} hunk, not code" + (near != null ? $"; nearest symbol {near.Name.TrimStart('_')} + 0x{offset - near.Address:x}" : ""));
                return 0;
            }
            var baseAddr = ObjectBase(exeList, objList);
            if (baseAddr == null) return Fail("cannot find _main in both the exe and its object, so the offsets cannot be related");
            var inObject = offset - baseAddr.Value;
            var loc = inObject < 0 ? null : Find(inObject, ParseLines(decoded.Output), objList, File.Exists(c) ? SourcePaths(File.ReadAllText(c)) : new List<string>());
            var sys = exeList.Where(s => s.Section == ".text" && s.Address <= offset).OrderByDescending(s => s.Address).FirstOrDefault();
            if (loc == null)
            {
                o.WriteLine($"  not in the program's own code" + (sys != null ? $"; nearest symbol {sys.Name.TrimStart('_')} + 0x{offset - sys.Address:x} (startup or library code)" : ""));
                return 0;
            }
            if (loc.Function == "main")
            {
                o.WriteLine("  in the generated main() wrapper (calls the entry point)");
                return 0;
            }
            if (loc.Function != null && loc.Function.StartsWith("Es_ESharp_"))
            {
                // Platform and runtime code is emitted without #line, so the line table only has generated-C line numbers there.
                o.WriteLine($"  in E# runtime/platform code, C function {loc.Function} (no E# source line is kept for it: look at the user code that called it)");
                return 0;
            }
            o.WriteLine($"  {loc.File}:{loc.Line}");
            o.WriteLine($"  C function {loc.Function}");
            if (File.Exists(loc.File))
            {
                var lines = File.ReadAllLines(loc.File);
                if (loc.Line >= 1 && loc.Line <= lines.Length) o.WriteLine($"  {loc.Line,5}: {lines[loc.Line - 1].Trim()}");
            }
            return 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Fail(docker == null ? $"'{tool}' not found: pass --docker <image> (the m68k toolchain image) or put the toolchain on PATH" : "docker not found");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }
}
