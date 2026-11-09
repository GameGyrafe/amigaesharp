using ESharp;
using ESharp.Platform;

namespace IffSample;

/// <summary>
/// Writes a small IFF file (a FORM of type "ESHP" with a header chunk, an odd-sized text chunk and a data chunk) and reads it
/// back chunk by chunk (<see cref="Iff"/>, iffparse.library). Prints "ok NAME" or "FAIL NAME". iffparse.library is on the
/// Workbench disk, not in ROM: without it the sample says so and stops (that is the expected result, not a failure).
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
        if (!Iff.Open()) { Dos.PutStr("iffparse.library is not installed: nothing to do\n"); return; }
        Check(true, "iffparse.library opens");
#if AMIGA
        string path = "RAM:esharp_test.iff";
#else
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp_test.iff");
#endif
        uint form = Iff.Id("ESHP"), head = Iff.Id("HEAD"), name = Iff.Id("NAME"), body = Iff.Id("BODY");
        byte[] header = new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 };
        byte[] text = new byte[5] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };     // odd size: a pad byte follows
        byte[] data = new byte[300];
        for (int i = 0; i < 300; i++) data[i] = (byte)(i * 7 + 3);

        IffFile w = Iff.Create(path, form);
        Check(w.IsOpen, "the file is created");
        bool wrote = w.IsOpen;
        fixed (byte* h = header)
        fixed (byte* t = text)
        fixed (byte* d = data)
        {
            wrote = wrote && w.WriteChunk(head, h, 8);
            wrote = wrote && w.WriteChunk(name, t, 5);
            wrote = wrote && w.BeginChunk(body);                 // a chunk written in two pieces
            wrote = wrote && w.Write(d, 100) && w.Write(d + 100, 200);
            wrote = wrote && w.EndChunk();
        }
        Check(wrote, "three chunks are written");
        Check(w.Finish(), "the FORM is closed");

        IffFile r = Iff.OpenRead(path);
        Check(r.IsOpen, "the file opens for reading");
        Check(r.FormType == form, "the form type reads back");
        byte[] buf = new byte[400];
        int found = 0;
        bool good = true;
        fixed (byte* b = buf)
        {
            while (r.Next())
            {
                int got = r.Read(b, 400);
                found++;
                if (r.ChunkId == head) good = good && r.ChunkSize == 8 && got == 8 && b[0] == 1 && b[7] == 8;
                else if (r.ChunkId == name) good = good && r.ChunkSize == 5 && got == 5 && b[0] == 'h' && b[4] == 'o';
                else if (r.ChunkId == body)
                {
                    good = good && r.ChunkSize == 300 && got == 300;
                    for (int i = 0; i < 300 && good; i++) good = b[i] == (byte)(i * 7 + 3);
                }
                else good = false;
            }
        }
        Check(found == 3, "three chunks are found");
        Check(good, "every chunk reads back with the same size and bytes");
        r.Close();
        Check(!r.IsOpen, "the reader is closed");

        IffFile none = Iff.OpenRead("RAM:esharp_no_such_file.iff");
        Check(!none.IsOpen, "a missing file does not open");
        Files.Delete(path);
        Iff.Close();
    }
}
