using System;
using System.Collections.Generic;
using System.Linq;

namespace ESharp.Cli;

/// <summary>Parsed "ok NAME" / "FAIL NAME" lines of one run, and the comparison between two runs.</summary>
public sealed class TestReport
{
    public List<(string Name, bool Ok)> Checks { get; } = new();
    public int Ok => Checks.Count(c => c.Ok);
    public int Failed => Checks.Count(c => !c.Ok);

    public static TestReport Parse(string output)
    {
        var r = new TestReport();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("ok ", StringComparison.Ordinal)) r.Checks.Add((line[3..], true));
            else if (line.StartsWith("FAIL ", StringComparison.Ordinal)) r.Checks.Add((line[5..], false));
        }
        return r;
    }

    /// <summary>
    /// Differences between host and Amiga (or another target, named by <paramref name="label"/>): a check that is ok on one and FAIL or missing on the other.
    /// A name repeated in a program is matched by occurrence (first with first, and so on).
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
                if (hs != asx) diffs.Add($"{name}: host {hs}, {label} {asx}");
            }
        }
        return diffs;
    }

    private static string Describe(List<bool>? l, int i) => l == null || i >= l.Count ? "missing" : l[i] ? "ok" : "FAIL";
}
