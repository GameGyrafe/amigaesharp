// Amiga-only (BOOPSI, hooks): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
using System;

namespace ESharp.Platform;

// ------------------------------------------------------------------ typed attributes

/// <summary>A BOOPSI attribute whose value is an integer (a position, a size, a count).</summary>
public struct IntAttr
{
    public uint Tag;
    public IntAttr(uint tag) { Tag = tag; }
}

/// <summary>A BOOPSI attribute whose value is true or false.</summary>
public struct BoolAttr
{
    public uint Tag;
    public BoolAttr(uint tag) { Tag = tag; }
}

/// <summary>A BOOPSI attribute whose value is a string. The object keeps a copy of what is set through <see cref="BoopsiObject"/>.</summary>
public struct StrAttr
{
    public uint Tag;
    public StrAttr(uint tag) { Tag = tag; }
}

/// <summary>A BOOPSI attribute whose value is a pointer (a DrawInfo, a target object, a struct Hook).</summary>
public struct PtrAttr
{
    public uint Tag;
    public PtrAttr(uint tag) { Tag = tag; }
}

/// <summary>A BOOPSI attribute whose value is another BOOPSI object (an image of a gadget, a notification target).</summary>
public struct ObjAttr
{
    public uint Tag;
    public ObjAttr(uint tag) { Tag = tag; }
}

/// <summary>A BOOPSI attribute whose value is a hook (<see cref="Hook"/>).</summary>
public struct HookAttr
{
    public uint Tag;
    public HookAttr(uint tag) { Tag = tag; }
}

/// <summary>Tag values of the ROM classes: measured from the system headers (Layout.g.cs) on the Amiga, made-up distinct numbers on the host.</summary>
internal static class BTag
{
#if AMIGA
    public const uint GaLeft = Layout.GaLeft, GaTop = Layout.GaTop, GaWidth = Layout.GaWidth, GaHeight = Layout.GaHeight;
    public const uint GaText = Layout.GaText, GaImage = Layout.GaImage, GaId = Layout.GaId, GaUserData = Layout.GaUserData;
    public const uint GaDisabled = Layout.GaDisabled, GaSelected = Layout.GaSelected, GaImmediate = Layout.GaImmediate;
    public const uint GaRelVerify = Layout.GaRelVerify, GaDrawInfo = Layout.GaDrawInfo;
    public const uint PgaFreedom = Layout.PgaFreedom, PgaTop = Layout.PgaTop, PgaVisible = Layout.PgaVisible, PgaTotal = Layout.PgaTotal;
    public const uint PgaNewLook = Layout.PgaNewLook, PgaBorderless = Layout.PgaBorderless, FreeHoriz = Layout.FreeHoriz, FreeVert = Layout.FreeVert;
    public const uint StringaMaxChars = Layout.StringaMaxChars, StringaTextVal = Layout.StringaTextVal, StringaLongVal = Layout.StringaLongVal;
    public const uint StringaEditHook = Layout.StringaEditHook;
    public const uint IcaTarget = Layout.IcaTarget, IcaMap = Layout.IcaMap;
    public const uint IaLeft = Layout.IaLeft, IaTop = Layout.IaTop, IaWidth = Layout.IaWidth, IaHeight = Layout.IaHeight;
    public const uint IaRecessed = Layout.IaRecessed, IaFrameType = Layout.IaFrameType;
    public const uint FrameDefault = Layout.FrameDefault, FrameButton = Layout.FrameButton, FrameRidge = Layout.FrameRidge;
    public const uint OmSet = Layout.OmSet, OmGet = Layout.OmGet, SghKey = Layout.SghKey, SghClick = Layout.SghClick;
#else
    public const uint GaLeft = 0x7001, GaTop = 0x7002, GaWidth = 0x7003, GaHeight = 0x7004;
    public const uint GaText = 0x7005, GaImage = 0x7006, GaId = 0x7007, GaUserData = 0x7008;
    public const uint GaDisabled = 0x7009, GaSelected = 0x700a, GaImmediate = 0x700b;
    public const uint GaRelVerify = 0x700c, GaDrawInfo = 0x700d;
    public const uint PgaFreedom = 0x7101, PgaTop = 0x7102, PgaVisible = 0x7103, PgaTotal = 0x7104;
    public const uint PgaNewLook = 0x7105, PgaBorderless = 0x7106, FreeHoriz = 2, FreeVert = 4;
    public const uint StringaMaxChars = 0x7201, StringaTextVal = 0x7202, StringaLongVal = 0x7203;
    public const uint StringaEditHook = 0x7204;
    public const uint IcaTarget = 0x7301, IcaMap = 0x7302;
    public const uint IaLeft = 0x7401, IaTop = 0x7402, IaWidth = 0x7403, IaHeight = 0x7404;
    public const uint IaRecessed = 0x7405, IaFrameType = 0x7406;
    public const uint FrameDefault = 0, FrameButton = 1, FrameRidge = 2;
    public const uint OmSet = 0x103, OmGet = 0x104, SghKey = 1, SghClick = 2;
#endif
}

/// <summary>Attributes of gadgetclass (every BOOPSI gadget). Positions are window coordinates, borders included (see <see cref="BoopsiWindow.InnerLeft"/>).</summary>
public static class Ga
{
    public static IntAttr Left => new IntAttr(BTag.GaLeft);
    public static IntAttr Top => new IntAttr(BTag.GaTop);
    public static IntAttr Width => new IntAttr(BTag.GaWidth);
    public static IntAttr Height => new IntAttr(BTag.GaHeight);
    /// <summary>A label string (frbuttonclass draws it with the screen's DrawInfo).</summary>
    public static StrAttr Text => new StrAttr(BTag.GaText);
    /// <summary>The image the gadget draws (a frameiclass object for a framed button). Not disposed by the gadget: <see cref="BoopsiObject.Keep(BoopsiObject)"/> it.</summary>
    public static ObjAttr Image => new ObjAttr(BTag.GaImage);
    /// <summary>The gadget's number, sent along with its notifications; Update events are matched by it, so it must differ between the gadgets of a
    /// window. A window gives a gadget without one a free number from 1000 up, and refuses a created gadget whose number is taken.</summary>
    public static IntAttr Id => new IntAttr(BTag.GaId);
    public static IntAttr UserData => new IntAttr(BTag.GaUserData);
    public static BoolAttr Disabled => new BoolAttr(BTag.GaDisabled);
    public static BoolAttr Selected => new BoolAttr(BTag.GaSelected);
    public static BoolAttr Immediate => new BoolAttr(BTag.GaImmediate);
    /// <summary>Send a gadget-up event when the mouse button is released over the gadget.</summary>
    public static BoolAttr RelVerify => new BoolAttr(BTag.GaRelVerify);
    public static PtrAttr DrawInfo => new PtrAttr(BTag.GaDrawInfo);
}

/// <summary>Attributes of propgclass (a proportional slider): <see cref="Top"/> is the knob's position in 0 .. <see cref="Total"/> - <see cref="Visible"/>.</summary>
public static class Pga
{
    public static IntAttr Freedom => new IntAttr(BTag.PgaFreedom);
    public static IntAttr Top => new IntAttr(BTag.PgaTop);
    public static IntAttr Visible => new IntAttr(BTag.PgaVisible);
    public static IntAttr Total => new IntAttr(BTag.PgaTotal);
    public static BoolAttr NewLook => new BoolAttr(BTag.PgaNewLook);
    public static BoolAttr Borderless => new BoolAttr(BTag.PgaBorderless);
    /// <summary><see cref="Freedom"/> value: moves left and right.</summary>
    public static int FreeHoriz => (int)BTag.FreeHoriz;
    /// <summary><see cref="Freedom"/> value: moves up and down.</summary>
    public static int FreeVert => (int)BTag.FreeVert;
}

/// <summary>Attributes of strgclass (a text entry gadget).</summary>
public static class Stringa
{
    public static IntAttr MaxChars => new IntAttr(BTag.StringaMaxChars);
    /// <summary>The text (copied into the gadget's own buffer).</summary>
    public static StrAttr TextVal => new StrAttr(BTag.StringaTextVal);
    /// <summary>The number in an integer gadget (one created with this attribute).</summary>
    public static IntAttr LongVal => new IntAttr(BTag.StringaLongVal);
    /// <summary>A hook called for each edit action (on input.device's task); its message's first long is the command (<see cref="Key"/>, <see cref="Click"/>).</summary>
    public static HookAttr EditHook => new HookAttr(BTag.StringaEditHook);
    /// <summary>Edit hook command: a key was handled.</summary>
    public static uint Key => BTag.SghKey;
    /// <summary>Edit hook command: the gadget was clicked.</summary>
    public static uint Click => BTag.SghClick;
}

/// <summary>Attributes of icclass and the notification of gadgets: where attribute changes go.</summary>
public static unsafe class Ica
{
    /// <summary>The object that receives the changes, or <see cref="Idcmp"/> for the window's event loop (an Update event, matched to the gadget
    /// by its <see cref="Ga.Id"/>, which the window makes unique).</summary>
    public static PtrAttr Target => new PtrAttr(BTag.IcaTarget);
    public static PtrAttr Map => new PtrAttr(BTag.IcaMap);
    /// <summary><see cref="Target"/> value: send changes to the window as IDCMP_IDCMPUPDATE (all bits set, pointer sized).</summary>
    public static void* Idcmp => (void*)(nint)(-1);
}

/// <summary>Attributes of imageclass (frameiclass and the other images).</summary>
public static class Ia
{
    public static IntAttr Left => new IntAttr(BTag.IaLeft);
    public static IntAttr Top => new IntAttr(BTag.IaTop);
    public static IntAttr Width => new IntAttr(BTag.IaWidth);
    public static IntAttr Height => new IntAttr(BTag.IaHeight);
    public static BoolAttr Recessed => new BoolAttr(BTag.IaRecessed);
    /// <summary>frameiclass: <see cref="FrameDefault"/>, <see cref="FrameButton"/> or <see cref="FrameRidge"/>.</summary>
    public static IntAttr FrameType => new IntAttr(BTag.IaFrameType);
    public static int FrameDefault => (int)BTag.FrameDefault;
    public static int FrameButton => (int)BTag.FrameButton;
    public static int FrameRidge => (int)BTag.FrameRidge;
}

/// <summary>Method numbers of rootclass for <see cref="BoopsiObject.DoMethod(uint, nuint, nuint)"/>.</summary>
public static class Om
{
    public static uint Set => BTag.OmSet;
    /// <summary>OM_GET: (attribute, address of a pointer-sized slot).</summary>
    public static uint Get => BTag.OmGet;
}

// ------------------------------------------------------------------ hooks

/// <summary>The C# side of a <see cref="Hook"/>: called with the object and the message the system passes; the result goes back to it.</summary>
public unsafe delegate nuint HookFunc(void* obj, void* msg);

/// <summary>
/// A system hook (<c>struct Hook</c>) that calls a C# delegate, also a capturing lambda:
/// <code>
/// int keys = 0;
/// Hook h = new Hook((obj, msg) => { keys++; return 0; });
/// ... Stringa.EditHook, h ...                         // or Utility.CallHookPkt(h.Address, obj, msg)
/// h.Dispose(); Heap.Free(ref h);                      // after everything that may call it is gone
/// </code>
/// The hook owns the delegate (the constructor's parameter is <c>[Owned]</c>): <see cref="Dispose"/> frees it and the struct.
/// The entry the system calls is a <see cref="HookEntryAttribute"/> method, so the calling convention of each target is taken
/// care of by the compiler (m68k registers, the MorphOS gate, plain C on AmigaOS 4 and AROS). A hook given to an object
/// must live as long as the object: hand it over with <see cref="BoopsiObject.Keep(Hook)"/> and it is disposed right after it.
/// <para>
/// The delegate may run on another task: an edit hook runs on input.device's task, which is a plain Task (not a Process) with a
/// small stack. Code that a hook runs must therefore: allocate nothing (no <c>new</c>, no string interpolation or concatenation, no
/// capturing lambda that would become a heap closure: the C heap is not safe from two tasks at once, and a failed allocation ends the
/// program from the wrong task); call no dos.library function (no <c>Dos.PutStr</c>, no files); call nothing that takes Intuition's
/// locks or waits (no <c>Set</c> on a gadget, no refresh, no window or requester); keep its stack use small (no LINQ sorts, no big
/// local arrays); and do nothing that can stop the program (an index out of range, <c>exit</c>). Reading and writing plain fields and
/// captured locals (make shared ones <c>volatile</c>) and doing arithmetic is fine. The same rules hold for any
/// <see cref="HookEntryAttribute"/> method the system calls from another task. Nothing checks them yet.
/// </para>
/// </summary>
public sealed unsafe class Hook
{
    private HookFunc? _func;
    internal Hook? NextKept;
    internal BoopsiObject? Owner;      // the object that keeps it (Keep), if any
#if AMIGA
    private byte* _hook;     // struct Hook in public memory; h_Data points back at this object
#endif

    public Hook([Owned] HookFunc func)
    {
        _func = func;
#if AMIGA
        _hook = Mem.AllocFast((uint)Layout.HookSize);
        if (_hook != null)
        {
            delegate*<void*, void*, void*, nuint> entry = &Dispatch;
            *(void**)(_hook + Layout.HookEntryOffset) = (void*)entry;
            *(void**)(_hook + Layout.HookData) = Cast.ObjectToPtr(this);
        }
#endif
    }

#if AMIGA
    /// <summary>The <c>struct Hook</c> to hand to the system (null if there was no memory for it, or after <see cref="Dispose"/>).</summary>
    public void* Address => _hook;

    /// <summary>False if there was no memory for the struct (or after <see cref="Dispose"/>): such a hook cannot be given to an object.</summary>
    public bool IsValid => _hook != null;

    /// <summary>Calls the hook the way the system does (utility.library CallHookPkt), so it goes through the target's entry; 0 if it is gone.</summary>
    public nuint Call(void* obj, void* msg)
    {
        if (_hook == null || !Utility.Open()) return 0;
        return Utility.CallHookPkt((void*)_hook, obj, msg);
    }

    // The entry for every Hook: h_Data says which object it belongs to.
    [HookEntry]
    private static nuint Dispatch(void* hook, void* obj, void* msg)
    {
        void* data = *(void**)((byte*)hook + Layout.HookData);
        if (data == null) return 0;
        Hook h = Cast.PtrToObject<Hook>(data);
        HookFunc? f = h._func;
        if (f == null) return 0;
        return f(obj, msg);
    }
#else
    /// <summary>Host: a made-up address (there is no system to call it).</summary>
    public void* Address => _func == null ? null : Cast.ObjectToPtr(this);

    public bool IsValid => _func != null;

    /// <summary>Host: calls the delegate directly.</summary>
    public nuint Call(void* obj, void* msg)
    {
        HookFunc? f = _func;
        return f == null ? 0 : f(obj, msg);
    }
#endif

    /// <summary>
    /// Frees the struct and the delegate. Nothing may call the hook afterwards; then free the object (<c>Heap.Free</c>). A hook that
    /// an object keeps is taken off that object's list first (so the object does not dispose it a second time).
    /// </summary>
    public void Dispose()
    {
        if (Owner != null) Owner.Unkeep(this);
#if AMIGA
        if (_hook != null)
        {
            *(void**)(_hook + Layout.HookData) = null;
            Mem.Free(_hook);
            _hook = null;
        }
#endif
        Heap.Free(ref _func);
    }
}

// ------------------------------------------------------------------ objects

#if AMIGA
/// <summary>DoMethodA, which is not a library vector (amiga.lib, a macro or the AmigaOS 4 interface): the emitter defines it per target.</summary>
internal static unsafe class BoopsiNative
{
    [Native] public static extern nuint es_domethod(void* obj, void* msg);

    /// <summary>Called at each Create: on AROS it finds out once, on the program's own task, where an object keeps its class (see es_domethod).</summary>
    [Native] public static extern void es_boopsi_prepare();
}
#endif

/// <summary>
/// A BOOPSI object of a public class (the ROM's <c>"buttongclass"</c>, <c>"frbuttonclass"</c>, <c>"propgclass"</c>,
/// <c>"strgclass"</c>, <c>"frameiclass"</c>, ...). Give it its starting attributes with <c>Init</c>, make it with
/// <see cref="Create"/>, then read and change attributes with <c>Get</c> and <c>Set</c>:
/// <code>
/// var prop = new BoopsiObject("propgclass");
/// prop.Init(Ga.Left, 20); prop.Init(Pga.Total, 100); prop.Init(Pga.Visible, 10);
/// if (!prop.Create()) ...
/// prop.Set(Pga.Top, 30); int top = prop.Get(Pga.Top);
/// </code>
/// Ownership: an object belongs to one owner. A gadget or image added to a <see cref="BoopsiWindow"/> belongs to the window, which
/// disposes and frees it when it closes. An object or hook handed to <see cref="Keep(BoopsiObject)"/> belongs to this object and is
/// disposed right after it (the image of a framed button, an edit hook). Anything else the program disposes itself:
/// <see cref="Dispose"/>, then <c>Heap.Free</c>. Strings set through <c>Init</c>/<c>Set</c> are copied and the copies live as long
/// as the object. <see cref="Live"/> counts the system objects made and not yet disposed, to check for leaks.
/// <para>
/// Events: when the object is a gadget in a <see cref="BoopsiWindow"/>, its events are raised on <see cref="Notify"/> (a C# event:
/// <c>+=</c> a lambda) by the window's <see cref="BoopsiWindow.Dispatch"/>. <see cref="Dispose"/> drops the handlers.
/// </para>
/// </summary>
public unsafe class BoopsiObject
{
    private byte* _className;
    private TagList _init;
    private byte* _strings;            // copies this object owns: a chain, each block starting with the next block's address
    private BoopsiObject? _keptFirst;  // owned objects, disposed after this one
    private Hook? _hooksFirst;         // owned hooks, disposed after this one
    internal BoopsiObject? NextKept;
    internal BoopsiObject? OwnerObject; // the object that keeps it (Keep), if any
    internal BoopsiObject? NextInWindow;
    internal BoopsiWindow? Window;     // the window it was added to
    internal bool IsImage;
    internal int GadgetId;             // the id it has, or will be created with (-1: none yet)
    internal bool Shown;               // in the open window (AddGList done)
    private bool _created;
    private bool _initFailed;          // an Init was refused: Create fails rather than make an object without it
    internal bool HasId;               // a Ga.Id was given
    private void* _handle;
    private static int _live;

    /// <summary>Raised by <see cref="BoopsiWindow.Dispatch"/> for each event of this gadget (released, or an attribute change).</summary>
    public event Action<BoopsiEvent>? Notify;

    public BoopsiObject(string publicClass)
    {
        _className = KeepString(publicClass, 0);
        GadgetId = -1;
        _init = new TagList();
    }

    /// <summary>The system object (<c>Object *</c>), null before <see cref="Create"/> or if it failed.</summary>
    public void* Address => _handle;

    /// <summary>True once <see cref="Create"/> has succeeded (and until <see cref="Dispose"/>).</summary>
    public bool IsCreated => _created;

    /// <summary>System objects made by <see cref="Create"/> and not yet disposed (all objects of the program).</summary>
    public static int Live => _live;

    // ---------------------------------------------------------- starting attributes

    /// <summary>
    /// A starting attribute (before <see cref="Create"/>; up to 13 per object: a window adds GA_DrawInfo and, if missing, GA_ID). False if it is too late or full; a refused starting
    /// attribute also makes <see cref="Create"/> fail, so an object is never made without an attribute the program asked for.
    /// </summary>
    public bool Init(IntAttr a, int value) { return InitRaw(a.Tag, (nuint)(nint)value); }

    public bool Init(BoolAttr a, bool value) { return InitRaw(a.Tag, value ? (nuint)1 : (nuint)0); }

    /// <summary>A string attribute: the object keeps its own copy.</summary>
    public bool Init(StrAttr a, string? value)
    {
        byte* copy = KeepString(value, a.Tag);
        if (value != null && copy == null) { if (!_created) _initFailed = true; return false; }
        return InitRaw(a.Tag, (nuint)copy);
    }

    public bool Init(PtrAttr a, void* value) { return InitRaw(a.Tag, (nuint)value); }

    /// <summary>An object attribute: <paramref name="value"/> must be created already. It stays owned by whoever owned it (see <see cref="Keep(BoopsiObject)"/>).</summary>
    public bool Init(ObjAttr a, BoopsiObject? value)
    {
        if (value != null && !value.IsCreated) { if (!_created) _initFailed = true; return false; }
        return InitRaw(a.Tag, value == null ? (nuint)0 : (nuint)value.Address);
    }

    /// <summary>A hook attribute: the hook must outlive the object (<see cref="Keep(Hook)"/> it). Refused (and Create fails) for a hook without memory.</summary>
    public bool Init(HookAttr a, Hook? value)
    {
        if (value != null && !value.IsValid) { if (!_created) _initFailed = true; return false; }
        return InitRaw(a.Tag, value == null ? (nuint)0 : (nuint)value.Address);
    }

    /// <summary>Any tag and value, for attributes without a typed name.</summary>
    public bool InitRaw(uint tag, nuint value) { return AddInit(tag, value, TagList.MaxPairs - 2); }

    // Two slots are kept for the window's own GA_DrawInfo and GA_ID (see BoopsiWindow.Add).
    internal bool AddInit(uint tag, nuint value, int max)
    {
        if (_created) return false;
        if (_init.Count >= max) { _initFailed = true; return false; }
        _init.Add(tag, value);
        if (tag == BTag.GaId) { HasId = true; GadgetId = (int)value; }
        return true;
    }

    /// <summary>Makes the system object from the class and the starting attributes. False if the class is missing, an attribute was refused, or memory ran out.</summary>
    public bool Create() { return CreateAll(); }

    // The MUI wrappers make a whole tree here (children first where the parent takes them as starting attributes).
    internal virtual bool CreateAll() { return CreateSelf(); }

    // A starting attribute could not be given: Create fails.
    internal void FailInit() { if (!_created) _initFailed = true; }

    internal bool CreateSelf()
    {
        if (_created || _initFailed) return false;
        nuint* tags = _init.End();
        if (tags == null) return false;
#if AMIGA
        BoopsiNative.es_boopsi_prepare();
        _handle = NewSystem(_className, tags);
        if (_handle == null) return false;
#else
        if (!HostKnowsClass(Cast.PtrToString(_className))) return false;
        _handle = Cast.ObjectToPtr(this);
        _hostCount = 0;
        for (int i = 0; tags[i] != 0; i += 2) HostStore((uint)tags[i], tags[i + 1]);
#endif
        _created = true;
        _live++;
        AfterCreate();
        return true;
    }

    // Hooks for the classes built on this one (the MUI wrappers): how the system object is made and disposed, what happens right
    // after Create, what else the object owns, and whether Dispose may run now.
#if AMIGA
    internal virtual void* NewSystem(byte* className, nuint* tags) { return Intuition.NewObjectA(null, className, (void*)tags); }

    internal virtual void DisposeSystem() { Intuition.DisposeObject(_handle); }
#else
    internal virtual bool HostKnowsClass(string name) { return HostKnows(name); }

    internal virtual void DisposeSystem() { }

    /// <summary>Host: an attribute was set (the MUI fake raises its notifications here).</summary>
    internal virtual void HostChanged(uint tag, nuint value) { }
#endif

    internal virtual void AfterCreate() { }

    internal virtual void DisposeOwned() { }

    internal virtual bool RefuseDispose() { return false; }

    // ---------------------------------------------------------- attributes of a live object

    /// <summary>Sets an attribute; a gadget in an open window redraws itself. False if the object is not created.</summary>
    public bool Set(IntAttr a, int value) { return SetRaw(a.Tag, (nuint)(nint)value); }

    public bool Set(BoolAttr a, bool value) { return SetRaw(a.Tag, value ? (nuint)1 : (nuint)0); }

    /// <summary>
    /// Sets a string attribute from a copy the object keeps. The copies made for earlier values of the same attribute are freed when the
    /// attribute reads back as the new copy (or as the object's own memory); otherwise they stay until <see cref="Dispose"/>.
    /// </summary>
    public bool Set(StrAttr a, string? value)
    {
        byte* fresh = KeepString(value, a.Tag);
        if (value != null && fresh == null) return false;
        if (!SetRaw(a.Tag, (nuint)fresh)) return false;
        // Free the copies of earlier values only when the object provably no longer points at them: the attribute reads back as the
        // new copy, or as memory of its own (a class that copies the text, such as strgclass). A class that ignores the new value
        // (an attribute it takes only at creation) or cannot report it keeps all copies until Dispose.
        nuint now = 0;
        if (TryGetRaw(a.Tag, out now) && ((byte*)now == fresh || !IsOurCopy((byte*)now))) FreeOlderStrings(a.Tag, fresh);
        return true;
    }

    public bool Set(PtrAttr a, void* value) { return SetRaw(a.Tag, (nuint)value); }

    public bool Set(ObjAttr a, BoopsiObject? value) { return SetRaw(a.Tag, value == null ? (nuint)0 : (nuint)value.Address); }

    public bool Set(HookAttr a, Hook? value)
    {
        if (value != null && !value.IsValid) return false;
        return SetRaw(a.Tag, value == null ? (nuint)0 : (nuint)value.Address);
    }

    /// <summary>Sets any tag (one tag list with one pair: SetGadgetAttrsA for a gadget in an open window, SetAttrsA otherwise).</summary>
    public bool SetRaw(uint tag, nuint value)
    {
        if (!_created) return false;
#if AMIGA
        TagList t = new TagList();
        t.Add(tag, value);
        nuint* list = t.End();
        void* win = Window != null && !IsImage ? Window.SystemWindow : null;
        if (win != null)
        {
            if (Intuition.SetGadgetAttrsA(_handle, win, null, (void*)list) != 0) Intuition.RefreshGList(_handle, win, null, 1);
        }
        else Intuition.SetAttrsA(_handle, (void*)list);
#else
        HostStore(tag, value);
        HostChanged(tag, value);
#endif
        return true;
    }

    /// <summary>Reads an integer attribute; 0 if the class does not know it (see <see cref="TryGetRaw"/>).</summary>
    public int Get(IntAttr a) { nuint v = 0; TryGetRaw(a.Tag, out v); return (int)(nint)v; }

    public bool Get(BoolAttr a) { nuint v = 0; TryGetRaw(a.Tag, out v); return v != 0; }

    /// <summary>Reads a string attribute: the object's own text (valid until it changes), or null.</summary>
    public string? Get(StrAttr a)
    {
        nuint v = 0;
        if (!TryGetRaw(a.Tag, out v) || v == 0) return null;
        return Cast.PtrToString((byte*)v);
    }

    public void* Get(PtrAttr a) { nuint v = 0; TryGetRaw(a.Tag, out v); return (void*)v; }

    /// <summary>Reads any attribute (GetAttr); false if the object is not created or its class does not know the attribute.</summary>
    public bool TryGetRaw(uint tag, out nuint value)
    {
        value = 0;
        if (!_created) return false;
#if AMIGA
        nuint slot = 0;
        if (Intuition.GetAttr(tag, _handle, (void*)&slot) == 0) return false;
        value = slot;
        return true;
#else
        for (int i = 0; i < _hostCount; i++)
        {
            if (_hostTags[i] == tag) { value = _hostValues[i]; return true; }
        }
        return false;
#endif
    }

    /// <summary>Sends a method to the object (DoMethodA with the message <c>{ id, a, b }</c>); the method's result. 0 if the object is not created.</summary>
    public nuint DoMethod(uint methodId, nuint a, nuint b)
    {
        if (!_created) return 0;
        nuint[] msg = new nuint[3];
        fixed (nuint* m = msg)
        {
            m[0] = methodId;
            m[1] = a;
            m[2] = b;
            return DoMethodMsg(m);
        }
    }

    /// <summary>Sends a message the program built (its first pointer-sized slot is the method number; the fields are pointer sized on AROS x86-64).</summary>
    public nuint DoMethodMsg(void* msg)
    {
        if (!_created) return 0;
#if AMIGA
        return BoopsiNative.es_domethod(_handle, msg);
#else
        nuint* m = (nuint*)msg;
        if ((uint)m[0] == BTag.OmGet)
        {
            nuint v = 0;
            if (!TryGetRaw((uint)m[1], out v)) return 0;
            *(nuint*)m[2] = v;
            return 1;
        }
        return 0;
#endif
    }

    // ---------------------------------------------------------- ownership

    /// <summary>
    /// This object owns <paramref name="child"/> from now on: it is disposed and freed right after this object. False if the child has
    /// an owner already (another object or a window), is this object, or owns this object (directly or further up).
    /// </summary>
    public bool Keep(BoopsiObject child)
    {
        if (child.OwnerObject != null || child.Window != null) return false;
        for (BoopsiObject? p = this; p != null; p = p.OwnerObject)
            if (p == child) return false;
        child.OwnerObject = this;
        child.NextKept = _keptFirst;
        _keptFirst = child;
        return true;
    }

    /// <summary>This object owns <paramref name="hook"/> from now on: it is disposed and freed right after this object. False if it has an owner already.</summary>
    public bool Keep(Hook hook)
    {
        if (hook.Owner != null) return false;
        hook.Owner = this;
        hook.NextKept = _hooksFirst;
        _hooksFirst = hook;
        return true;
    }

    // A kept object that is disposed by the program is taken off this object's list.
    internal void Unkeep(BoopsiObject child)
    {
        BoopsiObject? prev = null;
        for (BoopsiObject? p = _keptFirst; p != null; p = p.NextKept)
        {
            if (p == child)
            {
                if (prev == null) _keptFirst = p.NextKept; else prev.NextKept = p.NextKept;
                break;
            }
            prev = p;
        }
        child.NextKept = null;
        child.OwnerObject = null;
    }

    internal void Unkeep(Hook hook)
    {
        Hook? prev = null;
        for (Hook? p = _hooksFirst; p != null; p = p.NextKept)
        {
            if (p == hook)
            {
                if (prev == null) _hooksFirst = p.NextKept; else prev.NextKept = p.NextKept;
                break;
            }
            prev = p;
        }
        hook.NextKept = null;
        hook.Owner = null;
    }

    /// <summary>True when a window or another object owns this object (it is then disposed and freed by that owner).</summary>
    public bool HasOwner => OwnerObject != null || Window != null;

    /// <summary>
    /// Disposes the system object (a gadget still in an open window is taken out of it first), then what it keeps, its string copies
    /// and the <see cref="Notify"/> handlers. Free the object itself afterwards (<c>Heap.Free</c>), unless a window or another object
    /// owns it: they do both, and a program that disposes such an object itself takes it away from its owner first (it must then
    /// free it). Never free a gadget of a window from inside one of that window's event handlers: close the window instead.
    /// </summary>
    public void Dispose()
    {
        if (RefuseDispose()) return;
        if (Window != null) Window.Detach(this);
        if (OwnerObject != null) OwnerObject.Unkeep(this);
        if (_created)
        {
            DisposeSystem();
            _live--;
        }
        _created = false;
        _handle = null;
        // Take the lists first, so nothing reached from them can come back to this object half disposed.
        BoopsiObject? k = _keptFirst;
        _keptFirst = null;
        Hook? h = _hooksFirst;
        _hooksFirst = null;
        while (k != null)
        {
            BoopsiObject? next = k.NextKept;
            k.NextKept = null;
            k.OwnerObject = null;
            k.Dispose();
            Heap.Free(ref k);
            k = next;
        }
        while (h != null)
        {
            Hook? next = h.NextKept;
            h.NextKept = null;
            h.Owner = null;
            h.Dispose();
            Heap.Free(ref h);
            h = next;
        }
        DisposeOwned();
        FreeStrings();
        Heap.Free(ref Notify);
    }

    internal void Raise(BoopsiEvent e) { Notify?.Invoke(e); }

    // ---------------------------------------------------------- strings

    // A copy the object owns. Each block: the next block's address, the attribute's tag (0: not an attribute), then the characters.
    internal byte* KeepString(string? s, uint tag)
    {
        if (s == null) return null;
#if AMIGA
        byte* src = Cast.StringToPtr(s);
        int n = 0;
        while (src[n] != 0) n++;
        byte* block = Mem.AllocFast((uint)(2 * sizeof(nuint) + n + 1));
        if (block == null) return null;
        *(byte**)block = _strings;
        *(nuint*)(block + sizeof(nuint)) = tag;
        _strings = block;
        byte* d = block + 2 * sizeof(nuint);
        for (int i = 0; i <= n; i++) d[i] = src[i];
        return d;
#else
        return Cast.StringToPtr(s);   // the host keeps one native copy per distinct string
#endif
    }

    internal bool IsOurCopy(byte* p)
    {
#if AMIGA
        for (byte* b = _strings; b != null; b = *(byte**)b)
            if (b + 2 * sizeof(nuint) == p) return true;
#endif
        return false;
    }

    // After a Set: the copies of earlier values of the same attribute are no longer pointed at by the object.
    private void FreeOlderStrings(uint tag, byte* keep)
    {
#if AMIGA
        byte* prev = null;
        byte* b = _strings;
        while (b != null)
        {
            byte* next = *(byte**)b;
            if (*(nuint*)(b + sizeof(nuint)) == tag && b + 2 * sizeof(nuint) != keep)
            {
                if (prev == null) _strings = next; else *(byte**)prev = next;
                Mem.Free(b);
            }
            else prev = b;
            b = next;
        }
#endif
    }

    private void FreeStrings()
    {
#if AMIGA
        byte* b = _strings;
        while (b != null)
        {
            byte* next = *(byte**)b;
            Mem.Free(b);
            b = next;
        }
#endif
        _strings = null;
        _className = null;
    }

#if AMIGA
    /// <summary>String copies the object holds (the class name included): to check that <c>Set</c> does not pile them up.</summary>
    public int StringCopies
    {
        get
        {
            int n = 0;
            for (byte* b = _strings; b != null; b = *(byte**)b) n++;
            return n;
        }
    }
#else
    public int StringCopies => 0;
#endif

#if !AMIGA
    // Host: the attributes set so far (no class behaviour: what was set is what is read).
    private uint[] _hostTags = new uint[24];
    private nuint[] _hostValues = new nuint[24];
    private int _hostCount;

    // Host: the public classes of the Kickstart 3.1 ROM; any other name fails as an unknown class would.
    private static bool HostKnows(string name)
    {
        string[] known = { "rootclass", "imageclass", "frameiclass", "sysiclass", "fillrectclass", "itexticlass", "gadgetclass",
            "propgclass", "strgclass", "buttongclass", "frbuttonclass", "groupgclass", "icclass", "modelclass" };
        return Array.IndexOf(known, name) >= 0;
    }

    private void HostStore(uint tag, nuint value)
    {
        for (int i = 0; i < _hostCount; i++)
        {
            if (_hostTags[i] == tag) { _hostValues[i] = value; return; }
        }
        if (_hostCount < _hostTags.Length) { _hostTags[_hostCount] = tag; _hostValues[_hostCount] = value; _hostCount++; }
    }
#endif
}

// ------------------------------------------------------------------ window and events

/// <summary>What happened in a <see cref="BoopsiWindow"/>.</summary>
public enum BoopsiEventKind
{
    /// <summary>The close gadget was clicked (or <see cref="BoopsiWindow.Wait"/> was called on a window that is not open).</summary>
    Close,
    /// <summary>A gadget with <see cref="Ga.RelVerify"/> was released (a button clicked, Return in a string gadget, a slider let go).</summary>
    GadgetUp,
    /// <summary>A gadget whose <see cref="Ica.Target"/> is <see cref="Ica.Idcmp"/> changed an attribute (a slider being dragged).</summary>
    Update,
}

/// <summary>An event of a <see cref="BoopsiWindow"/>: its kind, the gadget (null for Close), and for Update the first changed attribute and its value.</summary>
public struct BoopsiEvent
{
    public BoopsiEventKind Kind;
    public BoopsiObject? Source;
    /// <summary>Update: the attribute's tag (compare with <c>Pga.Top.Tag</c>, ...); GadgetUp: 0.</summary>
    public uint Attr;
    /// <summary>Update: the attribute's new value; GadgetUp: the message's code.</summary>
    public int Value;

    public BoopsiEvent(BoopsiEventKind kind, BoopsiObject? source, uint attr, int value)
    {
        Kind = kind;
        Source = source;
        Attr = attr;
        Value = value;
    }
}

/// <summary>
/// A window on the default public screen (Workbench) that holds BOOPSI gadgets and images. Open it, then make the gadgets with
/// window coordinates (<see cref="InnerLeft"/>, <see cref="InnerTop"/>) and add them; a gadget that is not created yet is created by
/// <see cref="Add"/> with the screen's <see cref="DrawInfo"/> added to its starting attributes:
/// <code>
/// var win = new BoopsiWindow { Title = "BOOPSI", Width = 300, Height = 100 };
/// if (!win.Open()) return;
/// var b = new BoopsiObject("frbuttonclass");
/// b.Init(Ga.Left, win.InnerLeft + 10); ... b.Init(Ga.Text, "OK"); b.Init(Ga.RelVerify, true);
/// win.Add(b);
/// b.Notify += e => clicks++;                         // or a pull loop: BoopsiEvent e = win.Wait();
/// win.Run();                                         // until the close gadget
/// win.Close(); Heap.Free(ref win);                   // Close disposes and frees the gadgets and images it was given
/// </code>
/// Events come from IDCMP_GADGETUP (gadgets with <see cref="Ga.RelVerify"/>) and IDCMP_IDCMPUPDATE (gadgets whose
/// <see cref="Ica.Target"/> is <see cref="Ica.Idcmp"/>). <see cref="Dispatch"/> raises the gadget's <see cref="BoopsiObject.Notify"/>
/// and then the window's own <see cref="OnGadgetUp"/>, <see cref="OnUpdate"/> and <see cref="OnCloseRequest"/>, which a class derived
/// from this one can override. On the host nothing is shown: <see cref="HostRelease"/>, <see cref="HostUpdate"/> and
/// <see cref="HostClose"/> queue what a person would do.
/// </summary>
[Extensible]
public unsafe class BoopsiWindow
{
    public string? Title { get; set; }

    /// <summary>Inner width in pixels (default 300).</summary>
    public int Width { get; set; }

    /// <summary>Inner height in pixels (default 120).</summary>
    public int Height { get; set; }

    private BoopsiObject? _first;
    private BoopsiObject? _last;
    private bool _open;
    private int _dispatching;          // Dispatch calls running (a handler may ask to close)
    private bool _closePending;        // Close was called from a handler: done when Dispatch returns
    private int _nextId;               // ids given to gadgets without a Ga.Id (from 1000)
    private int _innerLeft;
    private int _innerTop;
    private byte* _titleCopy;
#if AMIGA
    private void* _screen;
    private void* _drawInfo;
    private void* _window;
#endif

    public BoopsiWindow()
    {
        Width = 300;
        Height = 120;
        _nextId = 1000;
    }

    public bool IsOpen => _open;

    /// <summary>Left border width: add it to gadget positions (0 before <see cref="Open"/>).</summary>
    public int InnerLeft => _innerLeft;

    /// <summary>Top border height (title bar included): add it to gadget positions (0 before <see cref="Open"/>).</summary>
    public int InnerTop => _innerTop;

#if AMIGA
    /// <summary>The screen's DrawInfo (for <see cref="Ga.DrawInfo"/>), while the window is open.</summary>
    public void* DrawInfo => _drawInfo;

    /// <summary>The system window (<c>struct Window *</c>), while it is open.</summary>
    public void* SystemWindow => _window;

    /// <summary>Opens the window. False if the screen, its DrawInfo or the window cannot be had; nothing stays open then.</summary>
    public bool Open()
    {
        if (_open) return false;
        _screen = Intuition.LockPubScreen(null);
        if (_screen == null) return false;
        _drawInfo = Intuition.GetScreenDrawInfo(_screen);
        _titleCopy = GuiText.Dup(Title);
        TagList w = new TagList();
        w.Add(Intuition.WA_Left, 40);
        w.Add(Intuition.WA_Top, 30);
        w.Add(Layout.WaInnerWidth, Width);
        w.Add(Layout.WaInnerHeight, Height);
        if (_titleCopy != null) w.Add(Layout.WaTitle, _titleCopy);
        w.Add(Layout.WaPubScreen, _screen);
        w.Add(Intuition.WA_IDCMP, Layout.IdcmpGadgetUp | Layout.IdcmpCloseWindow | Layout.IdcmpIdcmpUpdate);
        w.Add(Layout.WaDragBar, true);
        w.Add(Layout.WaDepthGadget, true);
        w.Add(Layout.WaCloseGadget, true);
        w.Add(Intuition.WA_NoCareRefresh, true);
        w.Add(Intuition.WA_Activate, true);
        nuint* t = w.End();
        if (_drawInfo != null && t != null) _window = Intuition.OpenWindowTagList(null, (void*)t);
        if (_window == null)
        {
            ReleaseScreen();
            return false;
        }
        _innerLeft = *(sbyte*)((byte*)_window + Layout.WindowBorderLeft);
        _innerTop = *(sbyte*)((byte*)_window + Layout.WindowBorderTop);
        _open = true;
        // Images first: a frame fills its inside, so it must not be drawn over a gadget.
        bool ok = true;
        for (BoopsiObject? o = _first; o != null && ok; o = o.NextInWindow) if (o.IsImage) ok = Attach(o);
        for (BoopsiObject? o = _first; o != null && ok; o = o.NextInWindow) if (!o.IsImage) ok = Attach(o);
        if (!ok)
        {
            // A gadget could not be created: nothing stays open (the objects stay in the window's list; Close disposes them).
            for (BoopsiObject? o = _first; o != null; o = o.NextInWindow) Unshow(o);
            CloseSystemWindow();
            ReleaseScreen();
            return false;
        }
        return true;
    }

    // Creates (if needed) and shows one object of the window.
    private bool Attach(BoopsiObject o)
    {
        if (!Prepare(o, _drawInfo)) return false;
        if (o.IsImage)
        {
            Intuition.DrawImage(*(void**)((byte*)_window + Layout.WindowRPort), o.Address, 0, 0);
            return true;
        }
        o.GadgetId = ReadId(o);
        if (IdInUse(o.GadgetId, o)) return false;
        Intuition.AddGList(_window, o.Address, 0xFFFF, 1, null);   // position ~0: at the end
        Intuition.RefreshGList(o.Address, _window, null, 1);
        o.Shown = true;
        return true;
    }

    private static int ReadId(BoopsiObject o) { return *(ushort*)((byte*)o.Address + Layout.GadGadgetId); }

    // Takes a gadget out of the open window (before it is disposed); only one that was put in.
    private void Unshow(BoopsiObject o)
    {
        if (_window != null && o.Shown) Intuition.RemoveGList(_window, o.Address, 1);
        o.Shown = false;
    }

    private void CloseSystemWindow()
    {
        if (_window != null) { Intuition.CloseWindow(_window); _window = null; }
    }

    private void ReleaseScreen()
    {
        if (_drawInfo != null) { Intuition.FreeScreenDrawInfo(_screen, _drawInfo); _drawInfo = null; }
        if (_screen != null) { Intuition.UnlockPubScreen(null, _screen); _screen = null; }
        if (_titleCopy != null) { GuiText.Free(_titleCopy); _titleCopy = null; }
        _open = false;
    }

    /// <summary>
    /// Waits for the next event (the task sleeps meanwhile). On a window that is not open it returns <see cref="BoopsiEventKind.Close"/> at once.
    /// </summary>
    public BoopsiEvent Wait()
    {
        if (!_open) return new BoopsiEvent(BoopsiEventKind.Close, null, 0, 0);
        void* port = *(void**)((byte*)_window + Layout.WindowUserPort);
        while (true)
        {
            byte* msg = (byte*)Exec.GetMsg(port);
            if (msg == null) { Exec.WaitPort(port); continue; }
            uint cls = *(uint*)(msg + Layout.MsgClass);
            int code = *(ushort*)(msg + Layout.MsgCode);
            void* address = *(void**)(msg + Layout.MsgIAddress);
            BoopsiObject? source = null;
            uint attr = 0;
            int value = code;
            if (cls == Layout.IdcmpGadgetUp)
            {
                for (BoopsiObject? o = _first; o != null && source == null; o = o.NextInWindow)
                    if (!o.IsImage && o.Address == address) source = o;
            }
            else if (cls == Layout.IdcmpIdcmpUpdate)
            {
                // The tag list belongs to Intuition and goes when the message is replied: read it first.
                int id = -1;
                value = 0;
                nuint* tags = (nuint*)address;
                while (tags != null)
                {
                    uint tag = (uint)tags[0];
                    if (tag == 0) break;
                    if (tag == Layout.TagMore) { tags = (nuint*)tags[1]; continue; }
                    if (tag == Layout.TagSkip) { tags += 2 * ((int)tags[1] + 1); continue; }
                    if (tag == Layout.GaId) id = (int)tags[1];
                    else if (tag != Layout.TagIgnore && attr == 0) { attr = tag; value = (int)(nint)tags[1]; }
                    tags += 2;
                }
                for (BoopsiObject? o = _first; o != null && source == null; o = o.NextInWindow)
                    if (!o.IsImage && o.GadgetId == id) source = o;
            }
            Exec.ReplyMsg((void*)msg);
            if (cls == Layout.IdcmpCloseWindow) return new BoopsiEvent(BoopsiEventKind.Close, null, 0, 0);
            if (source == null) continue;
            if (cls == Layout.IdcmpGadgetUp) return new BoopsiEvent(BoopsiEventKind.GadgetUp, source, 0, value);
            return new BoopsiEvent(BoopsiEventKind.Update, source, attr, value);
        }
    }

    private bool NoMoreEvents() { return false; }
#else
    /// <summary>Host: set false to make <see cref="Open"/> fail, the way a missing screen would.</summary>
    public static bool HostCanOpen = true;

    private static BoopsiEvent[] _queue = new BoopsiEvent[16];
    private static int _queued;
    private bool _drained;

    public void* DrawInfo => null;

    public void* SystemWindow => null;

    public bool Open()
    {
        if (_open || !HostCanOpen) return false;
        _titleCopy = GuiText.Dup(Title);
        _innerLeft = 4;
        _innerTop = 11;
        _queued = 0;
        _open = true;
        for (BoopsiObject? o = _first; o != null; o = o.NextInWindow) Attach(o);
        return true;
    }

    private bool Attach(BoopsiObject o)
    {
        if (!Prepare(o, null)) return false;
        if (o.IsImage) return true;
        o.GadgetId = ReadId(o);
        if (IdInUse(o.GadgetId, o)) return false;
        o.Shown = true;
        return true;
    }

    private static int ReadId(BoopsiObject o) { return o.Get(Ga.Id); }

    private void Unshow(BoopsiObject o) { o.Shown = false; }

    private void CloseSystemWindow() { }

    private void ReleaseScreen()
    {
        _titleCopy = null;
        _queued = 0;
        _open = false;
    }

    /// <summary>Host: the next queued event; <see cref="BoopsiEventKind.Close"/> when none is left (so a loop cannot hang).</summary>
    public BoopsiEvent Wait()
    {
        _drained = _queued == 0;
        if (!_open || _queued == 0) return new BoopsiEvent(BoopsiEventKind.Close, null, 0, 0);
        BoopsiEvent e = _queue[0];
        for (int i = 1; i < _queued; i++) _queue[i - 1] = _queue[i];
        _queued--;
        return e;
    }

    private bool NoMoreEvents() { return _drained; }

    private static void Post(BoopsiEvent e)
    {
        if (_queued < 16) { _queue[_queued] = e; _queued++; }
    }

    /// <summary>Host: a gadget is released (a click on a button, Return in a string gadget).</summary>
    public void HostRelease(BoopsiObject gadget, int code) { Post(new BoopsiEvent(BoopsiEventKind.GadgetUp, gadget, 0, code)); }

    /// <summary>Host: a gadget changes an attribute (a slider is dragged): the value is stored, then an Update event is queued.</summary>
    public void HostUpdate(BoopsiObject gadget, IntAttr attr, int value)
    {
        gadget.Set(attr, value);
        Post(new BoopsiEvent(BoopsiEventKind.Update, gadget, attr.Tag, value));
    }

    /// <summary>Host: the close gadget is clicked.</summary>
    public void HostClose() { Post(new BoopsiEvent(BoopsiEventKind.Close, null, 0, 0)); }
#endif

    /// <summary>
    /// Adds a gadget (any BOOPSI gadget). The window owns it from now on and disposes and frees it in <see cref="Close"/>. If the window is
    /// open, it is created (if it is not yet, with <see cref="Ga.DrawInfo"/>) and shown at once; otherwise when the window opens.
    /// False if it belongs to a window or another object already, its <see cref="Ga.Id"/> is one another gadget of the window has or asked for,
    /// or it could not be created. After a false the caller still owns the gadget (it may have been created already: dispose and free it).
    /// </summary>
    public bool Add(BoopsiObject gadget) { return AddObject(gadget, false); }

    /// <summary>
    /// Adds an image (frameiclass, ...), drawn at its own position when it is added or the window opens (before the gadgets then). Owned
    /// like a gadget. A frame fills its inside: add it before a gadget that sits inside it.
    /// </summary>
    public bool AddImage(BoopsiObject image) { return AddObject(image, true); }

    // Creates an object that is not created yet: a gadget gets the screen's DrawInfo and, without a Ga.Id of its own, a free id
    // (Update events are matched by id, so the ids in a window must differ).
    private bool Prepare(BoopsiObject o, void* drawInfo)
    {
        if (o.IsCreated) return true;
        if (!o.IsImage)
        {
            o.AddInit(BTag.GaDrawInfo, (nuint)drawInfo, TagList.MaxPairs);
            if (!o.HasId)
            {
                while (IdInUse(_nextId, o)) _nextId++;
                o.AddInit(BTag.GaId, (nuint)_nextId, TagList.MaxPairs);
                _nextId++;
            }
        }
        return o.Create();
    }

    // An id is taken when another gadget of the window has it or asked for it (Ga.Id before it is created).
    private bool IdInUse(int id, BoopsiObject except)
    {
        for (BoopsiObject? p = _first; p != null; p = p.NextInWindow)
            if (p != except && !p.IsImage && p.GadgetId == id) return true;
        return false;
    }

    private bool AddObject(BoopsiObject o, bool image)
    {
        if (o.Window != null || o.OwnerObject != null) return false;
        o.IsImage = image;
        if (!image)
        {
            // A duplicate id is refused here, before anything is created or shown.
            if (o.IsCreated) o.GadgetId = ReadId(o);
            if (o.GadgetId >= 0 && IdInUse(o.GadgetId, o)) return false;
        }
        if (_open && !Attach(o)) return false;
        o.Window = this;
        if (_last == null) _first = o; else _last.NextInWindow = o;
        _last = o;
        return true;
    }

    /// <summary>Takes an object out of the window's list (and the open window): from <see cref="BoopsiObject.Dispose"/>.</summary>
    internal void Detach(BoopsiObject o)
    {
        Unshow(o);
        BoopsiObject? prev = null;
        for (BoopsiObject? p = _first; p != null; p = p.NextInWindow)
        {
            if (p == o)
            {
                if (prev == null) _first = o.NextInWindow; else prev.NextInWindow = o.NextInWindow;
                if (_last == o) _last = prev;
                break;
            }
            prev = p;
        }
        o.NextInWindow = null;
        o.Window = null;
    }

    /// <summary>
    /// Closes the window: the gadgets out of it, the window, then the gadgets and images it was given are disposed and freed (do not use
    /// them afterwards), then the DrawInfo and the screen lock go. Free the window object itself after this. Called from an event
    /// handler (a Quit button's <see cref="BoopsiObject.Notify"/>, <see cref="OnGadgetUp"/>), it only marks the window: the teardown
    /// runs when <see cref="Dispatch"/> returns (false then, so <see cref="Run"/> stops), because the gadget whose event is being
    /// handled is still in use until then.
    /// </summary>
    public void Close()
    {
        if (_dispatching > 0) { _closePending = true; return; }
        for (BoopsiObject? o = _first; o != null; o = o.NextInWindow) Unshow(o);
        CloseSystemWindow();
        BoopsiObject? g = _first;
        _first = null;
        _last = null;
        while (g != null)
        {
            BoopsiObject? next = g.NextInWindow;
            g.NextInWindow = null;
            g.Window = null;
            g.Dispose();
            Heap.Free(ref g);
            g = next;
        }
        ReleaseScreen();
    }

    /// <summary>Waits and dispatches events until a close request is accepted (<see cref="OnCloseRequest"/>) or a handler closed the window; then call <see cref="Close"/> (again: it does nothing the second time).</summary>
    public void Run()
    {
        while (true)
        {
            BoopsiEvent e = Wait();
            if (e.Kind == BoopsiEventKind.Close && (!_open || NoMoreEvents())) return;
            if (!Dispatch(e)) return;
        }
    }

    /// <summary>
    /// Hands one event from <see cref="Wait"/> on: the gadget's <see cref="BoopsiObject.Notify"/> handlers, then <see cref="OnGadgetUp"/> or
    /// <see cref="OnUpdate"/>; a Close goes to <see cref="OnCloseRequest"/>. False for an accepted close (stop waiting), true otherwise.
    /// </summary>
    public bool Dispatch(BoopsiEvent e)
    {
        bool go = true;
        _dispatching++;
        if (e.Kind == BoopsiEventKind.Close) go = !OnCloseRequest();
        else
        {
            BoopsiObject? source = e.Source;
            if (source != null)
            {
                source.Raise(e);
                if (!_closePending)
                {
                    if (e.Kind == BoopsiEventKind.GadgetUp) OnGadgetUp(source, e.Value);
                    else OnUpdate(source, e.Attr, e.Value);
                }
            }
        }
        _dispatching--;
        if (_closePending)
        {
            // At any depth: a Run inside a handler stops too; the outermost Dispatch closes the window.
            if (_dispatching > 0) return false;
            _closePending = false;
            Close();
            return false;
        }
        return go;
    }

    /// <summary>A gadget was released (from <see cref="Run"/> or <see cref="Dispatch"/>). Does nothing unless overridden.</summary>
    protected virtual void OnGadgetUp(BoopsiObject gadget, int code) { }

    /// <summary>A gadget changed an attribute. Does nothing unless overridden.</summary>
    protected virtual void OnUpdate(BoopsiObject gadget, uint attr, int value) { }

    /// <summary>The close gadget was clicked: true ends <see cref="Run"/>. True unless overridden.</summary>
    protected virtual bool OnCloseRequest() { return true; }
}
#endif
