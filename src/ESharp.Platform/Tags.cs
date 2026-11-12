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

/// <summary>
/// A tag list with its own fixed storage (15 tag/value pairs and TAG_DONE), so building one needs no array, no
/// <c>fixed</c> and no capacity number. It is a plain struct kept in a local variable:
/// <code>
/// TagList t = new TagList();
/// t.Add(Intuition.SA_Width, 320);
/// t.Add(Intuition.SA_Quiet, true);
/// nuint* list = t.End();                       // null if more than 15 pairs were added
/// if (list != null) screen = Intuition.OpenScreenTagList(null, list);
/// </code>
/// The pointer is only valid while the variable lives and is not copied: pass it to the call at once.
/// Same <c>Add</c> overloads and TAG_DONE rule as <see cref="TagWriter"/>.
/// </summary>
public unsafe struct TagList
{
    private nuint _s0, _s1, _s2, _s3, _s4, _s5, _s6, _s7, _s8, _s9, _s10, _s11, _s12, _s13, _s14, _s15, _s16, _s17, _s18, _s19, _s20, _s21, _s22, _s23, _s24, _s25, _s26, _s27, _s28, _s29, _s30, _s31;
    private int _n;
    private bool _overflow;

    /// <summary>Most pairs a list can hold.</summary>
    public const int MaxPairs = 15;

    /// <summary>Tags added so far, not counting TAG_DONE.</summary>
    public int Count => _n / 2;

    public void Add(uint tag, nuint value)
    {
        if (_n + 3 > 32) { _overflow = true; return; }   // room for this pair and TAG_DONE
        fixed (nuint* p = &_s0)
        {
            p[_n] = tag;
            p[_n + 1] = value;
        }
        _n += 2;
    }

    public void Add(uint tag, int value) { Add(tag, (nuint)(nint)value); }

    public void Add(uint tag, uint value) { Add(tag, (nuint)value); }

    public void Add(uint tag, bool value) { Add(tag, value ? (nuint)1 : (nuint)0); }

    public void Add(uint tag, void* value) { Add(tag, (nuint)value); }

    /// <summary>Ends the list with TAG_DONE and returns it, or null if an Add did not fit.</summary>
    public nuint* End()
    {
        if (_overflow) return null;
        fixed (nuint* p = &_s0)
        {
            p[_n] = 0;
            return p;
        }
    }
}
