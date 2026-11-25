using ESharp;
using ESharp.Platform;
using AmigaLibSample;

namespace AmigaLibDemo;

/// <summary>
/// Opens amigalib.library (samples/amigalib, an E# .library) twice and shows that the two openers keep their own data: counters,
/// objects in the library's memory and statistics. Calls every function of the library, closes both openers and checks the open count.
/// On the host the library's code is compiled into this program and the binding keeps the two openers' statics apart.
/// </summary>
public static unsafe class Program
{
    private static int _failed;

    private static void Check(string name, bool ok)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
        if (!ok) _failed++;
    }

    /// <summary>
    /// A check that needs each opener's own data. On sdl the library's code is part of the program and its openers share one copy of
    /// the data (not done yet), so there these checks are skipped.
    /// </summary>
    private static void Separate(string name, bool ok)
    {
#if SDL
        Dos.PutStr("skip ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
#else
        Check(name, ok);
#endif
    }

    [EntryPoint]
    public static int Main()
    {
#if SDL
        Dos.PutStr("sdl: the library's code is part of this program and its openers share one copy of its data, so the checks that need their own are skipped\n");
#endif
        // The first line of output also makes dos.library allocate the output's buffer, before the free memory is measured.
        Dos.PutStr("amigalibdemo: two openers of amigalib.library\n");
#if AMIGA
        // One open and close first: the first load of a disk library may leave buffers behind (dos, ramlib) that are not the library's.
        var warm = AmigalibLibrary.Open(1);
        warm.Close();
        AmigalibLibrary.FlushUnused();
        nuint freeBefore = Exec.AvailMem(0);
#endif
        int before = AmigalibLibrary.OpenCount();
        var a = AmigalibLibrary.Open(1);
        Check("the library opens", a.IsOpen);
        if (!a.IsOpen)
        {
            Dos.PutStr("amigalib.library is not in LIBS: (esharp test copies it there)\n");
            return 20;
        }
        var b = AmigalibLibrary.Open(1);
        Check("a second opener", b.IsOpen);
        Check("each opener gets its own base", a.Base != b.Base);
        Check("open count two more", AmigalibLibrary.OpenCount() == (before < 0 ? 0 : before) + 2);
        var tooNew = AmigalibLibrary.Open(AmigalibLibrary.LibraryVersion + 1);
        Check("a newer version is refused", !tooNew.IsOpen);

        // Counter: one per opener, starting from the library's initial value.
        Check("start value", a.Value() == 100 && b.Value() == 100);
        Check("add on the first", a.Add(5) == 105 && a.Add(7) == 112);
        Separate("the second is untouched", b.Value() == 100);
        Separate("add on the second", b.Add(-30) == 70);
        Separate("the first is untouched", a.Value() == 112);
        Separate("calls per opener", a.Calls() == 2 && b.Calls() == 1);
        Separate("history objects per opener", a.Recent(0) == 112 && a.Recent(1) == 105 && b.Recent(0) == 70 && b.Recent(1) == -1);
        b.Reset(1000);
        Separate("reset one opener", b.Value() == 1000 && b.Calls() == 0 && b.Recent(0) == -1 && a.Value() == 112 && a.Recent(0) == 112);
        Check("bool results", a.IsEven(4) && !a.IsEven(7));
        Check("nine arguments (the ninth in a0)", a.Sum9(1, 2, 3, 4, 5, 6, 7, 8, 9) == 285);

        // Checksum: the caller's memory, a string, a struct filled through a pointer, a string result.
        byte[] data = new byte[9];
        for (int i = 0; i < 9; i++) data[i] = (byte)('a' + i);
        uint sum;
        fixed (byte* p = data) sum = a.Adler32(p, 9);
        Check("Adler-32 of abcdefghi", sum == 0x118E038Eu);
        fixed (byte* p = data) b.Adler32(p, 4);
        fixed (byte* p = data) b.Adler32(p, 2);
        Check("string argument", a.Length("Amiga") == 5 && a.Length(null) == -1);
        Stats sa;
        Stats sb;
        a.GetStats(&sa);
        b.GetStats(&sb);
        Separate("statistics per opener", sa.Calls == 1 && sa.Bytes == 9 && sa.Last == 0x118E038Eu && sb.Calls == 2 && sb.Bytes == 6);
        string name = a.Name();
        Check("string result", name != null && name == "amigalib");

        // A third opener starts from the initial data again, whatever the others did.
        var c = AmigalibLibrary.Open(1);
        Separate("a third opener starts fresh", c.IsOpen && c.Value() == 100 && c.Calls() == 0);
        c.Close();

        a.Close();
        Check("close drops the open count", AmigalibLibrary.OpenCount() == (before < 0 ? 0 : before) + 1);
        Separate("the other opener still works", b.Add(1) == 1001);
        b.Close();
        Check("open count back to where it was", AmigalibLibrary.OpenCount() == (before < 0 ? 0 : before));
        Check("closed openers have no base", !a.IsOpen && !b.IsOpen);

        // Nobody has it open: a memory flush expunges it (its Expunge frees the base, and ramlib unloads the code).
        AmigalibLibrary.FlushUnused();
        Check("expunged when nobody has it open", AmigalibLibrary.OpenCount() == -1);
#if AMIGA
        nuint freeAfter = Exec.AvailMem(0);
        Check("all memory back after the expunge", freeAfter == freeBefore);
        if (freeAfter != freeBefore)
        {
            Dos.PutStr("free memory before and after: ");
            Dos.PutInt((int)freeBefore);
            Dos.PutStr(" ");
            Dos.PutInt((int)freeAfter);
            Dos.PutStr("\n");
        }
#else
        Dos.PutStr("skip all memory back after the expunge\n");
#endif
        var again = AmigalibLibrary.Open(1);
        Check("loads again after the expunge, with fresh data", again.IsOpen && again.Value() == 100 && again.Calls() == 0);
        again.Close();
        return _failed == 0 ? 0 : 10;
    }
}
