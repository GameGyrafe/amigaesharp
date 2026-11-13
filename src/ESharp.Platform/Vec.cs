using System;

namespace ESharp.Platform;

/// <summary>
/// A growable list of <typeparamref name="T"/> in one block of memory (Fast RAM unless <c>chip</c> is set).
/// Growing allocates a bigger block (twice the size), copies and frees the old one, so pointers and spans taken
/// earlier become invalid after <see cref="Add"/> or <see cref="Insert"/>. There are no exceptions: operations that
/// cannot be done (out of memory, index out of range) return false or do nothing, and reading an index out of range
/// gives the zero value. Call <see cref="Destroy"/> to give the memory back.
/// <para>
/// <typeparamref name="T"/> may be a value (numbers, structs) or a class: a <c>Vec&lt;Player&gt;</c> holds references
/// (pointers in C). The list does not own the objects: <see cref="Destroy"/> frees the list's block, not the objects, so free
/// them first (with <c>Heap.Free</c>) if nothing else keeps them. On the dotnet host a list of references keeps them in a managed
/// array, so the garbage collector sees them; values use the same fake memory as on the Amiga.
/// </para>
/// </summary>
#pragma warning disable CS8500 // a pointer to T where T may be a class: in C it is a pointer to the object pointers
public unsafe struct Vec<T>
{
    private T* _items;
    private int _count;
    private int _capacity;
    private bool _chip;
#if HOST
    private T[]? _refs;   // the host's storage for class references (null for value types and before the first item)
    private static readonly bool HoldsRefs = System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#endif

    /// <summary>A list with room for <paramref name="capacity"/> items. Check <see cref="Capacity"/> for 0 if it matters.</summary>
    public Vec(int capacity, bool chip)
    {
        _count = 0;
        _chip = chip;
        _items = null;
        _capacity = 0;
#if HOST
        _refs = null;
#endif
        if (capacity > 0) Reserve(capacity);
    }

    public int Count { get { return _count; } }

    public int Capacity { get { return _capacity; } }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count) return default(T)!;
#if HOST
            if (HoldsRefs) return _refs![index];
#endif
            return _items[index];
        }
        set
        {
            if (index < 0 || index >= _count) return;
#if HOST
            if (HoldsRefs) { _refs![index] = value; return; }
#endif
            _items[index] = value;
        }
    }

    /// <summary>Makes room for at least <paramref name="capacity"/> items. False when memory ran out (the list is unchanged).</summary>
    public bool Reserve(int capacity)
    {
        if (capacity <= _capacity) return true;
#if HOST
        if (HoldsRefs)
        {
            Array.Resize(ref _refs, capacity);
            _capacity = capacity;
            return true;
        }
#endif
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
#if HOST
        if (HoldsRefs) { _refs![_count++] = item; return true; }
#endif
        _items[_count] = item;
        _count++;
        return true;
    }

    /// <summary>Inserts before <paramref name="index"/> (0..Count). False when the index is out of range or memory ran out.</summary>
    public bool Insert(int index, T item)
    {
        if (index < 0 || index > _count) return false;
        if (_count == _capacity && !Reserve(_capacity == 0 ? 4 : _capacity * 2)) return false;
#if HOST
        if (HoldsRefs)
        {
            Array.Copy(_refs!, index, _refs!, index + 1, _count - index);
            _refs![index] = item;
            _count++;
            return true;
        }
#endif
        for (int i = _count; i > index; i--) _items[i] = _items[i - 1];
        _items[index] = item;
        _count++;
        return true;
    }

    /// <summary>Removes the item at <paramref name="index"/>, moving the later ones down. False when out of range.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _count) return false;
#if HOST
        if (HoldsRefs)
        {
            Array.Copy(_refs!, index + 1, _refs!, index, _count - index - 1);
            _refs![--_count] = default(T)!;
            return true;
        }
#endif
        for (int i = index; i < _count - 1; i++) _items[i] = _items[i + 1];
        _count--;
        return true;
    }

    /// <summary>Removes the item at <paramref name="index"/> by moving the last item into its place (order is not kept).</summary>
    public bool SwapRemoveAt(int index)
    {
        if (index < 0 || index >= _count) return false;
#if HOST
        if (HoldsRefs)
        {
            _refs![index] = _refs![_count - 1];
            _refs![--_count] = default(T)!;
            return true;
        }
#endif
        _items[index] = _items[_count - 1];
        _count--;
        return true;
    }

    /// <summary>The index of the first item equal to <paramref name="item"/> (the same object for a class), or -1.</summary>
    public int IndexOf(T item)
    {
#if HOST
        if (HoldsRefs)
        {
            for (int k = 0; k < _count; k++)
                if (ReferenceEquals(_refs![k], item)) return k;
            return -1;
        }
#endif
        for (int i = 0; i < _count; i++)
            if (Same(_items[i], item)) return i;
        return -1;
    }

    /// <summary>Removes the first item equal to <paramref name="item"/> (see <see cref="IndexOf"/>), keeping the order. False if there is none.</summary>
    public bool Remove(T item)
    {
        return RemoveAt(IndexOf(item));
    }

    /// <summary>Forgets all items; keeps the memory.</summary>
    public void Clear()
    {
#if HOST
        if (HoldsRefs && _refs != null) Array.Clear(_refs);
#endif
        _count = 0;
    }

    /// <summary>The items as a span. Invalid after the list grows or is destroyed.</summary>
    public Span<T> AsSpan()
    {
#if HOST
        if (HoldsRefs) return _refs == null ? Span<T>.Empty : new Span<T>(_refs, 0, _count);
#endif
        return new Span<T>(_items, _count);
    }

    /// <summary>
    /// Lets <c>foreach (T x in list)</c> walk the items in order with no allocation. It walks the items as they were
    /// when the loop started: do not add, insert or remove inside the loop.
    /// </summary>
    public VecEnumerator<T> GetEnumerator()
    {
#if HOST
        if (HoldsRefs) return new VecEnumerator<T>(_refs, _count);
#endif
        return new VecEnumerator<T>(_items, _count);
    }

    /// <summary>Gives the memory back and leaves an empty list. Objects a list of class references points to are not freed.</summary>
    public void Destroy()
    {
#if HOST
        _refs = null;
#endif
        if (_items != null) Mem.Free((byte*)_items);
        _items = null;
        _count = 0;
        _capacity = 0;
    }

    // Equality for IndexOf: the same object for a class, the same bytes for a value.
    private static bool Same(T a, T b)
    {
        byte* pa = (byte*)&a;
        byte* pb = (byte*)&b;
        for (int i = 0; i < sizeof(T); i++)
            if (pa[i] != pb[i]) return false;
        return true;
    }
}

/// <summary>The enumerator behind <c>foreach</c> over a <see cref="Vec{T}"/>: a plain struct, so the loop allocates nothing.</summary>
public unsafe struct VecEnumerator<T>
{
    private T* _items;
    private int _count;
    private int _index;
#if HOST
    private T[]? _refs;

    public VecEnumerator(T[]? refs, int count)
    {
        _items = null;
        _refs = refs;
        _count = count;
        _index = -1;
    }
#endif

    public VecEnumerator(T* items, int count)
    {
        _items = items;
        _count = count;
        _index = -1;
#if HOST
        _refs = null;
#endif
    }

    public bool MoveNext()
    {
        _index++;
        return _index < _count;
    }

    public T Current
    {
        get
        {
#if HOST
            if (_refs != null) return _refs[_index];
#endif
            return _items[_index];
        }
    }
}
#pragma warning restore CS8500
