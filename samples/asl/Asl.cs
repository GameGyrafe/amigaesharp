using ESharp;
using ESharp.Platform;

namespace AslSample;

/// <summary>
/// The system file requester (<see cref="Asl"/>, asl.library). Always: opens the library, allocates and frees a requester.
/// Only if the file <c>RAM:asl-open</c> exists (a person or a script makes it, so an unattended run does not wait for a click):
/// opens a screen, prints SHOT, shows the file requester and records whether OK or Cancel was chosen. With
/// <c>esharp run --input</c> the script clicks a button after SHOT. asl.library is on the Workbench disk, not in ROM:
/// without it the sample says so and stops.
/// </summary>
public static unsafe class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static void Main()
    {
        if (!Asl.Open()) { Dos.PutStr("asl.library is not installed: nothing to do\n"); return; }
        Check(true, "asl.library opens");
        Check(Asl.CanAllocate(), "a file requester is allocated and freed");
        Check(Asl.CanAllocate(), "and again");

#if AMIGA
        int flagSize;
        byte* flag = Files.ReadAll("RAM:asl-open", out flagSize, false);
        if (flag == null) { Dos.PutStr("RAM:asl-open not found: not opening the requester\n"); Asl.Close(); return; }
        Mem.Free(flag);
#else
        Asl.HostPick = "RAM:picked.txt";
#endif
        if (!Video.Open(640, 256, 2)) { Check(false, "a screen opens"); return; }
        Video.SetColor(0, 4, 4, 8);
        Dos.PutStr("SHOT\n");                                     // esharp run --input sends its clicks after this
        Dos.FlushOutput();
        string? picked = Asl.PickFile("E# file requester", "RAM:");
        Video.Close();
        if (picked == null) Dos.PutStr("result: cancelled\n");
        else Dos.PutStr($"result: picked {picked}\n");
        Check(true, "the requester returned");
        Asl.Close();
    }
}
