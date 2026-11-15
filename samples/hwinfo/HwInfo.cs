using System;
using ESharp;
using ESharp.Platform;

namespace HwInfo;

/// <summary>
/// Prints what the machine reports about itself, for hardware test reports: Kickstart version,
/// CPU and FPU flags, mains frequency, chip and fast RAM, AGA, library versions. It prints facts only
/// (no "ok" lines) and needs nothing from Workbench. Read from exec's ExecBase at fixed offsets, so
/// it is for the classic 68k build; elsewhere it says so.
/// </summary>
public static class Program
{
    static void Line(string label, int value)
    {
        Dos.PutStr(label);
        Dos.PutInt(value);
        Dos.PutStr("\n");
    }

    static void Text(string label, string value)
    {
        Dos.PutStr(label);
        Dos.PutStr(value);
        Dos.PutStr("\n");
    }

    static string YesNo(bool b) { return b ? "yes" : "no"; }

#if AMIGA && !AROS && !ESHARP_HOSTC
    static unsafe void Library(string name)
    {
        void* lib = Exec.OpenLibrary(name, 0);
        Dos.PutStr(name);
        if (lib == null)
        {
            Dos.PutStr(": not available\n");
            return;
        }
        byte* b = (byte*)lib;
        Dos.PutStr(" ");
        Dos.PutInt((b[20] << 8) | b[21]);
        Dos.PutStr(".");
        Dos.PutInt((b[22] << 8) | b[23]);
        Dos.PutStr("\n");
        Exec.CloseLibrary(lib);
    }
#endif

    [EntryPoint]
    public static unsafe void Main()
    {
        Dos.PutStr("E# hardware info\n");
#if AMIGA && !AROS && !ESHARP_HOSTC
        byte* sys = *(byte**)4;
        Line("exec.library (Kickstart) version: ", (sys[20] << 8) | sys[21]);
        Line("exec.library revision: ", (sys[22] << 8) | sys[23]);
        int attn = (sys[296] << 8) | sys[297];
        Text("68010 or better: ", YesNo((attn & 1) != 0));
        Text("68020 or better: ", YesNo((attn & 2) != 0));
        Text("68030 or better: ", YesNo((attn & 4) != 0));
        Text("68040 or better: ", YesNo((attn & 8) != 0));
        Text("68060: ", YesNo((attn & 128) != 0));
        Text("FPU (68881/68882/040/060): ", YesNo((attn & (16 | 32 | 64)) != 0));
        Line("vertical blank frequency (Hz, 50 PAL / 60 NTSC): ", sys[530]);
        Line("mains frequency (Hz): ", sys[531]);
#else
        Dos.PutStr("CPU and Kickstart details are only read on the classic 68k build\n");
#endif
        Line("free chip RAM (bytes): ", (int)Mem.AvailChip());
        Line("free fast RAM (bytes): ", (int)Mem.AvailFast());
        Text("AGA: ", YesNo(Video.HasAga()));
#if AMIGA && !AROS && !ESHARP_HOSTC
        Library("graphics.library");
        Library("intuition.library");
        Library("dos.library");
        Library("workbench.library");
#endif
        Dos.PutStr("done\n");
    }
}
