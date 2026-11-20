using System;
using System.Collections.Generic;
using System.Linq;

namespace ESharp.Cli;

/// <summary>What a target run learnt about the program besides its output (FsUae, Os4Vm).</summary>
public sealed class RunInfo
{
    /// <summary>The program's return code (the shell's RC after it), or null when the run script could not record it.</summary>
    public int? ReturnCode { get; set; }
}

/// <summary>
/// Parsed "ok NAME" / "FAIL NAME" / "skip NAME" lines of one run, and the comparison between two runs. A program prints
/// "skip NAME" for a check it could not run on this machine (no working sound hardware, an emulator that is not real
/// time): that is neither a pass nor a failure, and a skipped name never counts as a difference.
/// </summary>
public sealed class TestReport
{
    public List<(string Name, bool Ok)> Checks { get; } = new();
    public List<string> Skips { get; } = new();
    public int Ok => Checks.Count(c => c.Ok);
    public int Failed => Checks.Count(c => !c.Ok);
    public int Skipped => Skips.Count;

    /// <summary>"N ok, N FAIL" plus ", N skipped" when there are skips.</summary>
    public string Summary => $"{Ok} ok, {Failed} FAIL" + (Skipped > 0 ? $", {Skipped} skipped" : "");

    public static TestReport Parse(string output)
    {
        var r = new TestReport();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("ok ", StringComparison.Ordinal)) r.Checks.Add((line[3..], true));
            else if (line.StartsWith("FAIL ", StringComparison.Ordinal)) r.Checks.Add((line[5..], false));
            else if (line.StartsWith("skip ", StringComparison.Ordinal)) r.Skips.Add(line[5..]);
        }
        return r;
    }

    /// <summary>
    /// Differences between host and Amiga (or another target, named by <paramref name="label"/>): a check that is ok on one and FAIL or missing on the other.
    /// A name repeated in a program is matched by occurrence (first with first, and so on). A check that one side
    /// skipped is not a difference when it is missing on that side.
    /// </summary>
    public static List<string> Compare(TestReport host, TestReport amiga, string label = "Amiga")
    {
        static Dictionary<string, List<bool>> Index(TestReport r)
        {
            var d = new Dictionary<string, List<bool>>(StringComparer.Ordinal);
            foreach (var (n, ok) in r.Checks)
            {
                if (!d.TryGetValue(n, out var l)) d[n] = l = new List<bool>();
                l.Add(ok);
            }
            return d;
        }

        var h = Index(host);
        var a = Index(amiga);
        var diffs = new List<string>();
        foreach (var name in h.Keys.Concat(a.Keys.Where(k => !h.ContainsKey(k))))
        {
            h.TryGetValue(name, out var hl);
            a.TryGetValue(name, out var al);
            var count = Math.Max(hl?.Count ?? 0, al?.Count ?? 0);
            for (var i = 0; i < count; i++)
            {
                var hs = Describe(hl, i);
                var asx = Describe(al, i);
                if (hs == "missing" && host.Skips.Contains(name)) continue;
                if (asx == "missing" && amiga.Skips.Contains(name)) continue;
                if (hs != asx) diffs.Add($"{name}: host {hs}, {label} {asx}");
            }
        }
        return diffs;
    }

    /// <summary>The target side of a test: the lines to print, and whether it passes.</summary>
    public sealed record Verdict(bool Ok, IReadOnlyList<string> Lines);

    /// <summary>
    /// Judges one target run against the host run: its own FAIL lines, the differences, its return code (<paramref name="returnCode"/>,
    /// null when the runner could not get it) and, on hosted AROS, the traps in AROS's debug output (<paramref name="traps"/>, null
    /// elsewhere). A non-zero return code fails the test when the host returned 0 (a crash, or a Main that returned non-zero only
    /// there); any trap fails it.
    /// </summary>
    public static Verdict Judge(TestReport host, int hostExit, TestReport target, string label, int? returnCode, int? traps = null)
    {
        var lines = new List<string>();
        var ok = target.Failed == 0;
        lines.Add($"{label}: {target.Summary}, return code {(returnCode is { } rc ? rc.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown")}" +
                  (traps is { } n ? $", {n} trap(s)" : ""));
        if (traps is > 0)
        {
            ok = false;
            lines.Add($"{label}: {traps} trap line(s) in the debug output: the program (or the system) crashed");
        }
        if (returnCode is { } code && code != hostExit)
        {
            if (code != 0 && hostExit == 0)
            {
                ok = false;
                lines.Add($"{label} returned {code}, the host 0");
            }
            else lines.Add($"Return codes differ: host {hostExit}, {label} {code}");
        }
        var diffs = Compare(host, target, label);
        if (diffs.Count == 0) lines.Add($"Host and {label} agree on every check.");
        else
        {
            ok = false;
            lines.Add($"{diffs.Count} difference(s) between host and {label}:");
            lines.AddRange(diffs.Select(d => "  " + d));
        }
        return new Verdict(ok, lines);
    }

    private static string Describe(List<bool>? l, int i) => l == null || i >= l.Count ? "missing" : l[i] ? "ok" : "FAIL";
}
