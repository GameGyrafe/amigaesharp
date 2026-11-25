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
/// <see cref="Read"/> gives 0 bytes) and then the chunks inside it; <see cref="ChunkDepth"/> says how deeply each is nested, which
/// is how the end of a group shows. A copy of an <c>IffFile</c> (passed by value to a helper) works on the same file, with the same
/// result on every target; what <see cref="Next"/> reports (<see cref="ChunkId"/> and the like) is set in the copy it was called on,
/// so pass the file by <c>ref</c> to a helper that steps through it. After <see cref="Finish"/> or <see cref="Close"/> use none of the copies.
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
    /// <summary>
    /// How deeply the chunk <see cref="Next"/> stopped at is nested: 0 for a chunk or group directly in the file's FORM, 1 inside a
    /// group of it, and so on. Together with the order of the steps it tells where each group ends (a chunk whose depth is lower
    /// than the one before it is outside the group the one before was in), so the nesting written by <see cref="BeginGroup"/> can be rebuilt.
    /// </summary>
    public int ChunkDepth;
    internal nuint Handle;                     // Amiga: the iffparse handle
    internal nuint File;
    internal bool Writing;
    internal bool Library;                     // Amiga: iffparse.library does the work; otherwise the E# code below
    internal nuint St;                         // the state block (IffState, on the heap): shared by every copy of this struct, see Iff.NewState

    /// <summary>Starts a chunk in a file being written. Its size is filled in by <see cref="EndChunk"/>.</summary>
    public bool BeginChunk(uint id)
    {
#if AMIGA
        if (Library)
        {
            if (!IsOpen || !Writing) return false;
            IffState* st = (IffState*)St;
            if (st->InChunk || IffParse.PushChunk((void*)Handle, 0, id, Layout.IffSizeUnknown) != 0) return false;
            st->InChunk = true;
            return true;
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
            if (!IsOpen || !Writing || length < 0 || !((IffState*)St)->InChunk) return false;
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
            IffState* st = (IffState*)St;
            if (!st->InChunk) return false;
            st->InChunk = false;
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
            IffState* st = (IffState*)St;
            if (st->InChunk || st->Depth >= Iff.MaxDepth || IffParse.PushChunk((void*)Handle, type, group, Layout.IffSizeUnknown) != 0) return false;
            st->Depth++;
            return true;
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
            IffState* st = (IffState*)St;
            if (st->InChunk || st->Depth < 1) return false;
            st->Depth--;
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
            IffState* st = (IffState*)St;
            if (st->InChunk || st->Depth != 0) return false;
            bool ok = IffParse.PopChunk((void*)Handle) == 0;          // the FORM
            IffParse.CloseIFF((void*)Handle);
            Iff.Release((void*)Handle, File);
            FreeState();
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
        if (!IsOpen || Writing || ((IffState*)St)->Truncated) return false;     // Truncated: a damaged file stays at its error
        for (int guard = 0; guard < 100000; guard++)
        {
            int rc = IffParse.ParseIFF((void*)Handle, (int)Layout.IffParseRawStep);
            if (rc == 0)
            {
                byte* cn = (byte*)IffParse.CurrentChunk((void*)Handle);
                if (cn == null) return false;
                IffState* st = (IffState*)St;
                ChunkId = *(uint*)(cn + Layout.CnId);
                ChunkSize = *(int*)(cn + Layout.CnSize);
                // iffparse reports a chunk whose header it could read and only fails when its data is read; the E# reader does not
                // report a chunk that does not fit, so check against the file's length here. The position is followed by the
                // sizes (header 8 bytes, a group's type 4 more, data and pad), not asked of the stream, which iffparse may read ahead.
                bool grp = Iff.IsGroup(ChunkId);
                int dataStart = st->Pos + (grp ? 12 : 8);
                int dataLen = grp ? ChunkSize - 4 : ChunkSize;
                if (st->Len < 0 || dataLen < 0 || dataLen > st->Len - dataStart)
                {
                    st->Truncated = true;
                    Damaged = true;
                    return false;
                }
                st->Pos = grp ? dataStart : dataStart + ChunkSize + (ChunkSize & 1);
                ChunkDepth = st->Depth;
                if (Iff.IsGroup(ChunkId)) { ChunkType = *(uint*)(cn + Layout.CnType); st->Depth++; }
                else ChunkType = 0u;
                return true;
            }
            if ((uint)rc != Layout.IffErrEoc) { Damaged = (uint)rc != Layout.IffErrEof; return false; }   // the end of the file or an error
            // an end of chunk just goes on; the end of a group (not the file's own FORM) lowers the depth
            byte* ended = (byte*)IffParse.CurrentChunk((void*)Handle);
            IffState* es = (IffState*)St;
            if (ended != null && Iff.IsGroup(*(uint*)(ended + Layout.CnId)) && es->Depth > 0) es->Depth--;
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
            FreeState();
            IsOpen = false;
            return;
        }
#endif
        FreeState();
        IsOpen = false;
    }

    // ---- The E# implementation: the file is built in (or read into) memory, so it needs only Files and Mem. All its state is
    // in the heap block behind St, so a copy of this struct (passed by value to a helper) works on the same file. ----

    private bool Ensure(IffState* s, int extra)
    {
        if (extra < 0 || s->Len > 0x7FFFFFFF - extra) return false;
        int need = s->Len + extra;
        if (need <= s->Cap) return true;
        int cap = s->Cap < 4096 ? 4096 : s->Cap;
        while (cap < need) { if (cap > 0x3FFFFFFF) { cap = need; break; } cap *= 2; }
        byte* nb = Mem.AllocFast((uint)cap);
        if (nb == null) return false;
        for (int i = 0; i < s->Len; i++) nb[i] = s->Buf[i];
        if (s->Buf != null) Mem.Free(s->Buf);
        s->Buf = nb;
        s->Cap = cap;
        return true;
    }

    private static void Put32(IffState* s, uint v)
    {
        byte* b = s->Buf;
        int n = s->Len;
        b[n] = (byte)(v >> 24); b[n + 1] = (byte)(v >> 16); b[n + 2] = (byte)(v >> 8); b[n + 3] = (byte)v;
        s->Len = n + 4;
    }

    private static void Patch32(IffState* s, int at, uint v)
    {
        byte* b = s->Buf;
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    internal void FreeState()
    {
        IffState* s = (IffState*)St;
        if (s != null)
        {
            if (s->Buf != null) Mem.Free(s->Buf);
            Mem.Free((byte*)s);
        }
        St = 0;
    }

    internal bool EsStartWrite(string path, uint formType)
    {
        nuint st = Iff.NewState();
        if (st == 0) return false;
        IffState* s = (IffState*)st;
        if (!Ensure(s, 12)) { Mem.Free((byte*)s); return false; }
        Put32(s, Iff.IdForm); Put32(s, 0); Put32(s, formType);
        // Create (or replace) the file now, as iffparse does, so an unwritable path is reported here and not by Finish.
        if (!Files.WriteAll(path, new ReadOnlySpan<byte>(s->Buf, s->Len))) { Mem.Free(s->Buf); Mem.Free((byte*)s); return false; }
        s->Path = path;
        s->Stack[0] = 4;
        s->Depth = 1;
        St = st;
        Writing = true;
        IsOpen = true;
        FormType = formType;
        return true;
    }

    private bool EsBeginChunk(uint id)
    {
        IffState* s = (IffState*)St;
        if (!IsOpen || !Writing || s->InChunk || !Ensure(s, 8)) return false;
        Put32(s, id);
        s->SizeAt = s->Len;
        Put32(s, 0);
        s->DataStart = s->Len;
        s->InChunk = true;
        return true;
    }

    private bool EsWrite(byte* data, int length)
    {
        IffState* s = (IffState*)St;
        if (!IsOpen || !Writing || !s->InChunk || length < 0 || !Ensure(s, length)) return false;
        byte* b = s->Buf + s->Len;
        for (int i = 0; i < length; i++) b[i] = data[i];
        s->Len += length;
        return true;
    }

    private bool EsEndChunk()
    {
        IffState* s = (IffState*)St;
        if (!IsOpen || !Writing || !s->InChunk) return false;
        int size = s->Len - s->DataStart;
        if ((size & 1) != 0)
        {
            if (!Ensure(s, 1)) return false;
            s->Buf[s->Len] = 0;
            s->Len++;
        }
        Patch32(s, s->SizeAt, (uint)size);
        s->InChunk = false;
        return true;
    }

    private bool EsBeginGroup(uint group, uint type)
    {
        IffState* s = (IffState*)St;
        if (!IsOpen || !Writing || s->InChunk || s->Depth >= Iff.MaxDepth || !Ensure(s, 12)) return false;
        Put32(s, group); Put32(s, 0); Put32(s, type);
        s->Stack[s->Depth] = s->Len - 8;
        s->Depth++;
        return true;
    }

    private bool EsEndGroup()
    {
        IffState* s = (IffState*)St;
        if (!IsOpen || !Writing || s->InChunk || s->Depth < 2) return false;
        s->Depth--;
        int at = s->Stack[s->Depth];
        Patch32(s, at, (uint)(s->Len - at - 4));
        return true;
    }

    private bool EsFinish()
    {
        IffState* s = (IffState*)St;
        if (!IsOpen || !Writing || s->InChunk || s->Depth != 1) return false;
        Patch32(s, 4, (uint)(s->Len - 8));
        bool ok = Files.WriteAll(s->Path, new ReadOnlySpan<byte>(s->Buf, s->Len));
        FreeState();
        IsOpen = false;
        return ok;
    }

    internal bool EsStartRead(string path)
    {
        byte* buf = Files.ReadAll(path, out int size, false);
        if (buf == null) return false;
        if (size < 12 || Iff.Get32(buf, 0) != Iff.IdForm) { Mem.Free(buf); return false; }
        uint formSize = Iff.Get32(buf, 4);
        if (formSize < 4) { Mem.Free(buf); return false; }
        nuint st = Iff.NewState();
        if (st == 0) { Mem.Free(buf); return false; }
        IffState* s = (IffState*)st;
        s->Buf = buf;
        s->Len = size;
        if (formSize > (uint)(size - 8)) { s->FormEnd = size; s->Truncated = true; }
        else s->FormEnd = 8 + (int)formSize;
        FormType = Iff.Get32(buf, 8);
        s->Pos = 12;
        St = st;
        IsOpen = true;
        return true;
    }

    private bool EsNext()
    {
        if (!IsOpen || Writing) return false;
        IffState* s = (IffState*)St;
        ChunkType = 0;
        ChunkSize = 0;
        s->DataStart = s->DataEnd = s->Pos;           // nothing to Read until a chunk is found
        while (s->Depth > 0 && s->Pos >= s->Stack[s->Depth - 1]) s->Depth--;   // leave the groups that have ended
        int limit = s->Depth > 0 ? s->Stack[s->Depth - 1] : s->FormEnd;
        ChunkDepth = s->Depth;
        if (s->Pos >= limit) { Damaged = s->Truncated && s->Depth == 0; return false; }
        if (s->Pos + 8 > limit) { Damaged = true; return false; }
        uint id = Iff.Get32(s->Buf, s->Pos);
        uint size = Iff.Get32(s->Buf, s->Pos + 4);
        int start = s->Pos + 8;
        if (Iff.IsGroup(id))
        {
            // a group: report it, and the next step goes to the first chunk inside it
            if (size < 4 || size > (uint)(limit - start) || s->Depth >= Iff.MaxDepth) { Damaged = true; return false; }
            ChunkId = id;
            ChunkSize = (int)size;
            ChunkType = Iff.Get32(s->Buf, start);
            s->Stack[s->Depth] = start + (int)size;
            s->Depth++;
            s->Pos = start + 4;
            return true;
        }
        if (size > (uint)(limit - start)) { Damaged = true; return false; }
        ChunkId = id;
        ChunkSize = (int)size;
        s->DataStart = start;
        s->DataEnd = start + (int)size;
        s->Pos = s->DataEnd + (int)(size & 1u);
        if (s->Pos > limit) s->Pos = limit;           // a last odd chunk without its pad byte
        return true;
    }

    private int EsRead(byte* buffer, int max)
    {
        if (!IsOpen || Writing || max < 0) return -1;
        IffState* s = (IffState*)St;
        int left = s->DataEnd - s->DataStart;
        int n = max < left ? max : left;
        for (int i = 0; i < n; i++) buffer[i] = s->Buf[s->DataStart + i];
        s->DataStart += n;
        return n;
    }
}

/// <summary>The state of one <see cref="IffFile"/> (the E# implementation, and the group depth of the iffparse reader). It lives on the heap so that copies of the struct share it.</summary>
internal unsafe struct IffState
{
    public byte* Buf;
    public int Len, Cap, Pos, DataStart, DataEnd, FormEnd, SizeAt;
    public int Depth;                 // writing: open groups including the FORM; reading: groups open inside the FORM
    public int* Stack;                // writing: where the size field of each open group is; reading: where each open group ends
    public bool InChunk, Truncated;
    public string Path;
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

    /// <summary>The most groups that can be open inside a file's FORM, written or read (a deeper file is damaged to the reader; <c>BeginGroup</c> returns false).</summary>
    public const int MaxDepth = 16;

    /// <summary>A zeroed heap block for the state of one <see cref="IffFile"/>, with room for the group stack after it. 0 if memory is short.</summary>
    internal static nuint NewState()
    {
        int size = sizeof(IffState) + (MaxDepth + 1) * 4;
        byte* b = Mem.AllocFast((uint)size);
        if (b == null) return 0;
        for (int i = 0; i < size; i++) b[i] = 0;
        ((IffState*)b)->Stack = (int*)(b + sizeof(IffState));
        return (nuint)b;
    }

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
        nuint st = NewState();
        if (st == 0) { DosLib.Close(fh); return f; }
        if (!write)
        {
            DosLib.Seek(fh, 0, 1);                                      // OFFSET_END
            int length = DosLib.Seek(fh, 0, -1);                        // OFFSET_BEGINNING; returns where the end was
            ((IffState*)st)->Len = length < 0 ? 0x7FFFFFFF : length;    // unknown: no check
        }
        void* iff = IffParse.AllocIFF();
        if (iff == null) { Mem.Free((byte*)st); DosLib.Close(fh); return f; }
        *(nuint*)((byte*)iff + Layout.IffStream) = fh;
        IffParse.InitIFFasDOS(iff);
        if (IffParse.OpenIFF(iff, (int)(write ? Layout.IffWrite : Layout.IffRead)) != 0) { Release(iff, fh); Mem.Free((byte*)st); return f; }
        f.Handle = (nuint)iff;
        f.St = st;
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
            f.FreeState();
            DosLib.DeleteFile(path);
            return new IffFile();
        }
        return f;
    }

    private static IffFile OpenReadWithLibrary(string path)
    {
        IffFile f = Start(path, false);
        if (!f.IsOpen) return f;
        ((IffState*)f.St)->Pos = 12;                                    // the FORM header is read below
        if (IffParse.ParseIFF((void*)f.Handle, (int)Layout.IffParseRawStep) != 0)
        {
            IffParse.CloseIFF((void*)f.Handle);
            Release((void*)f.Handle, f.File);
            f.FreeState();
            return new IffFile();
        }
        byte* cn = (byte*)IffParse.CurrentChunk((void*)f.Handle);
        if (cn == null || *(uint*)(cn + Layout.CnId) != Layout.IdForm)
        {
            IffParse.CloseIFF((void*)f.Handle);
            Release((void*)f.Handle, f.File);
            f.FreeState();
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
