using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ESharp.Compiler;

namespace ESharp.Cli;

/// <summary>
/// <c>esharp addr</c>: maps a crash address of a built Amiga program back to the C# file and line, and the C function.
/// See docs/DEBUGGING.md.
/// <para>
/// m68k: the exe is a hunk file. Its hunk table (types, sizes, memory flags) and symbols are read here. The build also writes
/// <c>&lt;name&gt;.map</c> (the linker map: where each input section went) and <c>&lt;name&gt;.o</c> (the same C compiled with -g:
/// the hunk exe's own debug data is not readable by the binutils, but the object's DWARF line table is, and it carries the
/// <c>#line</c> information). An offset in a hunk becomes a place in the linker's address space, then a place in one of the
/// program object's sections: code goes through the object's line table, data and bss (chip or not) to the nearest symbol.
/// </para>
/// <para>
/// ELF targets (AROS x86-64 and i386, AmigaOS 4, MorphOS): the build also links <c>&lt;name&gt;.debug</c>, the same program
/// linked with -g (gcc's code does not change with -g; the tests compare the code sections). The section table and symbols
/// come from the exe; addr2line on the .debug file gives the line (with inlining).
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

    /// <summary>The symbols of <c>objdump -t</c> output (undefined and common ones are left out).</summary>
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

    /// <summary>Where the object's .text starts inside the exe's code hunk: _main is in both (for a build without a linker map).</summary>
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
        return new Location(SourcePath(best.File, sourcePaths), best.Line, function);
    }

    /// <summary>The path a <c>#line</c> directive gave for a file the debug data names (bare, or with the compile directory in front).</summary>
    internal static string SourcePath(string file, List<string> sourcePaths)
    {
        var f = file.Replace('\\', '/');
        var exact = sourcePaths.Where(p => f == p.Replace('\\', '/')).ToList();
        if (exact.Count == 1) return exact[0];
        var tail = sourcePaths.Where(p => f.EndsWith("/" + p.Replace('\\', '/'), StringComparison.Ordinal)).OrderByDescending(p => p.Length).ToList();
        if (tail.Count >= 1) return tail[0];
        var name = sourcePaths.Where(p => Path.GetFileName(p.Replace('\\', '/')) == Path.GetFileName(f)).ToList();
        return name.Count == 1 ? name[0] : file;
    }

    // ---- hunk executables ----

    internal enum HunkKind { Code, Data, Bss }

    /// <summary>One hunk of a hunk executable: its kind, size in bytes, memory type and the symbols of its HUNK_SYMBOL block.</summary>
    internal sealed record Hunk(int Index, HunkKind Kind, long Size, string Memory, List<Symbol> Symbols)
    {
        public string KindName => Kind == HunkKind.Code ? "code" : Kind == HunkKind.Data ? "data" : "bss";
        public string Describe() => $"{KindName}{(Memory == "any" ? "" : $", {Memory} RAM")}, {Size} bytes";
    }

    private const uint HunkHeader = 0x3F3, HunkCode = 0x3E9, HunkData = 0x3EA, HunkBss = 0x3EB, HunkReloc32 = 0x3EC, HunkReloc16 = 0x3ED,
        HunkReloc8 = 0x3EE, HunkSymbolType = 0x3F0, HunkDebug = 0x3F1, HunkEnd = 0x3F2, HunkBreak = 0x3F6, HunkDrel32 = 0x3F7,
        HunkDrel16 = 0x3F8, HunkDrel8 = 0x3F9, HunkReloc32Short = 0x3FC, HunkRelReloc32 = 0x3FD;

    internal static bool IsHunkFile(byte[] data) => data.Length >= 4 && BigU32(data, 0) == HunkHeader;

    private static uint BigU32(byte[] d, long p) => (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);

    /// <summary>
    /// The hunks of a hunk executable (HUNK_HEADER, then CODE/DATA/BSS with their relocations, symbols and debug blocks).
    /// Throws InvalidDataException for a file that is not one or is cut short.
    /// </summary>
    internal static List<Hunk> ReadHunks(byte[] d)
    {
        long p = 0;
        uint Long()
        {
            if (p + 4 > d.Length) throw new InvalidDataException("the hunk file ends early");
            var v = BigU32(d, p);
            p += 4;
            return v;
        }
        ushort Word()
        {
            if (p + 2 > d.Length) throw new InvalidDataException("the hunk file ends early");
            var v = (ushort)(d[p] << 8 | d[p + 1]);
            p += 2;
            return v;
        }
        if (Long() != HunkHeader) throw new InvalidDataException("not a hunk executable (no HUNK_HEADER)");
        while (Long() != 0) { }   // resident library names (none in an executable)
        Long();                   // table size
        var first = Long();
        var last = Long();
        if (last < first || last - first > 4096) throw new InvalidDataException("bad hunk table");
        var sizes = new List<(long Size, string Memory)>();
        for (var i = first; i <= last; i++)
        {
            var w = Long();
            var memory = (w >> 30) switch { 1 => "chip", 2 => "fast", 3 => "special", _ => "any" };
            if (w >> 30 == 3) Long();   // the attribute longword follows
            sizes.Add(((w & 0x3FFFFFFF) * 4L, memory));
        }
        var hunks = new List<Hunk>();
        List<Symbol>? symbols = null;
        while (p < d.Length)
        {
            var type = Long() & 0x3FFFFFFF;
            switch (type)
            {
                case HunkCode or HunkData or HunkBss:
                {
                    var n = Long() & 0x3FFFFFFF;
                    if (type != HunkBss) p += n * 4L;
                    if (p > d.Length) throw new InvalidDataException("the hunk file ends early");
                    var index = hunks.Count;
                    if (index >= sizes.Count) throw new InvalidDataException("more hunks than the hunk table lists");
                    symbols = new List<Symbol>();
                    var kind = type == HunkCode ? HunkKind.Code : type == HunkData ? HunkKind.Data : HunkKind.Bss;
                    hunks.Add(new Hunk(index, kind, sizes[index].Size, sizes[index].Memory, symbols));
                    break;
                }
                case HunkReloc32 or HunkReloc16 or HunkReloc8 or HunkDrel16 or HunkDrel8:
                    for (var n = Long(); n != 0; n = Long()) { Long(); p += n * 4L; }
                    break;
                case HunkDrel32 or HunkReloc32Short or HunkRelReloc32:
                    // Word format (dos.library reads HUNK_DREL32 in an executable as RELOC32SHORT).
                    for (var n = Word(); n != 0; n = Word()) { Word(); p += n * 2L; }
                    if (p % 4 != 0) p += 2;
                    break;
                case HunkSymbolType:
                    for (var w = Long(); w != 0; w = Long())
                    {
                        var len = (w & 0xFFFFFF) * 4L;
                        if (p + len > d.Length) throw new InvalidDataException("the hunk file ends early");
                        var name = Encoding.Latin1.GetString(d, (int)p, (int)len).TrimEnd('\0');
                        p += len;
                        var value = Long();
                        if (hunks.Count > 0) symbols?.Add(new Symbol(value, HunkSection(hunks[^1].Kind), name));
                    }
                    break;
                case HunkDebug:
                {
                    var n = Long();
                    p += n * 4L;
                    break;
                }
                case HunkEnd or HunkBreak:
                    break;
                default:
                    throw new InvalidDataException($"unknown hunk type 0x{type:x} at file offset 0x{p - 4:x}");
            }
        }
        if (hunks.Count != sizes.Count) throw new InvalidDataException($"the hunk table lists {sizes.Count} hunks but the file has {hunks.Count}");
        return hunks;
    }

    private static string HunkSection(HunkKind k) => k == HunkKind.Code ? ".text" : k == HunkKind.Data ? ".data" : ".bss";

    // ---- the GNU ld map of an m68k build ----

    internal sealed record MapSection(string Name, long Address, long Size);

    internal sealed record MapPiece(string Section, long Address, long Size, string File);

    private static readonly Regex MapNumbers = new(@"^\s+0x([0-9a-f]+)\s+0x([0-9a-f]+)(?:\s+(.+?))?\s*$", RegexOptions.Compiled);
    private static readonly Regex MapOutput = new(@"^(\.\S+)(?:\s+0x([0-9a-f]+)\s+0x([0-9a-f]+))?\s*$", RegexOptions.Compiled);
    private static readonly Regex MapInput = new(@"^ (\.\S+|COMMON)(?:\s+0x([0-9a-f]+)\s+0x([0-9a-f]+)\s+(.+?))?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The output sections (in order) and the input pieces of a GNU ld <c>-Map</c> file: which file's section went where, in the
    /// linker's address space (the m68k linker lays the hunks out one after the other there; each hunk starts at 0 in the file).
    /// </summary>
    internal static (List<MapSection> Sections, List<MapPiece> Pieces) ParseMap(string text)
    {
        var sections = new List<MapSection>();
        var pieces = new List<MapPiece>();
        var lines = text.Replace("\r", "").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("Linker script and memory map", StringComparison.Ordinal));
        if (start < 0) return (sections, pieces);
        string? current = null;
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            // A long section name puts its numbers on the next line.
            bool NextNumbers(out long a, out long s, out string file)
            {
                a = s = 0;
                file = "";
                if (i + 1 >= lines.Length) return false;
                var n = MapNumbers.Match(lines[i + 1]);
                if (!n.Success) return false;
                i++;
                a = long.Parse(n.Groups[1].Value, NumberStyles.HexNumber);
                s = long.Parse(n.Groups[2].Value, NumberStyles.HexNumber);
                file = n.Groups[3].Success ? n.Groups[3].Value : "";
                return true;
            }
            var o = MapOutput.Match(line);
            if (o.Success)
            {
                long a, s;
                if (o.Groups[2].Success) { a = long.Parse(o.Groups[2].Value, NumberStyles.HexNumber); s = long.Parse(o.Groups[3].Value, NumberStyles.HexNumber); }
                else if (!NextNumbers(out a, out s, out _)) continue;
                current = o.Groups[1].Value;
                sections.Add(new MapSection(current, a, s));
                continue;
            }
            var m = MapInput.Match(line);
            if (m.Success && current != null)
            {
                long a, s;
                string file;
                if (m.Groups[2].Success)
                {
                    a = long.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                    s = long.Parse(m.Groups[3].Value, NumberStyles.HexNumber);
                    file = m.Groups[4].Value;
                }
                else if (!NextNumbers(out a, out s, out file)) continue;
                if (file.Length > 0 && s > 0) pieces.Add(new MapPiece(m.Groups[1].Value, a, s, file.Trim()));
            }
        }
        return (sections, pieces);
    }

    /// <summary>The output sections that became hunks: the ones with a size (debug sections and empty ones are not hunks).</summary>
    internal static List<MapSection> HunkSections(List<MapSection> sections) =>
        sections.Where(s => s.Size > 0 && !s.Name.StartsWith(".debug", StringComparison.Ordinal) && !s.Name.StartsWith(".stab", StringComparison.Ordinal)
                            && s.Name != ".comment").ToList();

    /// <summary>
    /// The program's own object in the map: an input file that is not in a library archive and not start-up code (crt0),
    /// the one with the most code if there are several.
    /// </summary>
    internal static string? ProgramObject(List<MapPiece> pieces) =>
        pieces.Where(p => !p.File.Contains('(') && !Path.GetFileName(p.File.Replace('\\', '/')).Contains("crt", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.File).OrderByDescending(g => g.Where(p => p.Section == ".text").Sum(p => p.Size)).FirstOrDefault()?.Key;

    // ---- ELF ----

    internal sealed record ElfSection(int Index, string Name, int Type, long Flags, long Address, long Size, long Offset)
    {
        public bool Alloc => (Flags & 2) != 0;
        public bool Code => (Flags & 4) != 0;
        public bool NoBits => Type == 8;
        public string Describe() => $"{(Code ? "code" : NoBits ? "bss" : (Flags & 1) != 0 ? "data" : "read-only data")}, {Size} bytes";
    }

    internal sealed record ElfSymbol(string Name, long Value, long Size, int Section, int Kind);   // Kind: 1 object, 2 function

    internal sealed record ElfImage(bool Is64, int Type, int Machine, List<ElfSection> Sections, List<ElfSymbol> Symbols)
    {
        public bool Relocatable => Type == 1;
    }

    internal static bool IsElf(byte[] d) => d.Length >= 52 && d[0] == 0x7F && d[1] == (byte)'E' && d[2] == (byte)'L' && d[3] == (byte)'F';

    /// <summary>The section table and symbols of an ELF file (32 or 64 bits, either byte order).</summary>
    internal static ElfImage ReadElf(byte[] d)
    {
        if (!IsElf(d)) throw new InvalidDataException("not an ELF file");
        var is64 = d[4] == 2;
        var big = d[5] == 2;
        ulong U(long p, int n)
        {
            if (p < 0 || p + n > d.Length) throw new InvalidDataException("the ELF file ends early");
            ulong v = 0;
            for (var i = 0; i < n; i++) v = big ? v << 8 | d[p + i] : v | (ulong)d[p + i] << (8 * i);
            return v;
        }
        var type = (int)U(16, 2);
        var machine = (int)U(18, 2);
        var shoff = (long)(is64 ? U(0x28, 8) : U(0x20, 4));
        int shentsize = (int)U(is64 ? 0x3A : 0x2E, 2), shnum = (int)U(is64 ? 0x3C : 0x30, 2), shstrndx = (int)U(is64 ? 0x3E : 0x32, 2);
        var raw = new List<(uint Name, int Type, long Flags, long Addr, long Offset, long Size, int Link)>();
        for (var i = 0; i < shnum; i++)
        {
            var b = shoff + (long)i * shentsize;
            raw.Add(is64
                ? ((uint)U(b, 4), (int)U(b + 4, 4), (long)U(b + 8, 8), (long)U(b + 16, 8), (long)U(b + 24, 8), (long)U(b + 32, 8), (int)U(b + 40, 4))
                : ((uint)U(b, 4), (int)U(b + 4, 4), (long)U(b + 8, 4), (long)U(b + 12, 4), (long)U(b + 16, 4), (long)U(b + 20, 4), (int)U(b + 24, 4)));
        }
        string Str(int section, long at)
        {
            if (section <= 0 || section >= raw.Count) return "";
            var p = raw[section].Offset + at;
            if (p < 0 || p >= d.Length) return "";
            var e = p;
            while (e < d.Length && d[e] != 0) e++;
            return Encoding.Latin1.GetString(d, (int)p, (int)(e - p));
        }
        var sections = raw.Select((s, i) => new ElfSection(i, Str(shstrndx, s.Name), s.Type, s.Flags, s.Addr, s.Size, s.Offset)).ToList();
        var symbols = new List<ElfSymbol>();
        foreach (var s in raw)
        {
            if (s.Type != 2) continue;   // SHT_SYMTAB
            var size = is64 ? 24 : 16;
            for (var p = s.Offset + size; p + size <= s.Offset + s.Size; p += size)   // entry 0 is the null symbol
            {
                var name = (uint)U(p, 4);
                long value, sz;
                int info, shndx;
                if (is64) { info = (int)U(p + 4, 1); shndx = (int)U(p + 6, 2); value = (long)U(p + 8, 8); sz = (long)U(p + 16, 8); }
                else { value = (long)U(p + 4, 4); sz = (long)U(p + 8, 4); info = (int)U(p + 12, 1); shndx = (int)U(p + 14, 2); }
                var kind = info & 0xF;
                if (kind is 3 or 4 || shndx == 0 || shndx >= 0xFF00) continue;   // section and file symbols, undefined, absolute, common
                var n = Str(s.Link, name);
                if (n.Length > 0) symbols.Add(new ElfSymbol(n, value, sz, shndx, kind));
            }
        }
        return new ElfImage(is64, type, machine, sections, symbols);
    }

    // ---- what the user typed ----

    /// <summary>
    /// An address as a crash report gives it: a hunk or section (number or name) and an offset, a symbol and an offset
    /// (<c>Es_P_Main+0x48</c>, as AROS and the Grim Reaper print it), or a bare address (an AmigaOS 4 exe, as in the file).
    /// </summary>
    internal sealed record Where(int? Number, string? Name, string? Symbol, long Offset, bool Absolute);

    private static readonly Regex SymbolPlus = new(@"([A-Za-z_][\w.$]*)\s*(?:\(\))?\s*\+\s*(?:0x|\$)?([0-9a-fA-F]+)\b", RegexOptions.Compiled);
    private static readonly Regex AnyFunction = new(@"^\s*Function\s+([A-Za-z_][\w.$]*)\s*(?:\([^)]*\))?\s*Offset\s+(?:0x)?([0-9a-fA-F]+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase) { "hunk", "offset", "section", "segment", "@", "at" };

    internal static bool TryParseWhere(string[] words, out Where where)
    {
        where = new Where(null, null, null, 0, false);
        var text = string.Join(" ", words);
        text = Regex.Replace(text, @"^\s*Module\s+\S+\s+", "", RegexOptions.IgnoreCase);   // AROS: "Module crashx Segment 1 .text (0x...) Offset 0x698"
        var function = AnyFunction.Match(text);   // AROS: "Function Es_P_Main (0x...) Offset 0x48"
        if (function.Success)
        {
            where = new Where(null, null, function.Groups[1].Value, long.Parse(function.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), false);
            return true;
        }
        var sym = SymbolPlus.Match(text);
        if (sym.Success && !Regex.IsMatch(sym.Groups[1].Value, @"^(0x)?[0-9a-fA-F]+$"))
        {
            where = new Where(null, null, sym.Groups[1].Value, long.Parse(sym.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), false);
            return true;
        }
        text = Regex.Replace(text, @"\([^)]*\)", " ");   // "(0x0000000041F810C0)" in an AROS report
        var tokens = text.Split(new[] { ':', '+', ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries).Where(t => !Filler.Contains(t)).ToList();
        static bool Hex(string t, out long v)
        {
            var s = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t.Substring(2) : t.TrimStart('$');
            v = 0;
            return s.Length > 0 && long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }
        if (tokens.Count == 1)
        {
            if (!Hex(tokens[0], out var a)) return false;
            where = new Where(null, null, null, a, true);
            return true;
        }
        if (tokens.Count is < 2 or > 3 || !Hex(tokens[^1], out var offset)) return false;
        int? number = null;
        string? name = null;
        foreach (var t in tokens.Take(tokens.Count - 1))
        {
            if (number == null && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) number = n;
            else if (name == null && t.StartsWith('.') && t.Length > 1) name = t;
            else return false;
        }
        where = new Where(number, name, null, offset, false);
        return true;
    }

    /// <summary>Hunk number and offset only (the m68k form): <c>0 e8</c>, <c>0:e8</c>, <c>0:0xe8</c>, <c>0000 $000000E8</c>.</summary>
    internal static bool TryParseAddress(string[] words, out int hunk, out long offset)
    {
        hunk = 0; offset = 0;
        if (!TryParseWhere(words, out var w) || w.Number == null || w.Name != null || w.Symbol != null || w.Absolute) return false;
        hunk = w.Number.Value;
        offset = w.Offset;
        return true;
    }

    // ---- running the binutils ----

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

    /// <summary>The binutils prefix of a target (its compiler name without "gcc").</summary>
    internal static string ToolPrefix(TargetInfo t) => t.Compiler.EndsWith("gcc", StringComparison.Ordinal) ? t.Compiler.Substring(0, t.Compiler.Length - 3) : t.Compiler + "-";

    private const string Usage =
        "usage: esharp addr <exe | project dir> [<where>] [--target <t>] [--docker [image] | --toolchain <dir>]\n" +
        "  <where> is what the crash report gives: m68k '<hunk> <hex offset>' (0 e8); AROS '<segment> <offset>' or '.text <offset>';\n" +
        "  AmigaOS 4 '<section> <offset>' or an address in the file; on any target 'Function+0x48'. With no <where> the hunks or sections are listed.";

    public static int Run(string[] args, TextWriter? output = null)
    {
        var o = output ?? Console.Out;
        string? docker = null, path = null, toolchain = null, targetName = null;
        var words = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--toolchain" && i + 1 < args.Length) toolchain = args[++i];
            else if (args[i] == "--target" && i + 1 < args.Length) targetName = args[++i];
            else if (args[i] == "--docker") docker = i + 1 < args.Length && !args[i + 1].StartsWith("-") && !LooksLikeWhere(args[i + 1]) ? args[++i] : "";
            else if (args[i].StartsWith("-") && args[i].Length > 1 && !char.IsDigit(args[i][1])) return Fail($"unknown option '{args[i]}'");
            else if (path == null) path = args[i];
            else words.Add(args[i]);
        }
        if (path == null) return Fail(Usage);
        Where? where = null;
        if (words.Count > 0)
        {
            if (!TryParseWhere(words.ToArray(), out var w)) return Fail($"cannot read the address '{string.Join(" ", words)}'\n{Usage}");
            where = w;
        }
        TargetInfo? target = null;
        if (targetName != null)
        {
            target = Targets.Find(targetName);
            if (target == null || !target.IsAmiga) return Fail($"unknown Amiga target '{targetName}' (known: {string.Join(", ", Targets.All.Where(t => t.IsAmiga).Select(t => t.Name))})");
        }

        var exe = path;
        if (Directory.Exists(path))
        {
            var name = new DirectoryInfo(Path.GetFullPath(path)).Name;
            if (target == null)
            {
                var built = Targets.All.Where(t => t.IsAmiga && File.Exists(Path.Combine(path, "obj", t.Name, name))).ToList();
                if (built.Count == 0) return Fail($"no build of {name} in {Path.Combine(path, "obj")}: build it for an Amiga target first");
                target = built[0];   // m68k first: Targets.All lists it first
                if (built.Count > 1) o.WriteLine($"({target.Name} build; there are also {string.Join(", ", built.Skip(1).Select(t => t.Name))}: pass --target for another)");
            }
            exe = Path.Combine(path, "obj", target.Name, name);
        }
        exe = Path.GetFullPath(exe);
        if (!File.Exists(exe)) return Fail($"{exe} not found: build it first");
        var bytes = File.ReadAllBytes(exe);
        try
        {
            if (target == null)
            {
                var dirName = Path.GetFileName(Path.GetDirectoryName(exe)!);
                target = Targets.All.FirstOrDefault(t => t.IsAmiga && t.Name == dirName);
                if (target == null && IsHunkFile(bytes)) target = Targets.M68k;
                if (target == null && IsElf(bytes))
                    target = ReadElf(bytes).Machine switch { 62 => Targets.Aros, 3 => Targets.ArosI386, 20 => Targets.Os4, _ => null };
                if (target == null) return Fail($"cannot tell which target {exe} is for: pass --target");
            }
            if (docker != null && !string.IsNullOrWhiteSpace(toolchain)) return Fail("--docker and --toolchain both say where the binutils are: use one");
            if (docker == "") docker = Program.DefaultImage(target);
            if (IsHunkFile(bytes)) return RunHunk(o, exe, bytes, where, target, docker, toolchain);
            if (IsElf(bytes)) return RunElf(o, exe, bytes, where, target, docker, toolchain);
            return Fail($"{exe} is neither a hunk executable nor an ELF file");
        }
        catch (InvalidDataException e)
        {
            return Fail($"{exe}: {e.Message}");
        }
    }

    /// <summary>Whether a word after --docker is the address rather than an image name (a bare --docker takes the target's image).</summary>
    private static bool LooksLikeWhere(string s) =>
        Regex.IsMatch(s, @"^(\d+|0x[0-9a-fA-F]+|\$[0-9a-fA-F]+|\.\w+|\d+:\S+)$") || s.Contains('+') || s.Contains(' ') || s.Contains('(')
        || Filler.Contains(s) || s.Equals("module", StringComparison.OrdinalIgnoreCase) || s.Equals("function", StringComparison.OrdinalIgnoreCase);

    /// <summary>The path of one of the target's binutils: in the image as is, else from --toolchain, ESHARP_TOOLCHAIN* or PATH.</summary>
    private static string? Tool(TargetInfo target, string name, string? docker, string? toolchain, out string? error)
    {
        error = null;
        var tool = ToolPrefix(target) + name;
        if (docker != null) return tool;
        if (Toolchain.Resolve(target, toolchain, null, Environment.GetEnvironmentVariable, out var choice, tool, forBinutils: true) is { } e) { error = e; return null; }
        return choice!.Compiler;
    }

    private static int ToolMissing(string tool, string? docker) =>
        Fail(docker == null ? $"'{tool}' not found: pass --docker [image] (the target's toolchain image) or --toolchain <dir>, or put the toolchain on PATH" : "docker not found");

    private static List<string> SourcesOf(string exe) => File.Exists(exe + ".c") ? SourcePaths(File.ReadAllText(exe + ".c")) : new List<string>();

    private static void PrintSource(TextWriter o, string file, int line)
    {
        if (!File.Exists(file)) return;
        var lines = File.ReadAllLines(file);
        if (line >= 1 && line <= lines.Length) o.WriteLine($"  {line,5}: {lines[line - 1].Trim()}");
    }

    private static string Plain(string symbol) => symbol.Length > 1 && symbol[0] == '_' ? symbol.Substring(1) : symbol;

    // ---- m68k ----

    private static int RunHunk(TextWriter o, string exe, byte[] bytes, Where? where, TargetInfo target, string? docker, string? toolchain)
    {
        var hunks = ReadHunks(bytes);
        var dir = Path.GetDirectoryName(exe)!;
        if (where == null)
        {
            o.WriteLine($"{Path.GetFileName(exe)}: {hunks.Count} hunks");
            foreach (var h in hunks) o.WriteLine($"  hunk {h.Index}: {h.Describe()}");
            return 0;
        }
        int hunkIndex;
        long offset;
        if (where.Symbol != null)
        {
            var hit = hunks.SelectMany(h => h.Symbols.Select(s => (Hunk: h, Sym: s))).FirstOrDefault(x => x.Sym.Name == where.Symbol || Plain(x.Sym.Name) == where.Symbol);
            if (hit.Sym == null) return Fail($"no symbol {where.Symbol} in {Path.GetFileName(exe)} (the exe keeps global symbols only; give the hunk and offset)");
            hunkIndex = hit.Hunk.Index;
            offset = hit.Sym.Address + where.Offset;
        }
        else if (where.Number != null && where.Name == null && !where.Absolute)
        {
            hunkIndex = where.Number.Value;
            offset = where.Offset;
        }
        else return Fail("an m68k address is '<hunk> <hex offset>' (as SegTracker and Enforcer print it) or 'Symbol+0x<offset>'");

        o.WriteLine($"hunk {hunkIndex} offset 0x{offset:x}");
        if (hunkIndex < 0 || hunkIndex >= hunks.Count)
        {
            o.WriteLine($"  no such hunk: the exe has {hunks.Count} ({string.Join("; ", hunks.Select(h => $"{h.Index} {h.Describe()}"))})");
            return 0;
        }
        var hunk = hunks[hunkIndex];
        o.WriteLine($"  hunk {hunk.Index}: {hunk.Describe()}");
        if (offset >= hunk.Size)
        {
            o.WriteLine($"  past the end of the hunk ({hunk.Size} bytes)");
            return 0;
        }
        var nearExe = hunk.Symbols.Where(s => s.Address <= offset && s.Address < hunk.Size).OrderByDescending(s => s.Address).FirstOrDefault();
        string NearExe() => nearExe != null ? $"nearest symbol {Plain(nearExe.Name)} + 0x{offset - nearExe.Address:x}" : "no symbol before it";

        // Where the linker put this hunk and the program's sections (the map of the same build).
        var mapFile = exe + ".map";
        MapPiece? piece = null;
        long inPiece = 0;
        string? mapFrom = null;
        var mapped = false;
        if (File.Exists(mapFile))
        {
            var (sections, pieces) = ParseMap(File.ReadAllText(mapFile));
            var hs = HunkSections(sections);
            if (hs.Count == hunks.Count && hs.Zip(hunks).All(x => (x.First.Size + 3) / 4 * 4 == x.Second.Size))
            {
                mapped = true;
                var flat = hs[hunkIndex].Address + offset;
                var at = pieces.FirstOrDefault(p => p.Address <= flat && flat < p.Address + p.Size);
                var program = ProgramObject(pieces);
                if (at != null && at.File == program)
                {
                    if (pieces.Count(p => p.File == program && p.Section == at.Section) > 1)
                        return Fail($"the program's object has more than one {at.Section} section in {Path.GetFileName(mapFile)}, so its offsets are ambiguous: rebuild it with this esharp (it compiles m68k code with -fno-reorder-functions)");
                    piece = at;
                    inPiece = flat - at.Address;
                }
                else if (at != null) mapFrom = Path.GetFileName(at.File.Replace('\\', '/'));
            }
            else o.WriteLine($"  ({Path.GetFileName(mapFile)} does not match the exe's hunks: rebuild; mapped without it)");
        }

        if (mapped && piece == null)
        {
            o.WriteLine($"  not in the program's own {(hunk.Kind == HunkKind.Code ? "code" : "data")}; {NearExe()}" +
                        (mapFrom != null ? $" (from {mapFrom}: start-up or library {(hunk.Kind == HunkKind.Code ? "code" : "data")})" : ""));
            return 0;
        }
        if (mapped && piece!.Section == "COMMON")
        {
            o.WriteLine($"  in the program's common data; {NearExe()}");
            return 0;
        }
        if (!mapped && hunk.Kind != HunkKind.Code)
        {
            o.WriteLine($"  in a {hunk.KindName} hunk; {NearExe()} (no {Path.GetFileName(mapFile)} next to the exe: rebuild to see the program's own symbols)");
            return 0;
        }
        if (!mapped && hunkIndex != 0)
        {
            o.WriteLine($"  in code hunk {hunkIndex}; {NearExe()} (no {Path.GetFileName(mapFile)} next to the exe: rebuild to map it)");
            return 0;
        }

        var obj = exe + ".o";
        if (!File.Exists(obj)) return Fail($"{obj} not found: it is written by 'esharp build' for m68k; rebuild");
        var objdump = Tool(target, "objdump", docker, toolchain, out var toolError);
        if (objdump == null) return Fail(toolError!);
        var objName = Path.GetFileName(obj);
        try
        {
            var objSyms = RunTool(docker, dir, objdump, "-t", objName);
            if (objSyms.Code != 0) return Fail($"{objdump} failed: {objSyms.Output}".TrimEnd());
            var objList = ParseSymbols(objSyms.Output);

            if (piece != null && piece.Section != ".text")
            {
                var near = objList.Where(s => s.Section == piece.Section && s.Address <= inPiece).OrderByDescending(s => s.Address).FirstOrDefault();
                var what = piece.Section switch { ".datachip" => "chip data", ".bsschip" => "chip bss", _ => piece.Section.TrimStart('.') };
                o.WriteLine($"  in the program's {what}" +
                            (near != null ? $": {Plain(near.Name)} + 0x{inPiece - near.Address:x}" : $" at 0x{inPiece:x}, before its first symbol"));
                return 0;
            }

            long inObject;
            if (piece != null) inObject = inPiece;
            else
            {
                var exeList = hunk.Symbols.Select(s => s with { Section = ".text" }).ToList();
                var baseAddr = ObjectBase(exeList, objList);
                if (baseAddr == null) return Fail("cannot find _main in both the exe and its object, so the offsets cannot be related (rebuild: the build writes a linker map)");
                inObject = offset - baseAddr.Value;
            }
            // One objdump run for the line table, the DIEs and the range lists (each parser skips the lines that are not its own).
            var decoded = RunTool(docker, dir, objdump, "--dwarf=decodedline,info,Ranges", objName);
            if (decoded.Code != 0) return Fail($"{objdump} failed: {decoded.Output}".TrimEnd());
            var loc = inObject < 0 ? null : Find(inObject, ParseLines(decoded.Output), objList, SourcesOf(exe));
            if (loc == null)
            {
                o.WriteLine($"  not in the program's own code; {NearExe()} (start-up or library code)");
                return 0;
            }
            // The object's DWARF says whether the line is an inlined callee, and into what (the binutils' addr2line cannot read the
            // Amiga object format, so objdump's dump of .debug_info and .debug_ranges is read here).
            string? inlinedInto = null;
            if (FindInlined(inObject, decoded.Output, decoded.Output) is { } inl)
            {
                loc = loc with { Function = inl.Callee ?? loc.Function };
                inlinedInto = inl.Caller;
            }
            return PrintLocation(o, loc, inlinedInto);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ToolMissing(objdump, docker);
        }
    }

    /// <summary>Prints a code location: the .cs line, or what kind of code it is when it has none.</summary>
    private static int PrintLocation(TextWriter o, Location loc, string? inlinedInto)
    {
        var isCs = loc.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        var inlined = inlinedInto != null ? $", inlined into {inlinedInto}" : "";
        if (loc.Function == "main" && inlinedInto == null)
        {
            o.WriteLine("  in the generated main() wrapper (calls the entry point)");
            return 0;
        }
        if (!isCs && loc.Function != null && loc.Function.StartsWith("Es_ESharp_", StringComparison.Ordinal))
        {
            // Platform and runtime code is emitted without #line, so the line table only has generated-C line numbers there.
            o.WriteLine($"  in E# runtime/platform code, C function {loc.Function} (no E# source line is kept for it: look at the user code that called it){inlined}");
            return 0;
        }
        if (!isCs && loc.Function != null && loc.Function.StartsWith("Es_", StringComparison.Ordinal))
        {
            // A user function, but a line of the C file itself: platform or prelude code that the C compiler inlined there.
            o.WriteLine($"  in E# runtime/platform code inlined into C function {loc.Function}{inlined} (generated C {Path.GetFileName(loc.File)}:{loc.Line}): look at the user code that called it");
            return 0;
        }
        if (!isCs)
        {
            o.WriteLine($"  in the generated C prelude ({Path.GetFileName(loc.File)}:{loc.Line}), C function {loc.Function}{inlined}");
            return 0;
        }
        o.WriteLine($"  {loc.File}:{loc.Line}");
        o.WriteLine($"  C function {loc.Function}" + (inlinedInto != null ? $" (inlined into {inlinedInto})" : ""));
        PrintSource(o, loc.File, loc.Line);
        return 0;
    }

    // ---- inlined calls in the DWARF of the m68k object ----

    internal sealed record Inlined(string? Callee, string? Caller);

    private static readonly Regex DieRe = new(@"^\s*<(\d+)><([0-9a-f]+)>: Abbrev Number: \d+ \((DW_TAG_\w+)\)", RegexOptions.Compiled);
    private static readonly Regex AttrRe = new(@"^\s*<[0-9a-f]+>\s+(DW_AT_\w+)\s*:\s*(.*?)\s*$", RegexOptions.Compiled);
    private static readonly Regex RangeRe = new(@"^\s*([0-9a-f]{8})\s+(?:([0-9a-f]{8})\s+([0-9a-f]{8})|<End of list>)", RegexOptions.Compiled);

    private sealed class Die
    {
        public string Tag = "";
        public Die? Parent;
        public Dictionary<string, string> Attrs = new();
        public int Version;   // the DWARF version of its compilation unit (0: not in the dump)
    }

    private static readonly Regex VersionRe = new(@"^\s*Version:\s+(\d+)\s*$", RegexOptions.Compiled);

    private static long? HexOrDec(string? v)
    {
        if (v == null) return null;
        var m = Regex.Match(v, @"^(?:0x([0-9a-f]+)|(\d+))");
        return !m.Success ? null : m.Groups[1].Success ? long.Parse(m.Groups[1].Value, NumberStyles.HexNumber) : long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The innermost inlined call (DW_TAG_inlined_subroutine) that covers an offset in the object's .text, from
    /// <c>objdump --dwarf=info</c> and <c>--dwarf=Ranges</c>: the callee's name and the function it was inlined into. Null if none.
    /// </summary>
    internal static Inlined? FindInlined(long offset, string dwarfInfo, string dwarfRanges)
    {
        var dies = new Dictionary<long, Die>();
        var list = new List<Die>();
        var stack = new List<Die>();
        Die? current = null;
        var version = 0;
        foreach (var raw in dwarfInfo.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var v = VersionRe.Match(line);
            if (v.Success) { version = int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            var d = DieRe.Match(line);
            if (d.Success)
            {
                var depth = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                current = new Die { Tag = d.Groups[3].Value, Version = version };
                while (stack.Count > depth) stack.RemoveAt(stack.Count - 1);
                current.Parent = stack.Count > 0 ? stack[^1] : null;
                stack.Add(current);
                dies[long.Parse(d.Groups[2].Value, NumberStyles.HexNumber)] = current;
                list.Add(current);
                continue;
            }
            var a = AttrRe.Match(line);
            if (a.Success && current != null) current.Attrs[a.Groups[1].Value] = a.Groups[2].Value;
        }

        var rangeLists = new Dictionary<long, List<(long Begin, long End)>>();
        foreach (var raw in dwarfRanges.Split('\n'))
        {
            var m = RangeRe.Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            var key = long.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
            if (!rangeLists.TryGetValue(key, out var l)) rangeLists[key] = l = new List<(long, long)>();
            if (m.Groups[2].Success) l.Add((long.Parse(m.Groups[2].Value, NumberStyles.HexNumber), long.Parse(m.Groups[3].Value, NumberStyles.HexNumber)));
        }

        bool Covers(Die x)
        {
            if (HexOrDec(x.Attrs.GetValueOrDefault("DW_AT_ranges")) is { } r) return rangeLists.TryGetValue(r, out var l) && l.Any(e => e.Begin <= offset && offset < e.End);
            if (HexOrDec(x.Attrs.GetValueOrDefault("DW_AT_low_pc")) is not { } lo || HexOrDec(x.Attrs.GetValueOrDefault("DW_AT_high_pc")) is not { } hi) return false;
            // DWARF 4 stores high_pc as a length from low_pc (newer objdump says "offset from low_pc"); DWARF 2 and 3 as an address.
            var isAddress = x.Version is > 0 and < 4 && !x.Attrs["DW_AT_high_pc"].Contains("offset from low_pc", StringComparison.Ordinal);
            return lo <= offset && offset < (isAddress ? hi : lo + hi);
        }

        string? NameOf(Die x, int depth = 0)
        {
            if (x.Attrs.TryGetValue("DW_AT_name", out var n))
            {
                var colon = n.LastIndexOf("): ", StringComparison.Ordinal);
                return colon >= 0 ? n.Substring(colon + 3) : n;
            }
            foreach (var key in new[] { "DW_AT_abstract_origin", "DW_AT_specification" })
                if (depth < 4 && x.Attrs.TryGetValue(key, out var o) && Regex.Match(o, @"<0x([0-9a-f]+)>") is { Success: true } m
                    && dies.TryGetValue(long.Parse(m.Groups[1].Value, NumberStyles.HexNumber), out var target))
                    return NameOf(target, depth + 1);
            return null;
        }

        // Nested inlined calls come later in the dump than the call that holds them, so the last one that covers the offset is the innermost.
        var inner = list.LastOrDefault(x => x.Tag == "DW_TAG_inlined_subroutine" && Covers(x));
        if (inner == null) return null;
        var outer = inner.Parent;
        while (outer != null && outer.Tag != "DW_TAG_inlined_subroutine" && outer.Tag != "DW_TAG_subprogram") outer = outer.Parent;
        return new Inlined(NameOf(inner), outer == null ? null : NameOf(outer));
    }

    // ---- ELF (AROS, AmigaOS 4, MorphOS) ----

    /// <summary>addr2line -f -i output: function and file:line pairs, innermost first.</summary>
    internal static List<Location> ParseAddr2Line(string text)
    {
        var list = new List<Location>();
        var lines = text.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
        for (var i = 0; i + 1 < lines.Count; i += 2)
        {
            var function = lines[i].Trim();
            var place = lines[i + 1].Trim();
            var cut = place.IndexOf(" (discriminator", StringComparison.Ordinal);
            if (cut >= 0) place = place.Substring(0, cut);
            var colon = place.LastIndexOf(':');
            if (colon < 0) continue;
            int.TryParse(place.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var line);
            list.Add(new Location(place.Substring(0, colon), line, function == "??" ? null : function));
        }
        return list;
    }

    private static int RunElf(TextWriter o, string exe, byte[] bytes, Where? where, TargetInfo target, string? docker, string? toolchain)
    {
        var elf = ReadElf(bytes);
        var loaded = elf.Sections.Where(s => s.Alloc && s.Size > 0).ToList();
        if (where == null)
        {
            o.WriteLine($"{Path.GetFileName(exe)}: ELF {(elf.Relocatable ? "relocatable (loaded section by section)" : "executable")}, {loaded.Count} loaded sections");
            foreach (var s in loaded) o.WriteLine($"  section {s.Index} {s.Name}: {s.Describe()}" + (elf.Relocatable ? "" : $", at 0x{s.Address:x}"));
            return 0;
        }
        ElfSection? section;
        long offset;
        if (where.Symbol != null)
        {
            var sym = elf.Symbols.FirstOrDefault(s => s.Name == where.Symbol);
            if (sym == null) return Fail($"no symbol {where.Symbol} in {Path.GetFileName(exe)}");
            section = elf.Sections[sym.Section];
            offset = sym.Value - (elf.Relocatable ? 0 : section.Address) + where.Offset;
        }
        else if (where.Absolute)
        {
            if (elf.Relocatable) return Fail("this exe is relocatable (AROS): give the segment or section and the offset the crash report prints");
            section = loaded.FirstOrDefault(s => s.Address <= where.Offset && where.Offset < s.Address + s.Size);
            if (section == null) return Fail($"0x{where.Offset:x} is in no section of {Path.GetFileName(exe)}");
            offset = where.Offset - section.Address;
        }
        else
        {
            // A section name is the more reliable half of "segment 1 .text": the segment number counts what the loader made of the
            // sections, which need not match the section table's index, so the name wins when both are given.
            section = where.Name != null
                ? elf.Sections.FirstOrDefault(s => s.Name == where.Name && s.Alloc && s.Size > 0) ?? elf.Sections.FirstOrDefault(s => s.Name == where.Name)
                : where.Number != null && where.Number.Value >= 0 && where.Number.Value < elf.Sections.Count ? elf.Sections[where.Number.Value] : null;
            if (section != null && where.Number != null && where.Name != null && section.Index != where.Number.Value)
                o.WriteLine($"  (segment {where.Number} is not section {section.Index} here: using the section name {where.Name})");
            if (section == null)
                return Fail($"no section {where.Number?.ToString() ?? where.Name}{(where.Number != null && where.Name != null ? " " + where.Name : "")} in {Path.GetFileName(exe)} (loaded sections: {string.Join("; ", loaded.Select(s => $"{s.Index} {s.Name}"))})");
            offset = where.Offset;
        }
        o.WriteLine($"section {section.Index} {section.Name} offset 0x{offset:x}" + (elf.Relocatable ? "" : $" (address 0x{section.Address + offset:x} in the file)"));
        o.WriteLine($"  {section.Name}: {section.Describe()}");
        if (!section.Alloc || offset < 0 || offset >= section.Size)
        {
            o.WriteLine(section.Alloc ? $"  past the end of the section ({section.Size} bytes)" : "  not a loaded section");
            return 0;
        }
        var value = elf.Relocatable ? offset : section.Address + offset;
        var near = elf.Symbols.Where(s => s.Section == section.Index && s.Value <= value).OrderByDescending(s => s.Value).ThenByDescending(s => s.Kind).FirstOrDefault();
        var nearText = near != null ? $"nearest symbol {near.Name} + 0x{value - near.Value:x}" : "no symbol before it";
        if (!section.Code)
        {
            o.WriteLine($"  {nearText}");
            return 0;
        }

        var debug = exe + ".debug";
        if (!File.Exists(debug)) return Fail($"{debug} not found: it is written by 'esharp build' for {target.Name}; rebuild");
        var dbg = ReadElf(File.ReadAllBytes(debug));
        var twin = dbg.Sections.FirstOrDefault(s => s.Name == section.Name);
        if (twin == null || twin.Size != section.Size || (!elf.Relocatable && twin.Address != section.Address))
            return Fail($"{Path.GetFileName(debug)} does not match {Path.GetFileName(exe)} (its {section.Name} differs): rebuild");
        var addr2line = Tool(target, "addr2line", docker, toolchain, out var toolError);
        if (addr2line == null) return Fail(toolError!);
        try
        {
            var r = RunTool(docker, Path.GetDirectoryName(exe)!, addr2line, "-f", "-i", "-e", Path.GetFileName(debug), "-j", section.Name, $"0x{offset:x}");
            if (r.Code != 0) return Fail($"{addr2line} failed: {r.Output}".TrimEnd());
            var sources = SourcesOf(exe);
            var frames = ParseAddr2Line(r.Output).Select(l => l with { File = SourcePath(l.File, sources) }).ToList();
            if (frames.Count == 0 || frames[0].Line == 0)
            {
                o.WriteLine($"  not in the program's own code; {nearText} (start-up or library code)");
                return 0;
            }
            // The innermost frame with a .cs line is the place; the frame around it, if any, is where it was inlined.
            var first = frames.FindIndex(f => f.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            var loc = first >= 0 ? frames[first] : frames[0];
            var outer = first >= 0 && first + 1 < frames.Count ? frames[first + 1] : first < 0 && frames.Count > 1 ? frames[1] : null;
            if (first > 0) o.WriteLine($"  (in {frames[0].Function ?? "?"}: E# runtime/platform code inlined at the line below)");
            return PrintLocation(o, loc, outer == null ? null : outer.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? $"{outer.Function} at {outer.File}:{outer.Line}" : outer.Function);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ToolMissing(addr2line, docker);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"esharp: {message}");
        return 1;
    }
}
