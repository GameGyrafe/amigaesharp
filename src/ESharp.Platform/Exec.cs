#if AMIGA
namespace ESharp.Platform;

/// <summary>exec.library memory calls (all Kickstart 2.0 / v36 or older, so fine for the v37 floor).</summary>
public static unsafe class Exec
{
    public const uint MemPublic = 1;
    public const uint MemChip = 2;
    public const uint MemFast = 4;
    public const uint MemClear = 0x10000;

    /// <summary>exec.library AllocVec(), LVO -684. Returns null when memory is exhausted.</summary>
    [AmigaLib("exec", -684)]
    public static extern byte* AllocVec(uint byteSize, uint requirements);

    /// <summary>exec.library FreeVec(), LVO -690.</summary>
    [AmigaLib("exec", -690)]
    public static extern void FreeVec(byte* memoryBlock);

    /// <summary>exec.library GetMsg(), LVO -372: the next message at a port, or null.</summary>
    [AmigaLib("exec", -372)]
    public static extern void* GetMsg(void* port);

    /// <summary>exec.library ReplyMsg(), LVO -378.</summary>
    [AmigaLib("exec", -378)]
    public static extern void ReplyMsg(void* message);

    /// <summary>exec.library AvailMem(), LVO -216.</summary>
    [AmigaLib("exec", -216)]
    public static extern uint AvailMem(uint requirements);
}
#endif
