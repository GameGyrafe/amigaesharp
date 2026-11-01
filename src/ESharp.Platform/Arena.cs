namespace ESharp.Platform;

/// <summary>
/// A bump-pointer arena: one up-front allocation, cheap allocation inside it, and everything freed
/// at once by <see cref="Reset"/> or <see cref="Destroy"/>. There is no per-object free and no
/// fragmentation. Memory is zeroed when the arena is created; <see cref="Reset"/> does not clear it.
/// </summary>
public unsafe struct Arena
{
    public byte* Base;
    public uint Size;
    public uint Used;

    /// <summary>Allocates <paramref name="size"/> bytes of chip or fast RAM. Check <see cref="Size"/> for 0 on failure.</summary>
    public Arena(uint size, bool chip)
    {
        Base = chip ? Mem.AllocChip(size) : Mem.AllocFast(size);
        Size = Base == null ? 0 : size;
        Used = 0;
    }

    /// <summary>Takes <paramref name="size"/> bytes aligned to <paramref name="align"/> (a power of two). Null if full.</summary>
    public byte* Alloc(uint size, uint align)
    {
        if (Base == null || size == 0) return null;
        if (align == 0) align = 1;
        uint start = (Used + align - 1) & ~(align - 1);
        if (start < Used || start > Size || size > Size - start) return null;
        Used = start + size;
        return Base + start;
    }

    public uint Remaining() { return Size - Used; }

    /// <summary>Makes the whole arena available again. Earlier pointers must no longer be used.</summary>
    public void Reset() { Used = 0; }

    /// <summary>Returns the block to the system.</summary>
    public void Destroy()
    {
        Mem.Free(Base);
        Base = null;
        Size = 0;
        Used = 0;
    }
}
