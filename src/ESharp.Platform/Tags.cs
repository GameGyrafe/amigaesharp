namespace ESharp.Platform;

/// <summary>
/// Writes an AmigaOS tag list (pairs of pointer-sized tag and value, ended by TAG_DONE) into memory the caller
/// owns, so code reads as a list of settings instead of hand-numbered array slots:
/// <code>
/// nuint[] buf = new nuint[16];
/// fixed (nuint* p = buf)
/// {
///     var t = new TagWriter(p, 16);
///     t.Add(Intuition.SA_Width, 320);
///     t.Add(Intuition.SA_Quiet, true);
///     if (t.End()) screen = Intuition.OpenScreenTagList(null, p);
/// }
/// </code>
/// A list that does not fit is never passed on half-written: <see cref="End"/> returns false and the list holds
/// only TAG_DONE.
/// </summary>
public unsafe struct TagWriter
{
    private nuint* _p;
    private int _capacity;   // in nuint slots
    private int _n;
    private bool _overflow;

    public TagWriter(nuint* buffer, int slots)
    {
        _p = buffer;
        _capacity = slots;
        _n = 0;
        _overflow = false;
    }

    /// <summary>Tags written so far, not counting TAG_DONE.</summary>
    public int Count => _n / 2;

    public void Add(uint tag, nuint value)
    {
        if (_n + 3 > _capacity) { _overflow = true; return; }   // room for this pair and TAG_DONE
        _p[_n] = tag;
        _p[_n + 1] = value;
        _n += 2;
    }

    public void Add(uint tag, int value) { Add(tag, (nuint)(nint)value); }

    public void Add(uint tag, uint value) { Add(tag, (nuint)value); }

    public void Add(uint tag, bool value) { Add(tag, value ? (nuint)1 : (nuint)0); }

    public void Add(uint tag, void* value) { Add(tag, (nuint)value); }

    /// <summary>Ends the list with TAG_DONE. False (and an empty list) if any Add did not fit.</summary>
    public bool End()
    {
        if (_capacity < 1) return false;
        if (_overflow) { _p[0] = 0; return false; }
        _p[_n] = 0;
        return true;
    }
}
