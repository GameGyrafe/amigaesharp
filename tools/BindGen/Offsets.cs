using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Measures struct offsets and sizes with the target's own C compiler and headers: writes a C file of
/// <c>offsetof</c>/<c>sizeof</c> constants, compiles it to assembly (no linking, nothing is run) and reads the
/// constants back. The headers stay in the toolchain; only the numbers come out.
/// </summary>
internal static class Offsets
{
    private static readonly Dictionary<string, (string Cc, string Flags)> Known = new()
    {
        ["m68k"] = ("m68k-amigaos-gcc", "-noixemul -m68020"),
        ["aros"] = ("x86_64-aros-gcc", "--sysroot=/opt/x86_64-aros"),
        ["aros-i386"] = ("i386-aros-gcc", "--sysroot=/opt/i386-aros/sdk"),
        ["os4"] = ("ppc-amigaos-gcc", "-D__USE_INLINE__"),
        ["morphos"] = ("ppc-morphos-gcc", "-noixemul"),
    };

    public static int Run(string[] args)
    {
        string spec = args[0], target = args[1];
        string? docker = null, cc = null, flags = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--docker" && i + 1 < args.Length) docker = args[++i];
            else if (args[i] == "--cc" && i + 1 < args.Length) cc = args[++i];
            else if (args[i] == "--cflags" && i + 1 < args.Length) flags = args[++i];
        }
        if (Known.TryGetValue(target, out var k)) { cc ??= k.Cc; flags ??= k.Flags; }
        if (cc == null) { Console.Error.WriteLine($"unknown target '{target}': pass --cc"); return 2; }

        var includes = new List<string>();
        var items = new List<(string Name, string Expr)>();
        var values = new HashSet<string>();   // "value MACRO" lines: a constant, printed as an unsigned 32-bit number
        foreach (var raw in File.ReadAllLines(spec))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("include ")) { includes.Add(line.Substring(8).Trim()); continue; }
            var parts = line.Split('=', 2);
            string name = parts[0].Trim(), rhs = parts[1].Trim();
            string expr;
            if (rhs.StartsWith("sizeof ")) expr = $"sizeof(struct {rhs.Substring(7).Trim()})";
            else if (rhs.StartsWith("value ")) { expr = rhs.Substring(6).Trim(); values.Add(name); }
            else
            {
                int dot = rhs.IndexOf('.');
                expr = $"offsetof(struct {rhs.Substring(0, dot)}, {rhs.Substring(dot + 1)})";
            }
            items.Add((name, expr));
        }

        var c = new StringBuilder();
        foreach (var inc in includes) c.AppendLine($"#include <{inc}>");
        c.AppendLine("#include <stddef.h>");
        // Stored XOR 0x5A5A5A5A so no constant is zero (a zero can be emitted as .zero instead of a number; value + 1 would hit that for -1).
        foreach (var (name, expr) in items) c.AppendLine($"const unsigned int esharp_off_{name} = (unsigned int)({expr}) ^ 0x5A5A5A5Au;");

        string dir = Path.Combine(Path.GetTempPath(), "bindgen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "off.c"), c.ToString());
            var psi = docker == null
                ? new ProcessStartInfo(cc) { WorkingDirectory = dir }
                : new ProcessStartInfo("docker");
            if (docker != null) foreach (var a in new[] { "run", "--rm", "-v", $"{dir}:/work", "-w", "/work", docker, cc }) psi.ArgumentList.Add(a);
            foreach (var f in flags!.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(f);
            foreach (var a in new[] { "-O0", "-S", "-o", "off.s", "off.c" }) psi.ArgumentList.Add(a);
            psi.RedirectStandardError = true;
            using (var p = Process.Start(psi)!)
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) { Console.Error.Write(err); return 1; }
            }
            var asm = File.ReadAllText(Path.Combine(dir, "off.s"));
            int missing = 0;
            foreach (var (name, _) in items)
            {
                var m = Regex.Match(asm, @"_?esharp_off_" + Regex.Escape(name) + @":\s*\n\s*\.(?:long|int|4byte)\s+(-?\d+)");
                if (!m.Success) { Console.WriteLine($"{name} = ?"); missing++; continue; }
                long v = ((long.Parse(m.Groups[1].Value) % 0x100000000L + 0x100000000L) % 0x100000000L) ^ 0x5A5A5A5AL;
                Console.WriteLine(values.Contains(name) ? $"{name} = {v}u" : $"{name} = {v}");
            }
            return missing == 0 ? 0 : 1;
        }
        finally { Directory.Delete(dir, true); }
    }
}
