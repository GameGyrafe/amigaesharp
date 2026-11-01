#if HOST
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#endif

namespace ESharp.Platform;

/// <summary>
/// Raw memory with an explicit kind. Chip RAM is what the custom chips can reach (bitmaps, samples,
/// copper lists); fast RAM is everything else. Blocks are zeroed. Failure is a null return.
/// On the Amiga this is exec AllocVec/FreeVec; on the host it is a fake with a small chip budget,
/// so tests catch overruns and leaks. See docs/MEMORY.md.
/// </summary>
public static unsafe class Mem
{
#if AMIGA
    /// <summary>Zeroed chip RAM (a plain allocation on PowerPC, which has no chip RAM). Null if exhausted.</summary>
    public static byte* AllocChip(uint size) =>
        Exec.AllocVec(size, Exec.MemPublic | Exec.MemChip | Exec.MemClear);

    /// <summary>
    /// Zeroed RAM, preferring fast and falling back to chip, so it works on an unexpanded A500.
    /// Null if exhausted.
    /// </summary>
    public static byte* AllocFast(uint size) =>
        Exec.AllocVec(size, Exec.MemPublic | Exec.MemClear);

    /// <summary>Frees a block from either allocator. Null is ignored.</summary>
    public static void Free(byte* block)
    {
        if (block != null) Exec.FreeVec(block);
    }

    /// <summary>Free chip RAM in bytes.</summary>
    public static uint AvailChip() => Exec.AvailMem(Exec.MemChip);

    /// <summary>Free fast RAM in bytes (0 on a machine without any).</summary>
    public static uint AvailFast() => Exec.AvailMem(Exec.MemFast);
#else
    private struct Block
    {
        public uint Size;
        public bool Chip;
    }

    private static readonly Dictionary<ulong, Block> Live = new();
    private static uint _chipUsed;
    private static uint _fastUsed;

    /// <summary>Chip RAM the fake machine has. Default 512 KB, a stock A500.</summary>
    public static uint ChipBudget = 512 * 1024;

    /// <summary>Fast RAM the fake machine has. Default 0, a stock A500: AllocFast then uses chip.</summary>
    public static uint FastBudget = 0;

    public static uint ChipUsed => _chipUsed;
    public static uint FastUsed => _fastUsed;
    public static int LiveBlocks => Live.Count;

    /// <summary>Frees every live block and sets the budgets. Used by tests.</summary>
    public static void HostReset(uint chipBudget = 512 * 1024, uint fastBudget = 0)
    {
        foreach (var address in Live.Keys) NativeMemory.AlignedFree((void*)address);
        Live.Clear();
        _chipUsed = 0;
        _fastUsed = 0;
        ChipBudget = chipBudget;
        FastBudget = fastBudget;
    }

    private static byte* Take(uint size, bool chip)
    {
        var used = chip ? _chipUsed : _fastUsed;
        var budget = chip ? ChipBudget : FastBudget;
        if (size == 0 || size > budget - Math.Min(used, budget)) return null;
        var p = (byte*)NativeMemory.AlignedAlloc(size, 8);
        NativeMemory.Clear(p, size);
        Live[(ulong)p] = new Block { Size = size, Chip = chip };
        if (chip) _chipUsed += size; else _fastUsed += size;
        return p;
    }

    public static byte* AllocChip(uint size) => Take(size, chip: true);

    public static byte* AllocFast(uint size) => Take(size, chip: false) is var p && p != null ? p : Take(size, chip: true);

    public static void Free(byte* block)
    {
        if (block == null) return;
        if (!Live.Remove((ulong)block, out var info))
            throw new InvalidOperationException("Mem.Free: block was not allocated by Mem, or was already freed");
        if (info.Chip) _chipUsed -= info.Size; else _fastUsed -= info.Size;
        NativeMemory.AlignedFree(block);
    }

    public static uint AvailChip() => ChipBudget - Math.Min(_chipUsed, ChipBudget);

    public static uint AvailFast() => FastBudget - Math.Min(_fastUsed, FastBudget);
#endif
}
