using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

public class ChipBudgetTests
{
    private const string Members = @"
public struct Sprite { public byte Flag; public int Pos; public short W; }
[ChipRam] static byte[] Gfx = new byte[8192];
[ChipRam] static Sprite[] Sprites = new Sprite[10];
[ChipRam] static short[] Samples = { 1, 2, 3 };
";

    private static string Source(string body) =>
        Program(Members, body).Replace("public static void Main()", "public static unsafe void Main()");

    private static CompileResult Run(string body, long? budget = null, bool m68k = true) =>
        Compile(Source(body), (m68k ? Targets.M68k : Targets.Os4).ToOptions(chipBudget: budget));

    [Fact]
    public void Static_chip_data_is_sized_with_m68k_layout()
    {
        var r = Run("");
        Assert.True(r.Success);
        Assert.Equal(8192, r.Chip.Single(e => e.What.EndsWith("Gfx")).Bytes);
        // byte at 0, int aligned to 2 (m68k caps alignment at 2) -> 2..6, short -> 6..8: 8 bytes each.
        Assert.Equal(80, r.Chip.Single(e => e.What.EndsWith("Sprites")).Bytes);
        Assert.Equal(6, r.Chip.Single(e => e.What.EndsWith("Samples")).Bytes);
    }

    [Fact]
    public void Arenas_pools_and_allocations_with_constant_sizes_are_counted()
    {
        var r = Run(@"
Arena a = new Arena(1000, true);
Arena fast = new Arena(5000, false);
Pool<Sprite> p = new Pool<Sprite>(10, true);
byte* raw = Mem.AllocChip(64);
uint n = Mem.AvailChip();
Arena dyn = new Arena(n, true);
a.Destroy(); fast.Destroy(); p.Destroy(); Mem.Free(raw); dyn.Destroy();");
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Equal(1000, r.Chip.Single(e => e.What == "new Arena" && e.Bytes == 1000).Bytes);
        Assert.DoesNotContain(r.Chip, e => e.Bytes == 5000);                    // fast RAM is not chip
        Assert.Equal(10 * (4 + 8 + 1), r.Chip.Single(e => e.Kind == "pool").Bytes);
        Assert.Equal(64, r.Chip.Single(e => e.Kind == "alloc").Bytes);
        Assert.Equal(-1, r.Chip.Single(e => e.What == "new Arena" && e.Bytes < 0).Bytes);  // run-time size listed
        Assert.Equal(8192 + 80 + 6 + 1000 + 130 + 64, ChipBudgetTotal(r));
    }

    [Fact]
    public void Pool_size_includes_the_padding_of_the_free_list()
    {
        // Pool.cs pads the free list (4 bytes per slot) to a multiple of 8: 3 slots of 8 bytes are 16 + 24 + 3 = 43, not 3 * 13 = 39.
        var r = Run("Pool<Sprite> p = new Pool<Sprite>(3, true); Pool<Sprite> q = new Pool<Sprite>(2, true); p.Destroy(); q.Destroy();");
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Equal(new long[] { 43, 8 + 16 + 2 }, r.Chip.Where(e => e.Kind == "pool").Select(e => e.Bytes).ToArray());
    }

    private static long ChipBudgetTotal(CompileResult r) => r.Chip.Where(e => e.Bytes >= 0).Sum(e => e.Bytes);

    [Fact]
    public void Exceeding_the_budget_fails_the_build()
    {
        var over = Run("", budget: 8000);
        Assert.False(over.Success);
        Assert.Contains("ES0030", ErrorIds(over));
        Assert.True(Run("", budget: 8192 + 80 + 6).Success);
    }

    [Fact]
    public void PowerPC_targets_report_nothing()
    {
        var r = Run("", budget: 1, m68k: false);
        Assert.True(r.Success);
        Assert.Empty(r.Chip);
    }

    [Fact]
    public void Library_generic_over_a_private_type_reports_an_accessibility_error()
    {
        var r = Compile(Program("struct Hidden { public int A; }", "Pool<Hidden> p = new Pool<Hidden>(1, true);")
            .Replace("public static void Main()", "public static unsafe void Main()"));
        Assert.Contains("CS0122", ErrorIds(r));
    }
}
