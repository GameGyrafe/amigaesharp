#if AMIGA
namespace ESharp.Platform;

/// <summary>exec.library memory calls (all Kickstart 2.0 / v36 or older, so fine for the v39 floor).</summary>
public static unsafe class Exec
{
    public const uint MemPublic = 1;
    public const uint MemChip = 2;
    public const uint MemFast = 4;
    public const uint MemClear = 0x10000;

    /// <summary>exec.library AllocVec(), LVO -684. Returns null when memory is exhausted.</summary>
    [AmigaLib("exec", -684), Since(36)]
    public static extern byte* AllocVec(uint byteSize, uint requirements);

    /// <summary>exec.library FreeVec(), LVO -690.</summary>
    [AmigaLib("exec", -690), Since(36)]
    public static extern void FreeVec(byte* memoryBlock);

    /// <summary>exec.library GetMsg(), LVO -372: the next message at a port, or null.</summary>
    [AmigaLib("exec", -372)]
    public static extern void* GetMsg(void* port);

    /// <summary>exec.library WaitPort(), LVO -384: sleeps until a message is at the port, and returns it without removing it.</summary>
    [AmigaLib("exec", -384)]
    public static extern void* WaitPort(void* port);

    /// <summary>exec.library ReplyMsg(), LVO -378.</summary>
    [AmigaLib("exec", -378)]
    public static extern void ReplyMsg(void* message);

    /// <summary>exec.library AvailMem(), LVO -216.</summary>
    [AmigaLib("exec", -216)]
    public static extern uint AvailMem(uint requirements);

    /// <summary>exec.library OpenLibrary(), LVO -552: the library base, or null if it is missing or older than <paramref name="version"/>.</summary>
    [AmigaLib("exec", -552)]
    public static extern void* OpenLibrary(string libName, uint version);

    /// <summary>exec.library CloseLibrary(), LVO -414.</summary>
    [AmigaLib("exec", -414)]
    public static extern void CloseLibrary(void* library);

    /// <summary>exec.library CreateMsgPort(), LVO -666 (v36).</summary>
    [AmigaLib("exec", -666), Since(36)]
    public static extern void* CreateMsgPort();

    /// <summary>exec.library DeleteMsgPort(), LVO -672.</summary>
    [AmigaLib("exec", -672), Since(36)]
    public static extern void DeleteMsgPort(void* port);

    /// <summary>exec.library OpenDevice(), LVO -444: 0 on success.</summary>
    [AmigaLib("exec", -444)]
    public static extern int OpenDevice(string devName, uint unit, void* ioRequest, uint flags);

    /// <summary>exec.library CloseDevice(), LVO -450.</summary>
    [AmigaLib("exec", -450)]
    public static extern void CloseDevice(void* ioRequest);

    /// <summary>exec.library DoIO(), LVO -456: sends a request and waits for it; the request's io_Error.</summary>
    [AmigaLib("exec", -456)]
    public static extern int DoIO(void* ioRequest);

    /// <summary>exec.library CheckIO(), LVO -468: the request if it has completed, else null.</summary>
    [AmigaLib("exec", -468)]
    public static extern void* CheckIO(void* ioRequest);

    /// <summary>exec.library WaitIO(), LVO -474.</summary>
    [AmigaLib("exec", -474)]
    public static extern int WaitIO(void* ioRequest);

    /// <summary>exec.library AbortIO(), LVO -480.</summary>
    [AmigaLib("exec", -480)]
    public static extern int AbortIO(void* ioRequest);

    /// <summary>amiga.lib BeginIO(): starts a request without clearing its flags (SendIO would clear audio's ADIOF_PERVOL).</summary>
    [AmigaLib("alib", 0)]
    public static extern void BeginIO(void* ioRequest);
}
#endif
