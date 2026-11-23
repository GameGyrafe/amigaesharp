using System.Collections.Concurrent;
using System.Reflection;
using ESharp.Platform;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Platform code as the dotnet host runs it: the process-wide tables of <see cref="Cast"/> (not in the BoopsiHost collection: it
/// touches no state a sample's counters depend on, and the point is that it runs beside other classes).
/// </summary>
public class PlatformHostTests
{
    private static unsafe nuint ObjectToPtr(object o) =>
        (nuint)Pointer.Unbox(typeof(Cast).GetMethod("ObjectToPtr", BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(object)).Invoke(null, new[] { o })!);

    private static unsafe object PtrToObject(nuint p) =>
        typeof(Cast).GetMethod("PtrToObject", BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(object)).Invoke(null, new object[] { Pointer.Box((void*)p, typeof(void*)) })!;

    /// <summary>
    /// The host string and object address tables of Cast are process-wide and samples run in parallel in one process (#141: "its text
    /// reads back" failed once). Without the lock a Dictionary mutated by two threads loses entries, throws, or hands two objects one
    /// address. Strings: StringToPtr/PtrToString round trips. Objects: ObjectToPtr/PtrToObject round trips, the same object always the
    /// same address and two objects never one.
    /// </summary>
    [Fact]
    public unsafe void Cast_host_tables_survive_parallel_use()
    {
        // The tables keep what they are given for the life of the process (a string's native copy is never freed, an object is never
        // forgotten): this test leaves about 48000 strings (roughly 1 MB with their tables) and 16000 small objects behind.
        // A fixed cost, not a leak that grows with the suite, and the same as any sample that converts that many distinct strings.
        var errors = new ConcurrentBag<string>();
        var addresses = new ConcurrentBag<(nuint Address, object Owner)>();
        System.Threading.Tasks.Parallel.For(0, 16, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
        {
            try
            {
                var own = new object[1000];
                for (int i = 0; i < 3000; i++)
                {
                    var s = $"cast-{t}-{i}";
                    var back = Cast.PtrToString(Cast.StringToPtr(s));
                    if (back != s) errors.Add($"{s} came back as {back}");
                    if (i < own.Length)
                    {
                        var o = own[i] = new object();
                        var p = ObjectToPtr(o);
                        addresses.Add((p, o));
                        if (!ReferenceEquals(PtrToObject(p), o)) errors.Add($"object {t}-{i} came back as another object");
                        if (ObjectToPtr(o) != p) errors.Add($"object {t}-{i} got a second address");
                    }
                }
            }
            catch (Exception e) { errors.Add(e.GetType().Name + ": " + e.Message); }
        });
        Assert.Empty(errors.Take(5));
        var all = addresses.ToList();
        Assert.Equal(16 * 1000, all.Count);
        Assert.Equal(all.Count, all.Select(a => a.Address).Distinct().Count());   // two objects never share an address
        Assert.DoesNotContain(all, a => a.Address == 0);
    }
}
