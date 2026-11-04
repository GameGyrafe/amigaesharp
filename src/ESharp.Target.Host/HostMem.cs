using System;
using ESharp.Platform;

namespace ESharp.Target.Host;

/// <summary>
/// Gives a test a fresh fake machine: set the chip and fast budgets, then check nothing leaked.
/// Disposing frees any block the test left behind and restores the default budgets.
/// </summary>
public sealed class HostMem : IDisposable
{
    private HostMem(uint chip, uint fast) => Mem.HostReset(chip, fast);

    public static HostMem Capture(uint chipBudget = 2048 * 1024, uint fastBudget = 0) => new(chipBudget, fastBudget);

    public int LiveBlocks => Mem.LiveBlocks;
    public uint ChipUsed => Mem.ChipUsed;
    public uint FastUsed => Mem.FastUsed;

    public void Dispose() => Mem.HostReset();
}
