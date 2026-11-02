using System;

namespace ESharp.Platform;

/// <summary>
/// A hash table from <typeparamref name="TKey"/> to <typeparamref name="TValue"/> in raw memory (open addressing,
/// linear probing, grows at 3/4 full). Keys are compared and hashed by their bytes, so use integers, characters,
/// enums or structs without padding as keys; for string keys use <see cref="StringMap{TValue}"/>. There are no
/// exceptions: operations that cannot be done (duplicate key on <see cref="Add"/>, out of memory) return false and a
/// missing key reads as the zero value. Enumerate with <c>for (int i = d.First(); i >= 0; i = d.Next(i))</c> (order is
/// unspecified; do not add or remove while enumerating). Call <see cref="Destroy"/> to give the memory back.
/// </summary>
public unsafe struct Dictionary<TKey, TValue> where TKey : unmanaged where TValue : unmanaged
{
    private TKey* _keys;
    private TValue* _values;
    private byte* _state;      // 0 empty, 1 in use, 2 removed
    private int _count;
    private int _used;         // in use + removed
    private int _cap;          // a power of two, or 0

    public Dictionary(int capacity)
    {
        _keys = null; _values = null; _state = null; _count = 0; _used = 0; _cap = 0;
        if (capacity > 0) Rehash(capacity);
    }

    public int Count { get { return _count; } }

    public int Capacity { get { return _cap * 3 / 4; } }

    private int Find(TKey key)
    {
        if (_cap == 0) return -1;
        TKey k = key;
        byte* kp = (byte*)&k;
        int size = sizeof(TKey);
        int mask = _cap - 1;
        int i = (int)(Hashing.Bytes(kp, size) & (uint)mask);
        for (int n = 0; n < _cap; n++)
        {
            byte s = _state[i];
            if (s == 0) return -1;
            if (s == 1 && Hashing.SameBytes((byte*)&_keys[i], kp, size)) return i;
            i = (i + 1) & mask;
        }
        return -1;
    }

    /// <summary>Makes the table big enough for <paramref name="entries"/> entries (and drops removed slots). False when memory ran out (unchanged).</summary>
    public bool Rehash(int entries)
    {
        int cap = 8;
        while (cap * 3 / 4 < entries) cap *= 2;
        TKey* keys = (TKey*)Mem.AllocFast((uint)cap * (uint)sizeof(TKey));
        TValue* values = (TValue*)Mem.AllocFast((uint)cap * (uint)sizeof(TValue));
        byte* state = Mem.AllocFast((uint)cap);
        if (keys == null || values == null || state == null)
        {
            Mem.Free((byte*)keys); Mem.Free((byte*)values); Mem.Free(state);
            return false;
        }
        int mask = cap - 1;
        for (int j = 0; j < _cap; j++)
        {
            if (_state[j] != 1) continue;
            TKey k = _keys[j];
            int i = (int)(Hashing.Bytes((byte*)&k, sizeof(TKey)) & (uint)mask);
            while (state[i] != 0) i = (i + 1) & mask;
            state[i] = 1;
            keys[i] = k;
            values[i] = _values[j];
        }
        Mem.Free((byte*)_keys); Mem.Free((byte*)_values); Mem.Free(_state);
        _keys = keys; _values = values; _state = state;
        _cap = cap;
        _used = _count;
        return true;
    }

    /// <summary>Adds or replaces. False when memory ran out.</summary>
    public bool Set(TKey key, TValue value)
    {
        int slot = Find(key);
        if (slot >= 0) { _values[slot] = value; return true; }
        if ((_used + 1) * 4 > _cap * 3 && !Rehash((_count + 1) * 2)) return false;
        TKey k = key;
        int mask = _cap - 1;
        int i = (int)(Hashing.Bytes((byte*)&k, sizeof(TKey)) & (uint)mask);
        while (_state[i] == 1) i = (i + 1) & mask;
        if (_state[i] == 0) _used++;
        _state[i] = 1;
        _keys[i] = key;
        _values[i] = value;
        _count++;
        return true;
    }

    /// <summary>Adds a new entry. False when the key is already present (nothing changes) or memory ran out.</summary>
    public bool Add(TKey key, TValue value)
    {
        if (Find(key) >= 0) return false;
        return Set(key, value);
    }

    public bool ContainsKey(TKey key) { return Find(key) >= 0; }

    /// <summary>The value for the key; false (and the zero value) if absent.</summary>
    public bool TryGetValue(TKey key, out TValue value)
    {
        int slot = Find(key);
        if (slot < 0) { value = default(TValue); return false; }
        value = _values[slot];
        return true;
    }

    /// <summary>Reading a missing key gives the zero value; writing adds or replaces.</summary>
    public TValue this[TKey key]
    {
        get
        {
            int slot = Find(key);
            return slot < 0 ? default(TValue) : _values[slot];
        }
        set { Set(key, value); }
    }

    public bool Remove(TKey key)
    {
        int slot = Find(key);
        if (slot < 0) return false;
        _state[slot] = 2;
        _count--;
        return true;
    }

    /// <summary>Removes everything; keeps the memory.</summary>
    public void Clear()
    {
        for (int i = 0; i < _cap; i++) _state[i] = 0;
        _count = 0;
        _used = 0;
    }

    /// <summary>The first slot in use, or -1 when empty. See the class remarks for the loop.</summary>
    public int First() { return Next(-1); }

    /// <summary>The next slot in use after <paramref name="slot"/>, or -1 at the end.</summary>
    public int Next(int slot)
    {
        for (int i = slot + 1; i < _cap; i++) if (_state[i] == 1) return i;
        return -1;
    }

    public TKey KeyAt(int slot) { return _keys[slot]; }

    public TValue ValueAt(int slot) { return _values[slot]; }

    /// <summary>Changes the value stored in a slot found by enumerating.</summary>
    public void SetValueAt(int slot, TValue value) { _values[slot] = value; }

    /// <summary>Gives the memory back and leaves an empty table.</summary>
    public void Destroy()
    {
        Mem.Free((byte*)_keys); Mem.Free((byte*)_values); Mem.Free(_state);
        _keys = null; _values = null; _state = null;
        _count = 0; _used = 0; _cap = 0;
    }
}

/// <summary>A set of <typeparamref name="T"/> (compared by bytes, like the keys of <see cref="Dictionary{TKey,TValue}"/>).</summary>
public unsafe struct HashSet<T> where T : unmanaged
{
    private Dictionary<T, byte> _map;

    public int Count { get { return _map.Count; } }

    /// <summary>Adds the item. False when it was already present or memory ran out.</summary>
    public bool Add(T item) { return _map.Add(item, 1); }

    public bool Contains(T item) { return _map.ContainsKey(item); }

    public bool Remove(T item) { return _map.Remove(item); }

    public void Clear() { _map.Clear(); }

    public int First() { return _map.First(); }

    public int Next(int slot) { return _map.Next(slot); }

    public T ItemAt(int slot) { return _map.KeyAt(slot); }

    public void Destroy() { _map.Destroy(); }
}

/// <summary>
/// A hash table from strings to <typeparamref name="TValue"/> in raw memory (same behaviour as <see cref="Dictionary{TKey,TValue}"/>).
/// Keys are compared by their characters. The map keeps the key strings it is given and does not copy them, so they
/// must stay valid: string literals always are, a formatted (interpolated) string is not.
/// </summary>
public unsafe struct StringMap<TValue> where TValue : unmanaged
{
    private byte** _keys;   // pointers to the key strings (not copied: they must outlive the map)
    private TValue* _values;
    private byte* _state;      // 0 empty, 1 in use, 2 removed
    private int _count;
    private int _used;         // in use + removed
    private int _cap;          // a power of two, or 0

    public StringMap(int capacity)
    {
        _keys = null; _values = null; _state = null; _count = 0; _used = 0; _cap = 0;
        if (capacity > 0) Rehash(capacity);
    }

    public int Count { get { return _count; } }

    public int Capacity { get { return _cap * 3 / 4; } }

    private int Find(string key)
    {
        if (_cap == 0) return -1;
        int mask = _cap - 1;
        int i = (int)(Hashing.Text(key) & (uint)mask);
        for (int n = 0; n < _cap; n++)
        {
            byte s = _state[i];
            if (s == 0) return -1;
            if (s == 1 && Hashing.SameText(Cast.PtrToString(_keys[i]), key)) return i;
            i = (i + 1) & mask;
        }
        return -1;
    }

    /// <summary>Makes the table big enough for <paramref name="entries"/> entries (and drops removed slots). False when memory ran out (unchanged).</summary>
    public bool Rehash(int entries)
    {
        int cap = 8;
        while (cap * 3 / 4 < entries) cap *= 2;
        byte** keys = (byte**)Mem.AllocFast((uint)cap * (uint)sizeof(byte*));
        TValue* values = (TValue*)Mem.AllocFast((uint)cap * (uint)sizeof(TValue));
        byte* state = Mem.AllocFast((uint)cap);
        if (keys == null || values == null || state == null)
        {
            Mem.Free((byte*)keys); Mem.Free((byte*)values); Mem.Free(state);
            return false;
        }
        int mask = cap - 1;
        for (int j = 0; j < _cap; j++)
        {
            if (_state[j] != 1) continue;
            byte* k = _keys[j];
            int i = (int)(Hashing.Text(Cast.PtrToString(k)) & (uint)mask);
            while (state[i] != 0) i = (i + 1) & mask;
            state[i] = 1;
            keys[i] = k;
            values[i] = _values[j];
        }
        Mem.Free((byte*)_keys); Mem.Free((byte*)_values); Mem.Free(_state);
        _keys = keys; _values = values; _state = state;
        _cap = cap;
        _used = _count;
        return true;
    }

    /// <summary>Adds or replaces. False when memory ran out.</summary>
    public bool Set(string key, TValue value)
    {
        int slot = Find(key);
        if (slot >= 0) { _values[slot] = value; return true; }
        if ((_used + 1) * 4 > _cap * 3 && !Rehash((_count + 1) * 2)) return false;
        int mask = _cap - 1;
        int i = (int)(Hashing.Text(key) & (uint)mask);
        while (_state[i] == 1) i = (i + 1) & mask;
        if (_state[i] == 0) _used++;
        _state[i] = 1;
        _keys[i] = Cast.StringToPtr(key);
        _values[i] = value;
        _count++;
        return true;
    }

    /// <summary>Adds a new entry. False when the key is already present (nothing changes) or memory ran out.</summary>
    public bool Add(string key, TValue value)
    {
        if (Find(key) >= 0) return false;
        return Set(key, value);
    }

    public bool ContainsKey(string key) { return Find(key) >= 0; }

    /// <summary>The value for the key; false (and the zero value) if absent.</summary>
    public bool TryGetValue(string key, out TValue value)
    {
        int slot = Find(key);
        if (slot < 0) { value = default(TValue); return false; }
        value = _values[slot];
        return true;
    }

    /// <summary>Reading a missing key gives the zero value; writing adds or replaces.</summary>
    public TValue this[string key]
    {
        get
        {
            int slot = Find(key);
            return slot < 0 ? default(TValue) : _values[slot];
        }
        set { Set(key, value); }
    }

    public bool Remove(string key)
    {
        int slot = Find(key);
        if (slot < 0) return false;
        _state[slot] = 2;
        _count--;
        return true;
    }

    /// <summary>Removes everything; keeps the memory.</summary>
    public void Clear()
    {
        for (int i = 0; i < _cap; i++) _state[i] = 0;
        _count = 0;
        _used = 0;
    }

    /// <summary>The first slot in use, or -1 when empty. See the class remarks for the loop.</summary>
    public int First() { return Next(-1); }

    /// <summary>The next slot in use after <paramref name="slot"/>, or -1 at the end.</summary>
    public int Next(int slot)
    {
        for (int i = slot + 1; i < _cap; i++) if (_state[i] == 1) return i;
        return -1;
    }

    public string KeyAt(int slot) { return Cast.PtrToString(_keys[slot]); }

    public TValue ValueAt(int slot) { return _values[slot]; }

    /// <summary>Changes the value stored in a slot found by enumerating.</summary>
    public void SetValueAt(int slot, TValue value) { _values[slot] = value; }

    /// <summary>Gives the memory back and leaves an empty table.</summary>
    public void Destroy()
    {
        Mem.Free((byte*)_keys); Mem.Free((byte*)_values); Mem.Free(_state);
        _keys = null; _values = null; _state = null;
        _count = 0; _used = 0; _cap = 0;
    }
}

/// <summary>A set of strings (see <see cref="StringMap{TValue}"/> for how keys are kept).</summary>
public unsafe struct StringSet
{
    private StringMap<byte> _map;

    public int Count { get { return _map.Count; } }

    public bool Add(string item) { return _map.Add(item, 1); }

    public bool Contains(string item) { return _map.ContainsKey(item); }

    public bool Remove(string item) { return _map.Remove(item); }

    public void Clear() { _map.Clear(); }

    public int First() { return _map.First(); }

    public int Next(int slot) { return _map.Next(slot); }

    public string ItemAt(int slot) { return _map.KeyAt(slot); }

    public void Destroy() { _map.Destroy(); }
}
