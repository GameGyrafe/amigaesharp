using ESharp;
using ESharp.Platform;

namespace ChipCheckSample;

public struct Sprite
{
    public int Pos;
    public int W;
}

/// <summary>
/// Checks the chip RAM budget report (esharp build --chip-budget, docs/STATUS.md) against exec AvailMem(MEMF_CHIP):
/// every dynamic kind of reservation the report counts is made once with a constant size, and the drop in free
/// chip RAM is compared with the size the report gives for that call site.
/// Rule (Amiga m68k): AllocVec asks exec for size + 4 (the size longword in front of the block), and exec rounds a
/// request up to a multiple of 8 (its MemChunk granularity), so the drop is exactly (size + 4 + 7) &amp; ~7 for
/// the size the report states. Everywhere else (host fake, SDL, AROS, PowerPC) only the lower bound is checked: the
/// drop must be at least the size, because each allocator adds its own header and rounding (hosted AROS x86-64
/// drops 1024, 64, 4224 and 32 for 1000, 43, 4096 and 13).
/// Static [ChipRam] data is loaded with the program, before Main: it is compared with the hunk sizes in
/// obj/m68k/ChipCheck.map instead (docs/STATUS.md). The arrays are used below so they are kept.
/// </summary>
public static class Program
{
    // The numbers below are what `esharp build samples/chipcheck --chip-budget 1m` reports for these lines.
    const uint ArenaBytes = 1000;
    const uint PoolCapacity = 3;       // Sprite is 8 bytes: list 12 -> 16, items 24, flags 3 = 43 (the report's 3 * (4 + 8 + 1) = 39 left out the padding)
    const uint PoolBytes = 43;
    const uint AllocBytes = 4096;
    const uint OddBytes = 13;

    [ChipRam] static byte[] Gfx = new byte[1000];
    [ChipRam] static ushort[] Copper = { 0x0180, 0x0F00, 0x0182, 0x00F0, 0x0184, 0x000F };

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static void Num(string label, uint value)
    {
        Dos.PutStr("note: ");
        Dos.PutStr(label);
        Dos.PutStr(" ");
        Dos.PutInt((int)value);
        Dos.PutStr("\n");
    }

    static uint Expected(uint size)
    {
#if AMIGA && M68K && !ESHARP_HOSTC
        return (size + 4 + 7) & ~7u;
#else
        return size;
#endif
    }

    static bool DropOk(uint drop, uint size)
    {
#if AMIGA && M68K && !ESHARP_HOSTC
        return drop == Expected(size);
#else
        return drop >= size;
#endif
    }

    [EntryPoint]
    public static unsafe void Main()
    {
        Gfx[0] = 1;
        Copper[0] = 0x0180;

        // No output inside the measured window: on an Amiga the console handler allocates memory while text is written
        // (208 bytes were lost over six lines in the first version of this sample, which looked like a leak).
        uint start = Mem.AvailChip();
        uint before = start;
        Arena arena = new Arena(ArenaBytes, true);
        uint arenaDrop = before - Mem.AvailChip();

        before = Mem.AvailChip();
        Pool<Sprite> pool = new Pool<Sprite>(PoolCapacity, true);
        uint poolDrop = before - Mem.AvailChip();

        before = Mem.AvailChip();
        byte* block = Mem.AllocChip(AllocBytes);
        uint allocDrop = before - Mem.AvailChip();

        before = Mem.AvailChip();
        byte* odd = Mem.AllocChip(OddBytes);
        uint oddDrop = before - Mem.AvailChip();

        bool arenaOk = arena.Size == ArenaBytes, poolOk = pool.Capacity() == PoolCapacity && sizeof(Sprite) == 8;
        bool allocOk = block != null, oddOk = odd != null;

        // Everything given back: free chip RAM returns to where it was.
        Mem.Free(odd);
        Mem.Free(block);
        pool.Destroy();
        arena.Destroy();
        uint end = Mem.AvailChip();

        Check(start > 0, "free chip RAM is readable");
        Num("arena drop", arenaDrop);
        Check(arenaOk && DropOk(arenaDrop, ArenaBytes), "new Arena(1000, true) drop matches the report");
        Num("pool drop", poolDrop);
        Check(poolOk && DropOk(poolDrop, PoolBytes), "new Pool<Sprite>(3, true) drop matches the report");
        Num("alloc drop", allocDrop);
        Check(allocOk && DropOk(allocDrop, AllocBytes), "Mem.AllocChip(4096) drop matches the report");
        Num("odd alloc drop", oddDrop);
        Check(oddOk && DropOk(oddDrop, OddBytes), "Mem.AllocChip(13) drop is the size rounded up by the allocator");
        Num("free chip at the start of Main", start);
        Check(end == start, "freeing all four returns the chip RAM to the start value");
    }
}
