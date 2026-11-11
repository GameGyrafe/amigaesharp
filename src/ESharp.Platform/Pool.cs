namespace ESharp.Platform;

/// <summary>
/// A fixed-capacity pool of <typeparamref name="T"/> items with O(1) <see cref="Alloc"/> and
/// <see cref="Free"/>: one up-front allocation, no fragmentation. Items come back zeroed. A free list
/// of slot numbers and a per-slot in-use flag live in the same block, so freeing something that is
/// not an item of this pool, or freeing it twice, is detected and ignored (<see cref="Free"/> returns false).
/// With <c>DEBUG</c> (<c>esharp build --debug</c>) freed slots are filled with 0xDD.
/// </summary>
public unsafe struct Pool<T> where T : unmanaged
{
    private byte* _block;
    private uint* _free;     // stack of free slot numbers
    private byte* _used;     // 1 per slot while allocated
    private T* _items;
    private uint _capacity;
    private uint _freeCount;

    /// <summary>Allocates room for <paramref name="capacity"/> items. Check <see cref="Capacity"/> for 0 on failure.</summary>
    public Pool(uint capacity, bool chip)
    {
        uint itemBytes = (uint)sizeof(T);
        // Layout: free list (padded to 8 bytes so the items are aligned for pointers) | items | flags.
        // A size that does not fit in 32 bits is refused up front (it would wrap and give a small block), on every target.
        bool fits = capacity <= (0xFFFFFFFFu - 8) / (itemBytes + 5);
        uint listBytes = (capacity * 4 + 7) & ~7u;
        uint total = listBytes + capacity * itemBytes + capacity;
        _block = capacity == 0 || !fits ? null : (chip ? Mem.AllocChip(total) : Mem.AllocFast(total));
        _free = (uint*)_block;
        _items = (T*)(_block + listBytes);
        _used = _block + listBytes + capacity * itemBytes;
        _capacity = _block == null ? 0 : capacity;
        _freeCount = _capacity;
        for (uint i = 0; i < _capacity; i++) _free[i] = _capacity - 1 - i;
    }

    public uint Capacity() { return _capacity; }

    public uint InUse() { return _capacity - _freeCount; }

    /// <summary>A zeroed item, or null when the pool is full.</summary>
    public T* Alloc()
    {
        if (_freeCount == 0) return null;
        _freeCount--;
        uint slot = _free[_freeCount];
        _used[slot] = 1;
        T* item = _items + slot;
        byte* bytes = (byte*)item;
        for (uint i = 0; i < (uint)sizeof(T); i++) bytes[i] = 0;
        return item;
    }

    /// <summary>Returns an item to the pool. False (and nothing happens) if it is not a live item of this pool.</summary>
    public bool Free(T* item)
    {
        if (_capacity == 0 || (byte*)item < (byte*)_items) return false;
        uint offset = (uint)((byte*)item - (byte*)_items);
        uint size = (uint)sizeof(T);
        if (offset % size != 0) return false;
        uint slot = offset / size;
        if (slot >= _capacity || _used[slot] == 0) return false;
        _used[slot] = 0;
#if DEBUG
        byte* bytes = (byte*)item;
        for (uint i = 0; i < size; i++) bytes[i] = 0xDD;
#endif
        _free[_freeCount] = slot;
        _freeCount++;
        return true;
    }

    /// <summary>Frees every item at once. Earlier pointers must no longer be used.</summary>
    public void Reset()
    {
        for (uint i = 0; i < _capacity; i++) { _free[i] = _capacity - 1 - i; _used[i] = 0; }
        _freeCount = _capacity;
    }

    /// <summary>Returns the block to the system.</summary>
    public void Destroy()
    {
        Mem.Free(_block);
        _block = null;
        _free = null;
        _items = null;
        _used = null;
        _capacity = 0;
        _freeCount = 0;
    }
}
