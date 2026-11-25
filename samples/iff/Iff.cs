using System;
using ESharp;
using ESharp.Platform;

namespace IffSample;

/// <summary>
/// Writes a small IFF file (a FORM of type "ESHP" with a header chunk, an odd-sized text chunk, a data chunk, an empty chunk and
/// nested LIST, PROP, FORM and CAT groups) and reads it back chunk by chunk (<see cref="Iff"/>). Prints "ok NAME" or "FAIL NAME".
/// <see cref="Iff"/> works on every target: iffparse.library when it is on the Amiga (a first line says which), the E# code
/// otherwise. The file is written both ways (<see cref="Iff.ForceBuiltIn"/>), the bytes must be the same, and they must have the
/// reference checksum, so the E# writer is checked against iffparse byte for byte on an Amiga that has the library.
/// </summary>
public static unsafe class Program
{
    // FNV-1a 32 of the file this sample writes (the same on every target and with iffparse.library)
    const uint ReferenceSum = 0x17DCD906u;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    static string[] Hex = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "a", "b", "c", "d", "e", "f" };

    static void PutHex(uint v)
    {
        for (int shift = 28; shift >= 0; shift -= 4) Dos.PutStr(Hex[(int)((v >> shift) & 15u)]);
    }

    static uint Be(byte* p, int o) => ((uint)p[o] << 24) | ((uint)p[o + 1] << 16) | ((uint)p[o + 2] << 8) | p[o + 3];

    static uint Sum(byte* p, int n)
    {
        uint h = 2166136261u;
        for (int i = 0; i < n; i++) h = (h ^ p[i]) * 16777619u;
        return h;
    }

    // The file written for the checks: sizes 8, 5 (odd), 300, 0, 3 (odd), 5 (odd), 1 (odd), with groups inside the FORM.
    static bool Build(string path, byte* data)
    {
        Iff.ForceBuiltIn = false;
        return BuildNow(path, data);
    }

    static bool BuildNow(string path, byte* data)
    {
        byte[] header = new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 };
        byte[] text = new byte[5] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };     // odd size: a pad byte follows
        uint form = Iff.Id("ESHP");
        IffFile w = Iff.Create(path, form);
        if (!w.IsOpen) return false;
        bool ok = true;
        fixed (byte* h = header)
        fixed (byte* t = text)
        {
            ok = ok && w.WriteChunk(Iff.Id("HEAD"), h, 8);
            ok = ok && w.WriteChunk(Iff.Id("NAME"), t, 5);
            ok = ok && w.BeginChunk(Iff.Id("BODY"));             // a chunk written in two pieces
            ok = ok && w.Write(data, 100) && w.Write(data + 100, 200);
            ok = ok && w.EndChunk();
            ok = ok && w.WriteChunk(Iff.Id("NONE"), data, 0);   // an empty chunk
            ok = ok && w.BeginGroup(Iff.IdList, form);
            ok = ok && w.BeginGroup(Iff.IdProp, form) && w.WriteChunk(Iff.Id("DFLT"), data, 3) && w.EndGroup();
            ok = ok && w.BeginGroup(Iff.IdForm, form) && w.WriteChunk(Iff.Id("ITEM"), t, 5) && w.EndGroup();
            ok = ok && w.EndGroup();
            ok = ok && w.BeginGroup(Iff.IdCat, form);
            ok = ok && w.BeginGroup(Iff.IdForm, Iff.Id("SUBF")) && w.WriteChunk(Iff.Id("ITEM"), t, 1) && w.EndGroup();
            ok = ok && w.EndGroup();
            ok = ok && w.WriteChunk(Iff.Id("TAIL"), h, 2);
        }
        return w.Finish() && ok;
    }

    // Steps through the file and checks what the writer put in it; returns how many steps there were (-1 if something differs).
    static int Walk(string path, byte* buf, bool forceBuiltIn)
    {
        Iff.ForceBuiltIn = forceBuiltIn;
        IffFile r = Iff.OpenRead(path);
        Iff.ForceBuiltIn = false;
        if (!r.IsOpen || r.FormType != Iff.Id("ESHP")) return -1;
        uint form = Iff.Id("ESHP");
        uint[] ids = { Iff.Id("HEAD"), Iff.Id("NAME"), Iff.Id("BODY"), Iff.Id("NONE"), Iff.IdList, Iff.IdProp, Iff.Id("DFLT"),
                       Iff.IdForm, Iff.Id("ITEM"), Iff.IdCat, Iff.IdForm, Iff.Id("ITEM"), Iff.Id("TAIL") };
        int[] sizes = { 8, 5, 300, 0, 54, 16, 3, 18, 5, 26, 14, 1, 2 };
        int n = 0;
        bool good = true;
        while (r.Next())
        {
            if (n >= 13) { good = false; break; }
            bool isGroup = r.ChunkId == Iff.IdList || r.ChunkId == Iff.IdProp || r.ChunkId == Iff.IdForm || r.ChunkId == Iff.IdCat;
            int got = isGroup ? 0 : r.Read(buf, 400);          // never Read a group: iffparse would give its raw contents and step past the chunks inside
            good = good && r.ChunkId == ids[n] && r.ChunkSize == sizes[n] && (isGroup || got == sizes[n]);
            if (isGroup) good = good && (r.ChunkType == form || (n == 10 && r.ChunkType == Iff.Id("SUBF")));
            if (n == 0) good = good && buf[0] == 1 && buf[7] == 8;
            if (n == 1) good = good && buf[0] == 'h' && buf[4] == 'o';
            if (n == 2) for (int i = 0; i < 300 && good; i++) good = buf[i] == (byte)(i * 7 + 3);
            n++;
        }
        r.Close();
        return good && !r.IsOpen && n == 13 ? n : -1;
    }

    [EntryPoint]
    public static void Main()
    {
        Dos.PutStr(Iff.Open() ? "using iffparse.library\n" : "using the built-in E# reader and writer\n");
        Iff.Close();
#if AMIGA
        string pathA = "RAM:esharp_iff_a.iff", pathB = "RAM:esharp_iff_b.iff", pathC = "RAM:esharp_iff_c.iff";
#elif SDL
        string pathA = "esharp_iff_a.iff", pathB = "esharp_iff_b.iff", pathC = "esharp_iff_c.iff";
#else
        string pathA = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp_iff_a.iff");
        string pathB = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp_iff_b.iff");
        string pathC = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp_iff_c.iff");
#endif
        byte[] data = new byte[300];
        for (int i = 0; i < 300; i++) data[i] = (byte)(i * 7 + 3);
        byte[] buf = new byte[400];

        fixed (byte* d = data)
        fixed (byte* b = buf)
        {
            // written with whatever Iff uses here (iffparse.library on an Amiga that has it), and with the E# writer
            Check(Build(pathA, d), "the file is written");
            Iff.ForceBuiltIn = true;
            bool wroteB = BuildNow(pathB, d);
            Iff.ForceBuiltIn = false;
            Check(wroteB, "the file is written by the E# writer");
            byte* fa = Files.ReadAll(pathA, out int sa, false);
            byte* fb = Files.ReadAll(pathB, out int sb, false);
            Check(fa != null && fb != null && sa == sb, "both files have the same size");
            bool same = fa != null && fb != null && sa == sb;
            for (int i = 0; i < sa && same; i++) same = fa[i] == fb[i];
            Check(same, "the E# writer's bytes are the other writer's bytes");
            uint sum = fa != null ? Sum(fa, sa) : 0u;
            Dos.PutStr("sum "); PutHex(sum); Dos.PutStr("\n");
            Check(sa == 464 && sum == ReferenceSum, "the file has the reference size and checksum");
            Check(sa == 464 && Be(fa, 0) == Iff.IdForm && Be(fa, 4) == 456u && Be(fa, 8) == Iff.Id("ESHP"), "the FORM header holds the size and type");

            Check(Walk(pathA, b, false) == 13, "the file reads back: sizes, bytes and nested groups");
            Check(Walk(pathA, b, true) == 13, "the E# reader reads the first file");
            Check(Walk(pathB, b, false) == 13, "the second file reads back");

            // damaged files: no crash, an error result
            Iff.ForceBuiltIn = true;
            int cut = 55;                                          // part way into the BODY chunk (HEAD ends at 28, NAME at 42, BODY data from 50)
            Check(Files.WriteAll(pathC, new ReadOnlySpan<byte>(fa, cut)), "a truncated copy is written");
            IffFile t = Iff.OpenRead(pathC);
            int steps = 0;
            while (t.Next()) steps++;
            Check(t.IsOpen && steps == 2 && t.Damaged, "a file cut inside a chunk stops with an error after the chunks before it");
            t.Close();
            Files.WriteAll(pathC, new ReadOnlySpan<byte>(fa, 20));
            IffFile h = Iff.OpenRead(pathC);
            Check(h.IsOpen && !h.Next() && h.Damaged, "a file cut after its header has an error and no chunk");
            h.Close();
            Files.WriteAll(pathC, new ReadOnlySpan<byte>(fa, 7));
            Check(!Iff.OpenRead(pathC).IsOpen, "a file shorter than a FORM header does not open");
            Files.WriteAll(pathC, new ReadOnlySpan<byte>(fa + 12, 40));
            Check(!Iff.OpenRead(pathC).IsOpen, "a file that does not start with FORM does not open");
            Iff.ForceBuiltIn = false;
            if (fa != null) Mem.Free(fa);
            if (fb != null) Mem.Free(fb);
        }
        Check(!Iff.OpenRead("RAM:esharp_no_such_file.iff").IsOpen, "a missing file does not open");

        // misuse of a writer is an error result, not a crash
        Iff.ForceBuiltIn = true;
        IffFile m = Iff.Create(pathC, Iff.Id("ESHP"));
        bool bad = m.IsOpen;
        fixed (byte* d = data)
        {
            bad = bad && !m.Write(d, 4) && !m.EndChunk() && !m.EndGroup();    // no chunk is open
            bad = bad && m.BeginChunk(Iff.Id("ABCD")) && !m.BeginChunk(Iff.Id("EFGH")) && !m.BeginGroup(Iff.IdForm, 0) && !m.Finish();
            bad = bad && m.EndChunk() && !m.BeginGroup(Iff.Id("ABCD"), 0) && m.Finish();
        }
        Iff.ForceBuiltIn = false;
        Check(bad, "misuse of a writer gives false and the file can still be finished");

        Files.Delete(pathA);
        Files.Delete(pathB);
        Files.Delete(pathC);
        Iff.Close();
    }
}
