using ESharp;
using ESharp.Platform;

namespace InterruptsSample;

/// <summary>
/// [InlineAsm] and [Interrupt]. The assembly methods have a 68020 body (and a PowerPC one); the host, sdl and AROS run their
/// C# bodies, so the same checks pass everywhere. Two vertical blank servers count frames: one in a static field, one through
/// its is_Data pointer; the main program reads the counts after waiting for frames. Prints "ok NAME" or "FAIL NAME".
/// Interrupt servers are not supported on PowerPC yet and do not exist on sdl, so that part is left out there.
/// </summary>
public static unsafe class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    // %[result] is the output register, %[a], %[b] the inputs (named after the parameters).
    [InlineAsm("move.l %[a],%[result]\nadd.l %[b],%[result]", Ppc = "add %[result],%[a],%[b]")]
    static int Add(int a, int b) { return a + b; }

    [InlineAsm("move.l %[x],%[result]\nswap %[result]", Ppc = "rotlwi %[result],%[x],16")]
    static uint SwapHalves(uint x) { return (x << 16) | (x >> 16); }

    [InlineAsm("move.b %[v],%[result]\nnot.b %[result]", Ppc = "nor %[result],%[v],%[v]")]
    static byte Invert(byte v) { return (byte)~v; }

    // Pointers go in address registers.
    [InlineAsm("move.l %[p],%[result]\naddq.l #4,%[result]", Ppc = "addi %[result],%[p],4")]
    static int* NextInt(int* p) { return p + 1; }

    // No result; d1 is used besides the operands, so it is named as clobbered.
    [InlineAsm("move.l %[v],d1\nlsl.l #1,d1\nmove.l d1,(%[p])", Clobbers = "d1", Ppc = "add 0,%[v],%[v]\nstw 0,0(%[p])", PpcClobbers = "r0")]
    static void StoreDouble(int* p, int v) { *p = v * 2; }

#if !SDL && !PPC
    static volatile int _ticks;
    static int[] _box = new int[1];                           // a static array does not move, so a server may keep its address

    [Interrupt]
    static void Tick(void* data) { _ticks++; }

    [Interrupt]
    static void Bump(void* data) { *(int*)data += 1; }
#endif

    [EntryPoint]
    public static void Main()
    {
        Check(Add(40, 2) == 42, "inline asm add");
        Check(Add(-5, 3) == -2, "inline asm add with a negative operand");
        Check(SwapHalves(0x12345678u) == 0x56781234u, "inline asm swap of the halves");
        Check(Invert(0x0F) == 0xF0, "inline asm on a byte");
        int* cells = (int*)Mem.AllocFast(16);
        Check(NextInt(cells) == cells + 1, "inline asm on a pointer");
        StoreDouble(cells + 2, 21);
        Check(cells[2] == 42, "inline asm without a result stores through a pointer");

#if SDL || PPC
        Dos.PutStr("note: no [Interrupt] servers on this target (PowerPC: not supported yet; sdl: none), so that part is left out\n");
#else
        fixed (int* box = _box)
        {
            int id = Interrupts.AddVBlank(&Tick, null);
            Check(id >= 0, "a vertical blank server is added");
            int id2 = Interrupts.AddVBlank(&Bump, box);
            Check(id2 >= 0 && id2 != id, "a second server is added");
            Check(Interrupts.VBlankCount == 2, "two servers are on");

            Frame.WaitVBlank();                                   // line up with a frame edge
            int t0 = _ticks, b0 = *box;
            const int Frames = 25;
            Frame.Wait(Frames);
            int dt = _ticks - t0, db = *box - b0;
            Dos.PutStr($"{Frames} frames: the first server counted {dt}, the second {db}\n");
            Check(dt >= Frames - 5 && dt <= Frames + 5, "the server runs once per frame");
            Check(db >= Frames - 5 && db <= Frames + 5, "the server gets its is_Data");

            Interrupts.RemoveVBlank(id);
            Check(Interrupts.VBlankCount == 1, "one server is left after RemoveVBlank");
            int t1 = _ticks, b1 = *box;
            Frame.Wait(5);
            Check(_ticks == t1, "a removed server no longer runs");
            Check(*box > b1, "the other server still runs");
            // The second server stays on: Interrupts takes it off after Main returns ([AtExit]).
        }
#endif
        Mem.Free((byte*)cells);
    }
}
