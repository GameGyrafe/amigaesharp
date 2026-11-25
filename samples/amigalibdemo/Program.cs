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

#if AMIGA
    // dos.library calls for the second process (dostags.h values, from the AROS headers: SYS_Dummy is TAG_USER + 32).
    [AmigaLib("dos", -606), Since(36)] private static extern int SystemTagList(byte* command, void* tags);
    [AmigaLib("dos", -576), Since(36)] private static extern int GetProgramName(byte* buffer, int length);
    [AmigaLib("dos", -30)] private static extern nuint Open(string name, int accessMode);
    [AmigaLib("dos", -36)] private static extern int Close(nuint file);
    private const uint SysInput = 0x80000021u, SysOutput = 0x80000022u, SysAsynch = 0x80000023u;
    private const string ChildDone = "RAM:amigalibdemo.child";
    private const int ChildAdds = 20000;

    /// <summary>
    /// The second process (this program started again with CHILD): its own opener, many Adds while the first process is calling the
    /// library too, then "ok" or "FAIL" into a file in RAM: for the first process to read.
    /// </summary>
    private static int Child()
    {
        var lib = AmigalibLibrary.Open(1);
        bool ok = lib.IsOpen;
        if (ok)
        {
            for (int i = 1; i <= ChildAdds && ok; i++) ok = lib.Add(1) == 100 + i;
            ok = ok && lib.Calls() == ChildAdds && lib.Recent(0) == 100 + ChildAdds;
            lib.Close();
        }
        byte[] result = new byte[2];
        result[0] = ok ? (byte)'o' : (byte)'F';
        result[1] = ok ? (byte)'k' : (byte)'L';
        Files.WriteAll(ChildDone, result);
        return ok ? 0 : 10;
    }

    /// <summary>Starts this program again as the second process (asynchronously, its input and output NIL:); false when it could not.</summary>
    private static bool StartChild()
    {
        byte[] command = new byte[300];
        int length = 0;
        fixed (byte* p = command)
        {
            if (GetProgramName(p, 280) == 0) return false;
            while (p[length] != 0) length++;
            byte[] word = new byte[6];
            word[0] = (byte)' '; word[1] = (byte)'C'; word[2] = (byte)'H'; word[3] = (byte)'I'; word[4] = (byte)'L'; word[5] = (byte)'D';
            for (int i = 0; i < 6; i++) p[length + i] = word[i];
            p[length + 6] = 0;
            nuint input = Open("NIL:", 1005), output = Open("NIL:", 1006);
            if (input == 0 || output == 0)
            {
                if (input != 0) Close(input);
                if (output != 0) Close(output);
                return false;
            }
            uint[] tags = new uint[8];
            tags[0] = SysInput; tags[1] = (uint)input; tags[2] = SysOutput; tags[3] = (uint)output; tags[4] = SysAsynch; tags[5] = 1; tags[6] = 0; tags[7] = 0;
            fixed (uint* t = tags)
            {
                if (SystemTagList(p, t) != 0)
                {
                    Close(input);
                    Close(output);
                    return false;
                }
            }
        }
        return true;   // the system closes NIL: for the child when it ends
    }

    /// <summary>Calls the library from this process while the second one does too, until the second one is done; true when both kept their own count.</summary>
    private static bool TwoProcesses()
    {
        Files.Delete(ChildDone);
        if (!StartChild()) return false;
        var lib = AmigalibLibrary.Open(1);
        if (!lib.IsOpen) return false;
        bool ok = true;
        int done = 0, rounds = 0;
        int size;
        byte* result = null;
        while (ok && rounds < 2000)
        {
            for (int i = 0; i < 100 && ok; i++)
            {
                done++;
                ok = lib.Add(1) == 100 + done;
            }
            rounds++;
            result = Files.ReadAll(ChildDone, out size, false);
            if (result != null) break;
            if (rounds % 20 == 0) Dos.Delay(1);
        }
        ok = ok && lib.Calls() == done && result != null && result[0] == (byte)'o' && result[1] == (byte)'k';
        if (result != null) Mem.Free(result);
        lib.Close();
        Files.Delete(ChildDone);
        Dos.PutStr("the first process made ");
        Dos.PutInt(done);
        Dos.PutStr(" calls while the second one made ");
        Dos.PutInt(ChildAdds);
        Dos.PutStr("\n");
        return ok;
    }
#endif

    [EntryPoint]
    public static int Main()
    {
#if AMIGA
        if (Args.Has("CHILD")) return Child();
#endif
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
        Check("a dos.library call inside the library", a.Pause(2) == 2);

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
        // An expunge asked for while it is open only marks it (LIBF_DELEXP); it goes when the last opener closes.
        var held = AmigalibLibrary.Open(1);
        AmigalibLibrary.FlushUnused();
        Check("a memory flush leaves an open library alone", held.IsOpen && AmigalibLibrary.OpenCount() == 1);
        Check("an expunge asked for while it is open", AmigalibLibrary.ExpungeWhenClosed());
        Check("still there with its opener", AmigalibLibrary.OpenCount() == 1);
        Separate("the opener still has its data", held.Add(1) == 101);
        held.Close();
        Check("expunged at the last close", AmigalibLibrary.OpenCount() == -1);
#if AMIGA
        Check("all memory back after the delayed expunge", Exec.AvailMem(0) == freeBefore);
#else
        Dos.PutStr("skip all memory back after the delayed expunge\n");
#endif
#if AMIGA
        Check("a second process calls the library at the same time, with its own opener", TwoProcesses());
        Check("open count back to 0 after both processes", AmigalibLibrary.OpenCount() == 0);
#else
        Dos.PutStr("skip a second process calls the library at the same time, with its own opener\n");
        Dos.PutStr("skip open count back to 0 after both processes\n");
#endif
        var again = AmigalibLibrary.Open(1);
        Separate("loads again after the expunge, with fresh data", again.IsOpen && again.Value() == 100 && again.Calls() == 0);
        again.Close();
        return _failed == 0 ? 0 : 10;
    }
}
