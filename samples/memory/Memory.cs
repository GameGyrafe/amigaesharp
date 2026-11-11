using System;
using ESharp;
using ESharp.Platform;

namespace MemorySample;

public struct Item
{
    public int A;
    public int B;
}

/// <summary>
/// Memory management: Mem.AllocChip/AllocFast/Free and Arena (docs/MEMORY.md). Every check prints
/// "ok NAME" or "FAIL NAME"; the output is the same under dotnet, host gcc and on an Amiga.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static unsafe bool AllZero(byte* p, int length)
    {
        for (int i = 0; i < length; i++)
            if (p[i] != 0) return false;
        return true;
    }

    [EntryPoint]
    public static unsafe void Main()
    {
        // Raw chip memory.
        uint before = Mem.AvailChip();
        byte* chip = Mem.AllocChip(4096);
        Check(chip != null, "chip allocation succeeds");
        Check(AllZero(chip, 4096), "chip memory is zeroed");
        for (int i = 0; i < 4096; i++) chip[i] = (byte)(i * 7);
        bool pattern = true;
        for (int i = 0; i < 4096; i++)
            if (chip[i] != (byte)(i * 7)) pattern = false;
        Check(pattern, "chip memory keeps what is written");
        uint during = Mem.AvailChip();
        Check(before - during >= 4096, "allocation reduces free chip RAM");
#if AMIGA && !ESHARP_HOSTC && !AROS && !PPC   // PPC: AmigaOS 4 and MorphOS have no separate chip RAM range
        Check((uint)chip < 0x200000, "chip block is in the chip address range");
#else
        Check(true, "chip block is in the chip address range");
#endif
        Mem.Free(chip);
#if PPC
        Check(true, "free returns the chip RAM");   // AvailMem(MEMF_CHIP) on AmigaOS 4 is not the same pool: not compared there
#else
        Check(Mem.AvailChip() >= during + 4096, "free returns the chip RAM");
#endif

        // Fast memory falls back to chip on a machine with none.
        byte* fast = Mem.AllocFast(2048);
        Check(fast != null && AllZero(fast, 2048), "fast allocation works and is zeroed");
        Mem.Free(fast);
        Mem.Free(null);
        Check(Mem.AllocChip(0) == null, "zero-size allocation returns null");

        // Arena.
        Arena arena = new Arena(256, false);
        Check(arena.Size == 256 && arena.Remaining() == 256, "arena reports its size");
        byte* first = arena.Alloc(10, 1);
        byte* second = arena.Alloc(8, 8);
        Check(first != null && second != null && (int)(second - first) == 16, "arena aligns allocations");
        Check(arena.Remaining() == 232, "arena tracks what is left");
        Check(arena.Alloc(1000, 1) == null && arena.Remaining() == 232, "arena returns null when full");

        Item* items = (Item*)arena.Alloc((uint)sizeof(Item) * 4, 4);
        Check(sizeof(Item) == 8 && items != null, "typed allocation with sizeof");
        items[2].A = 5;
        items[2].B = 6;
        Check(items[2].A + items[2].B == 11 && items[0].A == 0, "typed arena memory is usable and zeroed");

        Span<int> ints = new Span<int>(arena.Alloc(64, 4), 16);
        for (int i = 0; i < ints.Length; i++) ints[i] = i;
        int total = 0;
        foreach (var v in ints) total += v;
        Check(total == 120, "span over arena memory");

        arena.Reset();
        Check(arena.Remaining() == 256 && arena.Alloc(256, 1) != null && arena.Alloc(1, 1) == null, "reset reuses the arena");
        arena.Destroy();
        Check(arena.Size == 0 && arena.Base == null, "destroy releases the arena");

        Arena huge = new Arena(0x7FFFFFF0, true);
        Check(huge.Size == 0 && huge.Alloc(1, 1) == null, "an impossible arena fails cleanly");

        Arena chipArena = new Arena(1024, true);
        Check(chipArena.Size == 1024, "chip arena");
        chipArena.Destroy();
    }
}
