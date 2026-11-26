using ESharp;
using ESharp.Platform;

namespace FileReqSample;

/// <summary>
/// <see cref="FileRequester"/>: a file requester on every target (asl.library on the Amiga, the system's file dialog on SDL). The
/// program prints SHOT, shows the Open requester (or Save with the switch SAVE) starting in <c>RAM:</c> (<c>/tmp</c> on SDL) with the
/// name <c>filereq-hello.txt</c> and the pattern <c>#?.txt</c>, and checks the result against EXPECT=path (default: none, a cancel).
/// On the Amiga the sample first writes <c>RAM:filereq-hello.txt</c> so there is a file to pick; a run that picks it with
/// <c>--input</c> passes <c>EXPECT=RAM:filereq-hello.txt</c>. On SDL headless there is no dialog and the answer is a cancel, so a run
/// without EXPECT passes. On the host the answer is preset from EXPECT with FileRequester.HostPick.
/// </summary>
public static class Program
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
        string expected = Args.Value("EXPECT");
        bool save = Args.Has("SAVE");
#if AMIGA
        byte[] hello = new byte[3];
        hello[0] = 104; hello[1] = 105; hello[2] = 10;
        Check(Files.WriteAll("RAM:filereq-hello.txt", hello), "a file to pick is written");
        string drawer = "RAM:";
#elif SDL
        string drawer = "/tmp";
#else
        FileRequester.HostPick = expected;
        string drawer = "RAM:";
#endif
        Dos.PutStr("SHOT\n");                                     // esharp run --screenshot and --input act after this
        Dos.FlushOutput();
        string? picked = save
            ? FileRequester.Save("E# save", drawer, "filereq-hello.txt", "#?.txt")
            : FileRequester.Open("E# open", drawer, "filereq-hello.txt", "#?.txt");
        if (picked == null) Dos.PutStr("result: cancelled\n");
        else Dos.PutStr($"result: picked {picked}\n");
        if (expected == null) Check(picked == null, "cancelled, as expected");
        else Check(picked != null && picked == expected, "the expected path was picked");
#if AMIGA
        Files.Delete("RAM:filereq-hello.txt");
#endif
    }
}
