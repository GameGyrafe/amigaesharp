namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// iffparse.library bindings. The library is on the Workbench disk (<c>Libs:</c>), not in ROM, so <see cref="Iff"/> opens it
/// first and only then calls these. Vectors from the classic fd file (checked with tools/BindGen).
/// </summary>
[LibraryBase("iffparse", "IFFParseBase", "struct Library")]
public static unsafe class IffParse
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    [AmigaLib("iffparse", -30), Since(36)] public static extern void* AllocIFF();
    [AmigaLib("iffparse", -36), Since(36)] public static extern int OpenIFF(void* iff, int rwMode);
    [AmigaLib("iffparse", -42), Since(36)] public static extern int ParseIFF(void* iff, int control);
    [AmigaLib("iffparse", -48), Since(36)] public static extern void CloseIFF(void* iff);
    [AmigaLib("iffparse", -54), Since(36)] public static extern void FreeIFF(void* iff);
    [AmigaLib("iffparse", -60), Since(36)] public static extern int ReadChunkBytes(void* iff, byte* buffer, int numBytes);
    [AmigaLib("iffparse", -66), Since(36)] public static extern int WriteChunkBytes(void* iff, byte* buffer, int numBytes);
    [AmigaLib("iffparse", -84), Since(36)] public static extern int PushChunk(void* iff, uint type, uint id, uint size);
    [AmigaLib("iffparse", -90), Since(36)] public static extern int PopChunk(void* iff);
    [AmigaLib("iffparse", -174), Since(36)] public static extern void* CurrentChunk(void* iff);
    [AmigaLib("iffparse", -234), Since(36)] public static extern void InitIFFasDOS(void* iff);
}
#endif

/// <summary>
/// One IFF file being written or read (see <see cref="Iff"/>). <see cref="IsOpen"/> is false if it could not be opened.
/// A file is one FORM with a form type (<c>Iff.Id("ILBM")</c>) holding chunks. To write: <see cref="BeginChunk"/>, one or more
/// <see cref="Write"/> calls, <see cref="EndChunk"/>, and <see cref="Finish"/> at the end. To read: <see cref="Next"/> steps
/// to the next chunk (its <see cref="ChunkId"/> and <see cref="ChunkSize"/>), <see cref="Read"/> gets its bytes.
/// </summary>
public unsafe struct IffFile
{
    public bool IsOpen;
    /// <summary>The form type: what was given to <see cref="Iff.Create"/>, or read from the file by <see cref="Iff.OpenRead"/>.</summary>
    public uint FormType;
    /// <summary>The chunk <see cref="Next"/> stopped at (<c>Iff.Id("BMHD")</c>-style).</summary>
    public uint ChunkId;
    /// <summary>Its data size in bytes.</summary>
    public int ChunkSize;
    internal nuint Handle;
    internal nuint File;
    internal bool Writing;

    /// <summary>Starts a chunk in a file being written. Its size is filled in by <see cref="EndChunk"/>.</summary>
    public bool BeginChunk(uint id)
    {
#if AMIGA
        if (!IsOpen || !Writing) return false;
        return IffParse.PushChunk((void*)Handle, 0, id, Layout.IffSizeUnknown) == 0;
#else
        return Iff.HostBeginChunk(ref this, id);
#endif
    }

    /// <summary>Appends bytes to the open chunk. False if nothing was written (no chunk open, or the disk is full).</summary>
    public bool Write(byte* data, int length)
    {
#if AMIGA
        if (!IsOpen || !Writing || length < 0) return false;
        return IffParse.WriteChunkBytes((void*)Handle, data, length) == length;
#else
        return Iff.HostWrite(ref this, data, length);
#endif
    }

    /// <summary>Ends the chunk (a pad byte follows an odd size, as the format needs).</summary>
    public bool EndChunk()
    {
#if AMIGA
        if (!IsOpen || !Writing) return false;
        return IffParse.PopChunk((void*)Handle) == 0;
#else
        return Iff.HostEndChunk(ref this);
#endif
    }

    /// <summary>Writes a whole chunk: <see cref="BeginChunk"/>, <see cref="Write"/>, <see cref="EndChunk"/>.</summary>
    public bool WriteChunk(uint id, byte* data, int length)
    {
        if (!BeginChunk(id)) return false;
        if (length > 0 && !Write(data, length)) return false;
        return EndChunk();
    }

    /// <summary>Ends the FORM and closes the file. Returns false if a write failed. Call it once.</summary>
    public bool Finish()
    {
#if AMIGA
        if (!IsOpen || !Writing) return false;
        bool ok = IffParse.PopChunk((void*)Handle) == 0;          // the FORM
        IffParse.CloseIFF((void*)Handle);
        Iff.Release((void*)Handle, File);
        IsOpen = false;
        return ok;
#else
        return Iff.HostFinish(ref this);
#endif
    }

    /// <summary>Steps to the next chunk of a file being read. False at the end of the file (or on a damaged file).</summary>
    public bool Next()
    {
#if AMIGA
        if (!IsOpen || Writing) return false;
        for (int guard = 0; guard < 100000; guard++)
        {
            int rc = IffParse.ParseIFF((void*)Handle, (int)Layout.IffParseRawStep);
            if (rc == 0)
            {
                byte* cn = (byte*)IffParse.CurrentChunk((void*)Handle);
                if (cn == null) return false;
                ChunkId = *(uint*)(cn + Layout.CnId);
                ChunkSize = *(int*)(cn + Layout.CnSize);
                return true;
            }
            if ((uint)rc != Layout.IffErrEoc) return false;        // end of file (or an error); an end of chunk just goes on
        }
        return false;
#else
        return Iff.HostNext(ref this);
#endif
    }

    /// <summary>Reads up to <paramref name="max"/> bytes of the current chunk; returns how many (0 at its end, negative on an error).</summary>
    public int Read(byte* buffer, int max)
    {
#if AMIGA
        if (!IsOpen || Writing || max < 0) return -1;
        return IffParse.ReadChunkBytes((void*)Handle, buffer, max);
#else
        return Iff.HostRead(ref this, buffer, max);
#endif
    }

    /// <summary>Closes a file. A file being written should end with <see cref="Finish"/> instead.</summary>
    public void Close()
    {
        if (!IsOpen) return;
#if AMIGA
        IffParse.CloseIFF((void*)Handle);
        Iff.Release((void*)Handle, File);
        IsOpen = false;
#else
        Iff.HostClose(ref this);
#endif
    }
}

/// <summary>
/// IFF files through iffparse.library, for saving and loading game data (the format ILBM pictures and 8SVX sounds use).
/// The library is on the Workbench disk, not in ROM: if it is missing <see cref="Open"/> returns false, and
/// <see cref="Create"/> and <see cref="OpenRead"/> give a file with <see cref="IffFile.IsOpen"/> false. On the host
/// the same files are read and written with plain .NET, so a file written on either can be read on the other.
/// </summary>
public static unsafe class Iff
{
    /// <summary>The four-character chunk or form id <paramref name="text"/> as a number (<c>Iff.Id("FORM")</c>). Shorter text is padded with spaces.</summary>
    public static uint Id(string text)
    {
        uint v = 0;
        for (int i = 0; i < 4; i++)
        {
            uint c = i < text.Length ? (uint)text[i] : 32u;
            v = (v << 8) | (c & 255u);
        }
        return v;
    }

#if AMIGA
    private static void* _base;

    /// <summary>Opens iffparse.library (v39). False if it is not installed.</summary>
    public static bool Open()
    {
        if (_base != null) return true;
        _base = Exec.OpenLibrary("iffparse.library", 39);
        if (_base == null) return false;
        IffParse.SetBase(_base);
        return true;
    }

    /// <summary>Closes iffparse.library. Finish or close every file first.</summary>
    public static void Close()
    {
        if (_base == null) return;
        IffParse.SetBase(null);
        Exec.CloseLibrary(_base);
        _base = null;
    }

    internal static void Release(void* iff, nuint file)
    {
        if (iff != null) IffParse.FreeIFF(iff);
        if (file != 0) DosLib.Close(file);
    }

    private static IffFile Start(string path, bool write)
    {
        IffFile f = new IffFile();
        if (!Open()) return f;
        nuint fh = DosLib.Open(path, write ? 1006 : 1005);          // MODE_NEWFILE / MODE_OLDFILE
        if (fh == 0) return f;
        void* iff = IffParse.AllocIFF();
        if (iff == null) { DosLib.Close(fh); return f; }
        *(nuint*)((byte*)iff + Layout.IffStream) = fh;
        IffParse.InitIFFasDOS(iff);
        if (IffParse.OpenIFF(iff, (int)(write ? Layout.IffWrite : Layout.IffRead)) != 0) { Release(iff, fh); return f; }
        f.Handle = (nuint)iff;
        f.File = fh;
        f.Writing = write;
        f.IsOpen = true;
        return f;
    }

    /// <summary>Creates (or replaces) the file at <paramref name="path"/> and starts a FORM of type <paramref name="formType"/> in it.</summary>
    public static IffFile Create(string path, uint formType)
    {
        IffFile f = Start(path, true);
        if (!f.IsOpen) return f;
        f.FormType = formType;
        if (IffParse.PushChunk((void*)f.Handle, formType, Layout.IdForm, Layout.IffSizeUnknown) != 0)
        {
            IffParse.CloseIFF((void*)f.Handle);
            Release((void*)f.Handle, f.File);
            DosLib.DeleteFile(path);
            return new IffFile();
        }
        return f;
    }

    /// <summary>Opens the IFF file at <paramref name="path"/> for reading and steps into its FORM (<see cref="IffFile.FormType"/> is set). Not open if the file is missing or not an IFF.</summary>
    public static IffFile OpenRead(string path)
    {
        IffFile f = Start(path, false);
        if (!f.IsOpen) return f;
        if (IffParse.ParseIFF((void*)f.Handle, (int)Layout.IffParseRawStep) != 0)
        {
            IffParse.CloseIFF((void*)f.Handle);
            Release((void*)f.Handle, f.File);
            return new IffFile();
        }
        byte* cn = (byte*)IffParse.CurrentChunk((void*)f.Handle);
        if (cn == null || *(uint*)(cn + Layout.CnId) != Layout.IdForm)
        {
            IffParse.CloseIFF((void*)f.Handle);
            Release((void*)f.Handle, f.File);
            return new IffFile();
        }
        f.FormType = *(uint*)(cn + Layout.CnType);
        return f;
    }
#else
    private sealed class HostFile
    {
        public string Path = "";
        public System.IO.MemoryStream Out = new();
        public byte[] In = System.Array.Empty<byte>();
        public int Pos, ChunkStart, ChunkEnd, ChunkSizeAt;
        public bool InChunk;
    }

    private static readonly System.Collections.Generic.List<HostFile?> Files = new();

    /// <summary>Host: whether the fake iffparse.library is "installed". Set false to test the missing-library path.</summary>
    public static bool HostAvailable = true;

    public static bool Open() { return HostAvailable; }

    public static void Close() { }

    private static IffFile Make(HostFile h, bool write, uint form)
    {
        Files.Add(h);
        return new IffFile { IsOpen = true, Writing = write, FormType = form, Handle = (nuint)Files.Count };
    }

    private static void Put32(System.IO.Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    public static IffFile Create(string path, uint formType)
    {
        if (!HostAvailable) return new IffFile();
        var h = new HostFile { Path = path };
        Put32(h.Out, Id("FORM")); Put32(h.Out, 0); Put32(h.Out, formType);
        return Make(h, true, formType);
    }

    private static uint Get32(byte[] b, int p) { return ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3]; }

    public static IffFile OpenRead(string path)
    {
        if (!HostAvailable || !System.IO.File.Exists(path)) return new IffFile();
        byte[] data = System.IO.File.ReadAllBytes(path);
        if (data.Length < 12 || Get32(data, 0) != Id("FORM")) return new IffFile();
        var h = new HostFile { Path = path, In = data, Pos = 12 };
        return Make(h, false, Get32(data, 8));
    }

    private static HostFile? Of(ref IffFile f) { return f.IsOpen && f.Handle >= 1 && (int)f.Handle <= Files.Count ? Files[(int)f.Handle - 1] : null; }

    internal static bool HostBeginChunk(ref IffFile f, uint id)
    {
        var h = Of(ref f);
        if (h == null || !f.Writing || h.InChunk) return false;
        Put32(h.Out, id);
        h.ChunkSizeAt = (int)h.Out.Length;
        Put32(h.Out, 0);
        h.InChunk = true;
        return true;
    }

    internal static bool HostWrite(ref IffFile f, byte* data, int length)
    {
        var h = Of(ref f);
        if (h == null || !f.Writing || !h.InChunk || length < 0) return false;
        for (int i = 0; i < length; i++) h.Out.WriteByte(data[i]);
        return true;
    }

    internal static bool HostEndChunk(ref IffFile f)
    {
        var h = Of(ref f);
        if (h == null || !f.Writing || !h.InChunk) return false;
        int size = (int)h.Out.Length - h.ChunkSizeAt - 4;
        if ((size & 1) != 0) h.Out.WriteByte(0);
        var end = h.Out.Position;
        h.Out.Position = h.ChunkSizeAt;
        Put32(h.Out, (uint)size);
        h.Out.Position = end;
        h.InChunk = false;
        return true;
    }

    internal static bool HostFinish(ref IffFile f)
    {
        var h = Of(ref f);
        if (h == null || !f.Writing || h.InChunk) { return false; }
        h.Out.Position = 4;
        Put32(h.Out, (uint)(h.Out.Length - 8));
        System.IO.File.WriteAllBytes(h.Path, h.Out.ToArray());
        Files[(int)f.Handle - 1] = null;
        f.IsOpen = false;
        return true;
    }

    internal static bool HostNext(ref IffFile f)
    {
        var h = Of(ref f);
        if (h == null || f.Writing) return false;
        int p = h.InChunk ? h.ChunkEnd : h.Pos;
        if (p + 8 > h.In.Length) return false;
        f.ChunkId = Get32(h.In, p);
        f.ChunkSize = (int)Get32(h.In, p + 4);
        if (f.ChunkSize < 0 || p + 8 + f.ChunkSize > h.In.Length) return false;
        h.ChunkStart = p + 8;
        h.Pos = h.ChunkStart;
        h.ChunkEnd = h.ChunkStart + f.ChunkSize + (f.ChunkSize & 1);
        h.InChunk = true;
        return true;
    }

    internal static int HostRead(ref IffFile f, byte* buffer, int max)
    {
        var h = Of(ref f);
        if (h == null || f.Writing || !h.InChunk || max < 0) return -1;
        int left = h.ChunkStart + f.ChunkSize - h.Pos;
        int n = max < left ? max : left;
        for (int i = 0; i < n; i++) buffer[i] = h.In[h.Pos + i];
        h.Pos += n;
        return n;
    }

    internal static void HostClose(ref IffFile f)
    {
        var h = Of(ref f);
        if (h == null) return;
        Files[(int)f.Handle - 1] = null;
        f.IsOpen = false;
    }
#endif
}
