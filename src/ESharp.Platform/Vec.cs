using System;

namespace ESharp.Platform;

/// <summary>
/// A growable list of <typeparamref name="T"/> in one block of memory (Fast RAM unless <c>chip</c> is set).
/// Growing allocates a bigger block (twice the size), copies and frees the old one, so pointers and spans taken
/// earlier become invalid after <see cref="Add"/> or <see cref="Insert"/>. There are no exceptions: operations that
/// cannot be done (out of memory, index out of range) return false or do nothing, and reading an index out of range
/// gives the zero value. Call <see cref="Destroy"/> to give the memory back.
/// </summary>
public unsafe struct Vec<T> where T : unmanaged
{
    private T* _items;
    private int _count;
    private int _capacity;
    private bool _chip;

    /// <summary>A list with room for <paramref name="capacity"/> items. Check <see cref="Capacity"/> for 0 if it matters.</summary>
    public Vec(int capacity, bool chip)
    {
        _count = 0;
        _chip = chip;
        _items = null;
        _capacity = 0;
        if (capacity > 0) Reserve(capacity);
    }

    public int Count { get { return _count; } }

    public int Capacity { get { return _capacity; } }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count) return default(T);
            return _items[index];
        }
        set
        {
            if (index >= 0 && index < _count) _items[index] = value;
        }
    }

    /// <summary>Makes room for at least <paramref name="capacity"/> items. False when memory ran out (the list is unchanged).</summary>
    public bool Reserve(int capacity)
    {
        if (capacity <= _capacity) return true;
        uint size = (uint)capacity * (uint)sizeof(T);
        T* bigger = (T*)(_chip ? Mem.AllocChip(size) : Mem.AllocFast(size));
        if (bigger == null) return false;
        for (int i = 0; i < _count; i++) bigger[i] = _items[i];
        if (_items != null) Mem.Free((byte*)_items);
        _items = bigger;
        _capacity = capacity;
        return true;
    }

    /// <summary>Appends an item. False when memory ran out.</summary>
    public bool Add(T item)
    {
        if (_count == _capacity && !Reserve(_capacity == 0 ? 4 : _capacity * 2)) return false;
        _items[_count] = item;
        _count++;
        return true;
    }

    /// <summary>Inserts before <paramref name="index"/> (0..Count). False when the index is out of range or memory ran out.</summary>
    public bool Insert(int index, T item)
    {
        if (index < 0 || index > _count) return false;
        if (_count == _capacity && !Reserve(_capacity == 0 ? 4 : _capacity * 2)) return false;
        for (int i = _count; i > index; i--) _items[i] = _items[i - 1];
        _items[index] = item;
        _count++;
        return true;
    }

    /// <summary>Removes the item at <paramref name="index"/>, moving the later ones down. False when out of range.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _count) return false;
        for (int i = index; i < _count - 1; i++) _items[i] = _items[i + 1];
        _count--;
        return true;
    }

    /// <summary>Removes the item at <paramref name="index"/> by moving the last item into its place (order is not kept).</summary>
    public bool SwapRemoveAt(int index)
    {
        if (index < 0 || index >= _count) return false;
        _items[index] = _items[_count - 1];
        _count--;
        return true;
    }

    /// <summary>Forgets all items; keeps the memory.</summary>
    public void Clear() { _count = 0; }

    /// <summary>The items as a span. Invalid after the list grows or is destroyed.</summary>
    public Span<T> AsSpan()
    {
        return new Span<T>(_items, _count);
    }

    /// <summary>Gives the memory back and leaves an empty list.</summary>
    public void Destroy()
    {
        if (_items != null) Mem.Free((byte*)_items);
        _items = null;
        _count = 0;
        _capacity = 0;
    }
}
