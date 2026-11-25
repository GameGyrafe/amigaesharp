using System;
#if HOST
using System.IO;
#endif

namespace ESharp.Platform;

#if AMIGA
/// <summary>dos.library file calls under their NDK names (the C functions of the same names are called).</summary>
internal static unsafe class DosLib
{
    [AmigaLib("dos", -30)] public static extern nuint Open(string name, int accessMode);
    [AmigaLib("dos", -36)] public static extern int Close(nuint file);
    [AmigaLib("dos", -42)] public static extern int Read(nuint file, byte* buffer, int length);
    [AmigaLib("dos", -48)] public static extern int Write(nuint file, byte* buffer, int length);
    [AmigaLib("dos", -66)] public static extern int Seek(nuint file, int position, int mode);
    [AmigaLib("dos", -72)] public static extern int DeleteFile(string name);
}
#endif

/// <summary>
/// Whole-file reads and writes: enough to load a sound or a data file into memory. A path is an AmigaDOS path on the Amiga
/// (<c>"DH0:Sounds/boom.wav"</c>, <c>"RAM:x"</c>) and an ordinary path on SDL and on the host.
/// </summary>
public static unsafe class Files
{
    /// <summary>
    /// Reads a whole file into a new block (fast RAM, or chip RAM when <paramref name="chip"/> is true). Free it with <see cref="Mem.Free"/>.
    /// Null, with <paramref name="size"/> 0, if the file is missing, empty or does not fit in memory.
    /// </summary>
    public static byte* ReadAll(string path, out int size, bool chip)
    {
        size = 0;
#if AMIGA
        nuint fh = DosLib.Open(path, 1005);                  // MODE_OLDFILE
        if (fh == 0) return null;
        DosLib.Seek(fh, 0, 1);                              // OFFSET_END
        int length = DosLib.Seek(fh, 0, -1);                // OFFSET_BEGINNING; returns where the end was
        if (length <= 0) { DosLib.Close(fh); return null; }
        byte* block = chip ? Mem.AllocChip((uint)length) : Mem.AllocFast((uint)length);
        if (block == null) { DosLib.Close(fh); return null; }
        int got = DosLib.Read(fh, block, length);
        DosLib.Close(fh);
        if (got != length) { Mem.Free(block); return null; }
        size = length;
        return block;
#elif SDL
        int length = Crt.es_std_file_size(path);
        if (length <= 0) return null;
        byte* block = chip ? Mem.AllocChip((uint)length) : Mem.AllocFast((uint)length);
        if (block == null) return null;
        if (Crt.es_std_file_read(path, block, length) != length) { Mem.Free(block); return null; }
        size = length;
        return block;
#else
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return null;
        byte* block = chip ? Mem.AllocChip((uint)bytes.Length) : Mem.AllocFast((uint)bytes.Length);
        if (block == null) return null;
        for (int i = 0; i < bytes.Length; i++) block[i] = bytes[i];
        size = bytes.Length;
        return block;
#endif
    }

    /// <summary>Writes (and replaces) a file. True if every byte was written.</summary>
    public static bool WriteAll(string path, ReadOnlySpan<byte> data)
    {
#if AMIGA
        nuint fh = DosLib.Open(path, 1006);                  // MODE_NEWFILE
        if (fh == 0) return false;
        int put = 0;
        if (data.Length > 0)
        {
            fixed (byte* p = data) put = DosLib.Write(fh, p, data.Length);
        }
        DosLib.Close(fh);
        return put == data.Length;
#elif SDL
        fixed (byte* p = data) return Crt.es_std_file_write(path, p, data.Length);
#else
        try { File.WriteAllBytes(path, data.ToArray()); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
#endif
    }

    /// <summary>Deletes a file. True if it is gone.</summary>
    public static bool Delete(string path)
    {
#if AMIGA
        return DosLib.DeleteFile(path) != 0;
#elif SDL
        return Crt.es_std_file_delete(path);
#else
        File.Delete(path);
        return true;
#endif
    }
}
