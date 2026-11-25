// IFF reader and writer written in E# (over Files), used on sdl, on the dotnet host and, on the Amiga targets, when iffparse.library
// cannot be opened (or Iff.ForceBuiltIn is set). The Amiga keeps iffparse.library when it is there: its output is byte for byte the same.
using System;

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
/// Nested groups (FORM, LIST, CAT, PROP) are written with <see cref="BeginGroup"/> and <see cref="EndGroup"/>; reading steps into
/// them: <see cref="Next"/> reports the group itself (<see cref="ChunkId"/> is its kind, <see cref="ChunkType"/> its type, a
/// <see cref="Read"/> gives 0 bytes) and then the chunks inside it.
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
    /// <summary>For a group <see cref="Next"/> stopped at: its type (<c>Iff.Id("ILBM")</c>-style); 0 for an ordinary chunk.</summary>
    public uint ChunkType;
    /// <summary>True when <see cref="Next"/> returned false because the file is damaged or cut short (not just at its end).</summary>
    public bool Damaged;
    internal nuint Handle;
    internal nuint File;
    internal bool Writing;
    internal bool Library;                     // Amiga: iffparse.library does the work; otherwise the E# code below
    // State of the E# implementation: the whole file in memory (written by Finish, read by OpenRead)
    internal byte* Buf;
    internal int Len, Cap, Pos, DataStart, DataEnd, FormEnd, SizeAt, Depth;
    internal int* Stack;                       // writing: where the size field of each open group is
    internal bool InChunk, Truncated;
    internal string Path;

    /// <summary>Starts a chunk in a file being written. Its size is filled in by <see cref="EndChunk"/>.</summary>
    public bool BeginChunk(uint id)
    {
#if AMIGA
        if (Library)
        {
            if (!IsOpen || !Writing) return false;
            return IffParse.PushChunk((void*)Handle, 0, id, Layout.IffSizeUnknown) == 0;
        }
#endif
        return EsBeginChunk(id);
    }

    /// <summary>Appends bytes to the open chunk. False if nothing was written (no chunk open, or the disk is full).</summary>
    public bool Write(byte* data, int length)
    {
#if AMIGA
        if (Library)
        {
            if (!IsOpen || !Writing || length < 0) return false;
            return IffParse.WriteChunkBytes((void*)Handle, data, length) == length;
        }
#endif
        return EsWrite(data, length);
    }

    /// <summary>Ends the chunk (a pad byte follows an odd size, as the format needs).</summary>
    public bool EndChunk()
    {
#if AMIGA
        if (Library)
        {
            if (!IsOpen || !Writing) return false;
            return IffParse.PopChunk((void*)Handle) == 0;
        }
#endif
        return EsEndChunk();
    }

    /// <summary>Starts a nested group in a file being written: <paramref name="group"/> is <c>Iff.Id("FORM")</c>, <c>"LIST"</c>, <c>"CAT "</c> or <c>"PROP"</c>, <paramref name="type"/> its type. End it with <see cref="EndGroup"/> (no chunk may be open).</summary>
    public bool BeginGroup(uint group, uint type)
    {
        if (group != Iff.IdForm && group != Iff.IdList && group != Iff.IdCat && group != Iff.IdProp) return false;
#if AMIGA
        if (Library)
        {
            if (!IsOpen || !Writing) return false;
            return IffParse.PushChunk((void*)Handle, type, group, Layout.IffSizeUnknown) == 0;
        }
#endif
        return EsBeginGroup(group, type);
    }

    /// <summary>Ends the innermost group started with <see cref="BeginGroup"/> (its size is filled in). The file's own FORM is ended by <see cref="Finish"/>.</summary>
    public bool EndGroup()
    {
#if AMIGA
        if (Library)
        {
            if (!IsOpen || !Writing) return false;
            return IffParse.PopChunk((void*)Handle) == 0;
        }
#endif
        return EsEndGroup();
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
        if (Library)
        {
            if (!IsOpen || !Writing) return false;
            bool ok = IffParse.PopChunk((void*)Handle) == 0;          // the FORM
            IffParse.CloseIFF((void*)Handle);
            Iff.Release((void*)Handle, File);
            IsOpen = false;
            return ok;
        }
#endif
        return EsFinish();
    }

    /// <summary>Steps to the next chunk of a file being read. False at the end of the file (or on a damaged file).</summary>
    public bool Next()
    {
#if AMIGA
        if (Library)
        {
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
                ChunkType = Iff.IsGroup(ChunkId) ? *(uint*)(cn + Layout.CnType) : 0u;
                return true;
            }
            if ((uint)rc != Layout.IffErrEoc) { Damaged = (uint)rc != Layout.IffErrEof; return false; }   // the end of the file or an error; an end of chunk just goes on
        }
        return false;
        }
#endif
        return EsNext();
    }

    /// <summary>Reads up to <paramref name="max"/> bytes of the current chunk; returns how many (0 at its end, negative on an error).</summary>
    public int Read(byte* buffer, int max)
    {
#if AMIGA
        if (Library)
        {
            if (!IsOpen || Writing || max < 0) return -1;
            return IffParse.ReadChunkBytes((void*)Handle, buffer, max);
        }
#endif
        return EsRead(buffer, max);
    }

    /// <summary>Closes a file. A file being written should end with <see cref="Finish"/> instead.</summary>
    public void Close()
    {
        if (!IsOpen) return;
#if AMIGA
        if (Library)
        {
            IffParse.CloseIFF((void*)Handle);
            Iff.Release((void*)Handle, File);
            IsOpen = false;
            return;
        }
#endif
        if (Buf != null) Mem.Free(Buf);
        if (Stack != null) Mem.Free((byte*)Stack);
        Buf = null; Stack = null;
        IsOpen = false;
    }

    // ---- The E# implementation: the file is built in (or read into) memory, so it needs only Files and Mem. ----

    private bool Ensure(int extra)
    {
        if (extra < 0 || Len > 0x7FFFFFFF - extra) return false;
        int need = Len + extra;
        if (need <= Cap) return true;
        int cap = Cap < 4096 ? 4096 : Cap;
        while (cap < need) { if (cap > 0x3FFFFFFF) { cap = need; break; } cap *= 2; }
        byte* nb = Mem.AllocFast((uint)cap);
        if (nb == null) return false;
        for (int i = 0; i < Len; i++) nb[i] = Buf[i];
        if (Buf != null) Mem.Free(Buf);
        Buf = nb;
        Cap = cap;
        return true;
    }

    private void Put32(uint v)
    {
        Buf[Len] = (byte)(v >> 24); Buf[Len + 1] = (byte)(v >> 16); Buf[Len + 2] = (byte)(v >> 8); Buf[Len + 3] = (byte)v;
        Len += 4;
    }

    private void Patch32(int at, uint v)
    {
        Buf[at] = (byte)(v >> 24); Buf[at + 1] = (byte)(v >> 16); Buf[at + 2] = (byte)(v >> 8); Buf[at + 3] = (byte)v;
    }

    internal bool EsStartWrite(string path, uint formType)
    {
        Stack = (int*)Mem.AllocFast(16 * 4);
        if (Stack == null) return false;
        if (!Ensure(12)) { Mem.Free((byte*)Stack); Stack = null; return false; }
        Put32(Iff.IdForm); Put32(0); Put32(formType);
        // Create (or replace) the file now, as iffparse does, so an unwritable path is reported here and not by Finish.
        if (!Files.WriteAll(path, new ReadOnlySpan<byte>(Buf, Len))) { Mem.Free(Buf); Mem.Free((byte*)Stack); Buf = null; Stack = null; return false; }
        Path = path;
        Stack[0] = 4;
        Depth = 1;
        Writing = true;
        IsOpen = true;
        FormType = formType;
        return true;
    }

    private bool EsBeginChunk(uint id)
    {
        if (!IsOpen || !Writing || InChunk || !Ensure(8)) return false;
        Put32(id);
        SizeAt = Len;
        Put32(0);
        DataStart = Len;
        InChunk = true;
        return true;
    }

    private bool EsWrite(byte* data, int length)
    {
        if (!IsOpen || !Writing || !InChunk || length < 0 || !Ensure(length)) return false;
        for (int i = 0; i < length; i++) Buf[Len + i] = data[i];
        Len += length;
        return true;
    }

    private bool EsEndChunk()
    {
        if (!IsOpen || !Writing || !InChunk) return false;
        int size = Len - DataStart;
        if ((size & 1) != 0)
        {
            if (!Ensure(1)) return false;
            Buf[Len] = 0;
            Len++;
        }
        Patch32(SizeAt, (uint)size);
        InChunk = false;
        return true;
    }

    private bool EsBeginGroup(uint group, uint type)
    {
        if (!IsOpen || !Writing || InChunk || Depth >= 16 || !Ensure(12)) return false;
        Put32(group); Put32(0); Put32(type);
        Stack[Depth] = Len - 8;
        Depth++;
        return true;
    }

    private bool EsEndGroup()
    {
        if (!IsOpen || !Writing || InChunk || Depth < 2) return false;
        Depth--;
        int at = Stack[Depth];
        Patch32(at, (uint)(Len - at - 4));
        return true;
    }

    private bool EsFinish()
    {
        if (!IsOpen || !Writing || InChunk || Depth != 1) return false;
        Patch32(4, (uint)(Len - 8));
        bool ok = Files.WriteAll(Path, new ReadOnlySpan<byte>(Buf, Len));
        Mem.Free(Buf);
        Mem.Free((byte*)Stack);
        Buf = null; Stack = null;
        IsOpen = false;
        return ok;
    }

    internal bool EsStartRead(string path)
    {
        Buf = Files.ReadAll(path, out int size, false);
        if (Buf == null) return false;
        if (size < 12 || Iff.Get32(Buf, 0) != Iff.IdForm) { Mem.Free(Buf); Buf = null; return false; }
        uint formSize = Iff.Get32(Buf, 4);
        if (formSize < 4) { Mem.Free(Buf); Buf = null; return false; }
        Len = size;
        if (formSize > (uint)(size - 8)) { FormEnd = size; Truncated = true; }
        else FormEnd = 8 + (int)formSize;
        FormType = Iff.Get32(Buf, 8);
        Pos = 12;
        IsOpen = true;
        return true;
    }

    private bool EsNext()
    {
        if (!IsOpen || Writing) return false;
        ChunkType = 0;
        ChunkSize = 0;
        DataStart = DataEnd = Pos;                    // nothing to Read until a chunk is found
        if (Pos >= FormEnd) { Damaged = Truncated; return false; }
        if (Pos + 8 > FormEnd) { Damaged = true; return false; }
        uint id = Iff.Get32(Buf, Pos);
        uint size = Iff.Get32(Buf, Pos + 4);
        int start = Pos + 8;
        if (Iff.IsGroup(id))
        {
            // a group: report it, and the next step goes to the first chunk inside it
            if (size < 4 || size > (uint)(FormEnd - start)) { Damaged = true; return false; }
            ChunkId = id;
            ChunkSize = (int)size;
            ChunkType = Iff.Get32(Buf, start);
            Pos = start + 4;
            return true;
        }
        if (size > (uint)(FormEnd - start)) { Damaged = true; return false; }
        ChunkId = id;
        ChunkSize = (int)size;
        DataStart = start;
        DataEnd = start + (int)size;
        Pos = DataEnd + (int)(size & 1u);
        if (Pos > FormEnd) Pos = FormEnd;             // a last odd chunk without its pad byte
        return true;
    }

    private int EsRead(byte* buffer, int max)
    {
        if (!IsOpen || Writing || max < 0) return -1;
        int left = DataEnd - DataStart;
        int n = max < left ? max : left;
        for (int i = 0; i < n; i++) buffer[i] = Buf[DataStart + i];
        DataStart += n;
        return n;
    }
}

/// <summary>
/// IFF files, for saving and loading game data (the format ILBM pictures and 8SVX sounds use). The same calls work on every target
/// and give the same bytes. On sdl and on the dotnet host the reader and writer are E# code over <see cref="Files"/>. On the Amiga
/// targets iffparse.library does the work when it can be opened (it is on the Workbench disk, not in ROM); without it the same E#
/// code takes over, so a program needs no check. <see cref="Iff.ForceBuiltIn"/> chooses the E# code even where the library is there.
/// The path given to <see cref="Create"/> must stay valid until <see cref="IffFile.Finish"/>.
/// </summary>
public static unsafe class Iff
{
    public const uint IdForm = 0x464F524Du;     // "FORM"
    public const uint IdList = 0x4C495354u;     // "LIST"
    public const uint IdCat = 0x43415420u;      // "CAT "
    public const uint IdProp = 0x50524F50u;     // "PROP"

    /// <summary>Use the E# reader and writer even where iffparse.library is available (to compare the two). Set it before <see cref="Create"/> or <see cref="OpenRead"/>.</summary>
    public static bool ForceBuiltIn;

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

    internal static bool IsGroup(uint id) => id == IdForm || id == IdList || id == IdCat || id == IdProp;

    internal static uint Get32(byte* b, int p) => ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3];

    /// <summary>Creates (or replaces) the file at <paramref name="path"/> and starts a FORM of type <paramref name="formType"/> in it. Not open if the file cannot be written.</summary>
    public static IffFile Create(string path, uint formType)
    {
#if AMIGA
        if (!ForceBuiltIn && Open()) return CreateWithLibrary(path, formType);
#endif
        IffFile f = new IffFile();
        f.EsStartWrite(path, formType);
        return f;
    }

    /// <summary>Opens the IFF file at <paramref name="path"/> for reading and steps into its FORM (<see cref="IffFile.FormType"/> is set). Not open if the file is missing or not an IFF.</summary>
    public static IffFile OpenRead(string path)
    {
#if AMIGA
        if (!ForceBuiltIn && Open()) return OpenReadWithLibrary(path);
#endif
        IffFile f = new IffFile();
        f.EsStartRead(path);
        return f;
    }

#if AMIGA
    private static void* _base;

    /// <summary>Opens iffparse.library (v39) for <see cref="Create"/> and <see cref="OpenRead"/>, which call it themselves. False if it is not installed (the E# code is used then). Always false on sdl and on the host.</summary>
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
        f.Library = true;
        f.IsOpen = true;
        return f;
    }

    private static IffFile CreateWithLibrary(string path, uint formType)
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

    private static IffFile OpenReadWithLibrary(string path)
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
    /// <summary>No iffparse.library on this target: the E# code does the work. Always false.</summary>
    public static bool Open() { return false; }

    public static void Close() { }
#endif
}
