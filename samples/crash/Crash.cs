using ESharp;
using ESharp.Platform;

namespace CrashSample;

public sealed class Box
{
    public int Value;
}

/// <summary>
/// A program to try esharp addr on: it has chip data and chip bss as well as ordinary data and bss (so the m68k exe has
/// five hunks), writes where it is to the serial trace (Trace.Where, in a --trace build), and with the argument CRASH it
/// crashes on purpose: a zero divide (a 68k trap and an x86 fault), then a write through a null reference (a PowerPC fault).
/// Run it with: esharp run samples/crash --trace --serial trace.txt --args CRASH (docs/DEBUGGING.md).
/// </summary>
public static class Program
{
    [ChipRam] static ushort[] Pattern = { 0x0180, 0x0F00, 0x0182, 0x00F0, 0x0184, 0x000F, 0x0186, 0x0FFF };   // chip data
    [ChipRam] static byte[] Buffer = new byte[256];                                                        // chip bss
    static int[] Weights = { 3, 1, 4, 1, 5, 9, 2, 6 };                                                     // data
    static int _calls;                                                                                     // bss
    static Box? _box;

    static int Checksum()
    {
        Trace.Where("checksum");
        int sum = 0;
        for (int i = 0; i < Pattern.Length; i++) sum += Pattern[i] * Weights[i];
        for (int i = 0; i < Buffer.Length; i++) Buffer[i] = (byte)i;
        sum += Buffer[255];
        _calls++;
        return sum;
    }

    static int Divide(int a, int b)
    {
        Trace.Where("divide");
        return a / b;
    }

    static void Poke(int value)
    {
        Trace.Where("poke");
        _box!.Value = value;
    }

    [EntryPoint]
    public static void Main()
    {
        Trace.Where("main");
        int sum = Checksum();
        Dos.PutStr(sum == 34456 ? "ok checksum " : "FAIL checksum ");
        Dos.PutInt(sum);
        Dos.PutStr("\n");
        if (!Args.Has("CRASH")) return;
        Dos.PutStr("crashing now\nSHOT\n");   // SHOT: esharp run --screenshot takes its picture a second after this, of the crash
        Dos.FlushOutput();
        int zero = Args.Has("ONE") ? 1 : 0;   // not a constant, so the C compiler keeps a real divide
        Dos.PutInt(Divide(_calls, zero));
        Poke(Args.Count);
        Dos.PutStr("still running: nothing trapped\n");
    }
}
