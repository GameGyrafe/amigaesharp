// Amiga-only (exec interrupt servers): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
namespace ESharp.Platform;

/// <summary>
/// Vertical blank interrupt servers: an <see cref="InterruptAttribute">[Interrupt]</see> method that exec calls once per
/// frame, for example to count frames or step music while the main program draws. <see cref="AddVBlank"/> puts the server
/// on exec's INTB_VERTB chain (AddIntServer) with its node in public memory; <see cref="RemoveVBlank"/> takes it off, and
/// whatever is still on when the program's Main returns is taken off then ([AtExit]). A program that ends another way
/// (exit, a crash) leaves its server on the chain, pointing at freed code, so return from Main. The server runs in
/// interrupt context: keep it short, allocate nothing, call no library that may wait, and mark the fields it shares with
/// the main program <c>volatile</c>. On m68k and AROS; on PowerPC [Interrupt] is not supported yet (ES0099), and on the
/// dotnet host each <see cref="Frame.WaitVBlank"/> runs the servers once, in order, as the fake vertical blank.
/// <code>
/// static volatile int _ticks;
/// [Interrupt] static void Tick(void* data) { _ticks++; }
/// int id = Interrupts.AddVBlank(&amp;Tick, null);   // ... Interrupts.RemoveVBlank(id);
/// </code>
/// </summary>
public static unsafe class Interrupts
{
    /// <summary>How many vertical blank servers a program can have at once.</summary>
    public const int MaxVBlank = 4;

    /// <summary>Servers on the chain now.</summary>
    public static int VBlankCount { get { return _count; } }

    private static int _count;

#if AMIGA
    private const int NameSize = 16;
    private static byte* _block;       // MaxVBlank struct Interrupt nodes, then the name they share; public memory, cleared

    /// <summary>
    /// Adds <paramref name="handler"/> (the address of an [Interrupt] method) as a vertical blank server called with
    /// <paramref name="data"/>. Returns its id for <see cref="RemoveVBlank"/>, or -1 when all <see cref="MaxVBlank"/> are in use,
    /// memory is short, or the target cannot (PowerPC).
    /// </summary>
    public static int AddVBlank(delegate*<void*, void> handler, void* data)
    {
#if PPC
        return -1;
#else
        if (handler == null) return -1;
        if (_block == null)
        {
            _block = Exec.AllocVec((uint)(MaxVBlank * Layout.InterruptSize + NameSize), Exec.MemPublic | Exec.MemClear);
            if (_block == null) return -1;
            byte* name = _block + MaxVBlank * Layout.InterruptSize;
            string text = "E# VBlank";
            for (int k = 0; k < text.Length; k++) name[k] = (byte)text[k];
        }
        for (int i = 0; i < MaxVBlank; i++)
        {
            byte* node = _block + i * Layout.InterruptSize;
            if (*(void**)(node + Layout.IntCode) != null) continue;
            node[Layout.IntType] = (byte)Layout.NtInterrupt;
            node[Layout.IntPri] = 0;
            *(byte**)(node + Layout.IntName) = _block + MaxVBlank * Layout.InterruptSize;
            *(void**)(node + Layout.IntData) = data;
            *(void**)(node + Layout.IntCode) = (void*)handler;
            Exec.AddIntServer((int)Layout.IntbVertb, (void*)node);
            _count++;
            return i;
        }
        return -1;
#endif
    }

    /// <summary>Takes the server <paramref name="id"/> (from <see cref="AddVBlank"/>) off the chain; nothing if it is not on.</summary>
    public static void RemoveVBlank(int id)
    {
        if (_block == null || id < 0 || id >= MaxVBlank) return;
        byte* node = _block + id * Layout.InterruptSize;
        if (*(void**)(node + Layout.IntCode) == null) return;
        Exec.RemIntServer((int)Layout.IntbVertb, (void*)node);
        *(void**)(node + Layout.IntCode) = null;
        _count--;
    }

    [AtExit]
    private static void RemoveAll()
    {
        if (_block == null) return;
        for (int i = 0; i < MaxVBlank; i++) RemoveVBlank(i);
        Exec.FreeVec(_block);
        _block = null;
    }
#else
    // Host: the servers and their data, run by the fake vertical blank of Frame.WaitVBlank.
    private static readonly nint[] _handlers = new nint[MaxVBlank];
    private static readonly nint[] _data = new nint[MaxVBlank];

    /// <summary>Host: adds a server that each <see cref="Frame.WaitVBlank"/> calls once. Returns its id, or -1 when all are in use.</summary>
    public static int AddVBlank(delegate*<void*, void> handler, void* data)
    {
        if (handler == null) return -1;
        for (int i = 0; i < MaxVBlank; i++)
        {
            if (_handlers[i] != 0) continue;
            _handlers[i] = (nint)handler;
            _data[i] = (nint)data;
            _count++;
            return i;
        }
        return -1;
    }

    public static void RemoveVBlank(int id)
    {
        if (id < 0 || id >= MaxVBlank || _handlers[id] == 0) return;
        _handlers[id] = 0;
        _count--;
    }

    /// <summary>Host: one fake vertical blank (Frame.WaitVBlank calls it).</summary>
    internal static void HostVBlank()
    {
        for (int i = 0; i < MaxVBlank; i++)
            if (_handlers[i] != 0) ((delegate*<void*, void>)_handlers[i])((void*)_data[i]);
    }
#endif
}
#endif
