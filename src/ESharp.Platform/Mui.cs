// Amiga-only (MUI): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
using System;

namespace ESharp.Platform;

// MUI facts (class names, attribute and method numbers, special values, message layouts) are taken from the AROS Zune headers
// (AROS Public License); muimaster.library vectors from the AROS fd file. Nothing here comes from the MUI distribution.

#if AMIGA
/// <summary>
/// muimaster.library (MUI 3.8 is version 19; MUI 4/5 and Zune are newer). Not in ROM: <see cref="Mui.Open"/> opens it when it is
/// installed. The emitter calls these through the library's vectors on each target (no MUI header is used).
/// </summary>
[LibraryBase("muimaster", "MUIMasterBase", "struct Library")]
public static unsafe class MuiMaster
{
    /// <summary>Emitter intrinsic: sets the library base the calls below use.</summary>
    public static extern void SetBase(void* library);

    [AmigaLib("muimaster", -30), Since(19)] public static extern void* MUI_NewObjectA(byte* classId, void* tags);
    [AmigaLib("muimaster", -36), Since(19)] public static extern void MUI_DisposeObject(void* obj);
    [AmigaLib("muimaster", -42), Since(19)] public static extern int MUI_RequestA(void* app, void* win, uint flags, byte* title, byte* gadgets, byte* format, void* parms);
    [AmigaLib("muimaster", -66), Since(19)] public static extern int MUI_Error();
    [AmigaLib("muimaster", -120), Since(19)] public static extern void* MUI_MakeObjectA(int type, nuint* parms);
}
#endif

/// <summary>MUI numbers (the same on every target).</summary>
internal static class MuiId
{
    public const uint Notify = 0x8042c9cb, CallHook = 0x8042b96b, NewInput = 0x80423ba6, ReturnId = 0x804276ef;
    public const uint ListInsertSingle = 0x804254d5, ListClear = 0x8042ad89, ListGetEntry = 0x804280ec, ListRemove = 0x8042647e;
    public const uint GroupInitChange = 0x80420887, GroupExitChange = 0x8042d1cc;
    public const uint OmAddMember = 0x109, OmRemMember = 0x10a;
    public const uint EveryTime = 0x49893131, TriggerValue = 0x49893131;
    public const uint ReturnIdQuit = 0xffffffff;
    public const int ListInsertBottom = -3, ListActiveOff = -1;
    public static nuint ListHookString => ~(nuint)0;
    public const int MakeLabel = 1, MakeButton = 2, MakeCheckmark = 3, MakeHSpace = 9, MakeVSpace = 10;
}

/// <summary>Attributes of the MUI classes (MUIA_...), with their value types: <c>slider.Set(Muia.NumericValue, 30)</c>.</summary>
public static class Muia
{
    public static StrAttr ApplicationTitle => new StrAttr(0x804281b8);
    /// <summary>A version string (<c>"$VER: name 1.0 (17.11.2026)"</c>).</summary>
    public static StrAttr ApplicationVersion => new StrAttr(0x8042b33f);
    public static StrAttr ApplicationCopyright => new StrAttr(0x8042ef4d);
    public static StrAttr ApplicationAuthor => new StrAttr(0x80424842);
    public static StrAttr ApplicationDescription => new StrAttr(0x80421fc6);
    /// <summary>The ARexx and settings name of the program (upper case, no spaces); set only at creation.</summary>
    public static StrAttr ApplicationBase => new StrAttr(0x8042e07a);
    public static BoolAttr ApplicationUseCommodities => new BoolAttr(0x80425ee5);
    public static BoolAttr ApplicationUseRexx => new BoolAttr(0x80422387);
    /// <summary>True puts every window of the application to sleep (busy pointer, no input).</summary>
    public static BoolAttr ApplicationSleep => new BoolAttr(0x80425711);
    public static StrAttr WindowTitle => new StrAttr(0x8042ad3d);
    /// <summary>A number that names the window for MUI's saved positions (four letters made into a number, or 0).</summary>
    public static IntAttr WindowId => new IntAttr(0x804201bd);
    public static ObjAttr WindowRootObject => new ObjAttr(0x8042cba5);
    public static BoolAttr WindowOpen => new BoolAttr(0x80428aa0);
    /// <summary>Becomes true when the close gadget is clicked (read only: notify on it).</summary>
    public static BoolAttr WindowCloseRequest => new BoolAttr(0x8042e86e);
    public static BoolAttr WindowActivate => new BoolAttr(0x80428d2f);
    public static IntAttr WindowWidth => new IntAttr(0x8042dcae);
    public static IntAttr WindowHeight => new IntAttr(0x80425846);
    public static ObjAttr GroupChild => new ObjAttr(0x804226e6);
    /// <summary>Children side by side instead of one under the other (only at creation).</summary>
    public static BoolAttr GroupHoriz => new BoolAttr(0x8042536b);
    /// <summary>Children in this many columns, filled row by row.</summary>
    public static IntAttr GroupColumns => new IntAttr(0x8042f416);
    public static BoolAttr GroupSameSize => new BoolAttr(0x80420860);
    /// <summary>The frame (a <see cref="MuiFrame"/> value; only at creation).</summary>
    public static IntAttr Frame => new IntAttr(0x8042ac64);
    public static StrAttr FrameTitle => new StrAttr(0x8042d1c7);
    public static IntAttr Background => new IntAttr(0x8042545b);
    public static BoolAttr Disabled => new BoolAttr(0x80423661);
    public static BoolAttr Selected => new BoolAttr(0x8042654b);
    /// <summary>A button was pressed and released (read only: notify on it becoming false).</summary>
    public static BoolAttr Pressed => new BoolAttr(0x80423535);
    public static IntAttr Weight => new IntAttr(0x80421d1f);
    public static BoolAttr CycleChain => new BoolAttr(0x80421ce7);
    public static StrAttr ShortHelp => new StrAttr(0x80428fe3);
    /// <summary>The text (copied by the object; may hold MUI's text escapes).</summary>
    public static StrAttr TextContents => new StrAttr(0x8042f8dc);
    public static StrAttr TextPreParse => new StrAttr(0x8042566d);
    public static StrAttr StringContents => new StrAttr(0x80428ffd);
    public static IntAttr StringMaxLen => new IntAttr(0x80424984);
    public static IntAttr StringInteger => new IntAttr(0x80426e8a);
    /// <summary>Return was pressed in the string (read only: notify on it).</summary>
    public static StrAttr StringAcknowledge => new StrAttr(0x8042026c);
    public static PtrAttr CycleEntries => new PtrAttr(0x80420629);
    public static IntAttr CycleActive => new IntAttr(0x80421788);
    public static IntAttr NumericValue => new IntAttr(0x8042ae3a);
    public static IntAttr NumericMin => new IntAttr(0x8042e404);
    public static IntAttr NumericMax => new IntAttr(0x8042d78a);
    public static BoolAttr SliderHoriz => new BoolAttr(0x8042fad1);
    public static IntAttr GaugeCurrent => new IntAttr(0x8042f0dd);
    public static IntAttr GaugeMax => new IntAttr(0x8042bcdb);
    public static BoolAttr GaugeHoriz => new BoolAttr(0x804232dd);
    public static StrAttr GaugeInfoText => new StrAttr(0x8042bf15);
    /// <summary>Number of entries (read only).</summary>
    public static IntAttr ListEntries => new IntAttr(0x80421654);
    /// <summary>The selected entry, or -1 for none.</summary>
    public static IntAttr ListActive => new IntAttr(0x8042391c);
    public static PtrAttr ListConstructHook => new PtrAttr(0x8042894f);
    public static PtrAttr ListDestructHook => new PtrAttr(0x804297ce);
    public static ObjAttr ListviewList => new ObjAttr(0x8042bcce);
    public static BoolAttr ListviewInput => new BoolAttr(0x8042682d);
}

/// <summary>Values of <see cref="Muia.Frame"/>.</summary>
public static class MuiFrame
{
    public const int None = 0, Button = 1, ImageButton = 2, Text = 3, String = 4, ReadList = 5, InputList = 6, Prop = 7, Gauge = 8,
        Group = 9, PopUp = 10, Virtual = 11, Slider = 12, Knob = 13, Drag = 14;
}

/// <summary>A small message for DoMethod (eight pointer-sized slots, as MUI's methods want them on every target).</summary>
internal unsafe struct MuiMsg
{
    private nuint _s0, _s1, _s2, _s3, _s4, _s5, _s6, _s7;

    public void Put(int i, nuint v)
    {
        fixed (nuint* p = &_s0) p[i] = v;
    }

    public nuint* Ptr()
    {
        fixed (nuint* p = &_s0) return p;
    }
}

/// <summary>
/// MUI as a whole: <see cref="Open"/> before making any MUI object. MUI is not part of AmigaOS 3.1 (the user installs it; MorphOS
/// and AROS have it built in, AmigaOS 4 ships MUI 5), so a MUI program checks the result and says so when it is missing:
/// <code>
/// if (!Mui.Open()) { Dos.PutStr("this program needs MUI\n"); return; }
/// </code>
/// On the host there is no MUI: the objects remember what is set and raise their notifications when an attribute is set (see
/// <see cref="MuiObject"/>); <see cref="HostAvailable"/> false makes <see cref="Open"/> fail.
/// </summary>
public static unsafe class Mui
{
    /// <summary>Notification handlers running (a handler that sets an attribute may start another).</summary>
    internal static int HandlerDepth;
    internal static int LiveObjects;
    private static bool _warned;

#if AMIGA
    private static void* _base;

    /// <summary>Opens muimaster.library version 19 (MUI 3.8) or newer. False if MUI is not installed.</summary>
    public static bool Open()
    {
        if (_base != null) return true;
        _base = Exec.OpenLibrary("muimaster.library", 19);
        if (_base == null) return false;
        MuiMaster.SetBase(_base);
        return true;
    }

    /// <summary>Closes the library again, unless MUI objects are still alive (dispose the application first).</summary>
    public static void Close()
    {
        if (_base == null || LiveObjects > 0) return;
        MuiMaster.SetBase(null);
        Exec.CloseLibrary(_base);
        _base = null;
    }

    public static bool IsOpen => _base != null;

    /// <summary>
    /// A MUI requester (MUI_RequestA): <paramref name="gadgets"/> like <c>"_OK|_Cancel"</c>; returns 1 for the first gadget, 2 for the
    /// next, ..., 0 for the last. <paramref name="app"/> may be null. Waits for the user; never call it from a notification handler.
    /// </summary>
    public static int Request(MuiApplication? app, string title, string gadgets, string text)
    {
        if (_base == null) return -1;
        MuiMsg parms = new MuiMsg();
        parms.Put(0, (nuint)Cast.StringToPtr(text));
        byte* format = Cast.StringToPtr("%s");
        void* a = app == null ? null : app.Address;
        return MuiMaster.MUI_RequestA(a, null, 0, Cast.StringToPtr(title), Cast.StringToPtr(gadgets), format, (void*)parms.Ptr());
    }
#else
    /// <summary>Host: whether the fake MUI is "installed". Set false to test the missing-MUI path.</summary>
    public static bool HostAvailable = true;

    /// <summary>Host: what <see cref="Request"/> answers.</summary>
    public static int HostRequestAnswer = 1;

    private static bool _open;

    public static bool Open()
    {
        _open = HostAvailable;
        return _open;
    }

    public static void Close()
    {
        if (LiveObjects == 0) _open = false;
    }

    public static bool IsOpen => _open;

    public static int Request(MuiApplication? app, string title, string gadgets, string text) { return _open ? HostRequestAnswer : -1; }
#endif

    /// <summary>True while a notification handler runs.</summary>
    public static bool InHandler => HandlerDepth > 0;

    /// <summary>MUI objects made and not yet disposed (all of the program).</summary>
    public static int Live => LiveObjects;

    internal static void Warn(string text)
    {
        if (_warned) return;
        _warned = true;
        Dos.PutStr(text);
    }
}

/// <summary>
/// One notification of a <see cref="MuiObject"/>: MUIM_Notify with MUIM_CallHook on a struct Hook of its own, whose h_Data points
/// here. MUI calls the hook on the program's own task (inside the application's input handling, or inside the Set that changed the
/// attribute), so the C# handler may allocate, print and set attributes of other objects.
/// </summary>
internal sealed unsafe class MuiNotification
{
    internal MuiNotification? Next;
    internal uint Attr;
    internal nuint Trigger;                // MuiId.EveryTime, or the value that triggers it, widened as Set widens it (sign-extended)
    internal bool Registered;
    internal Action<int>? OnInt;
    internal Action<bool>? OnBool;
    internal Action<string?>? OnStr;
    internal Action? OnPlain;
#if AMIGA
    private byte* _hook;

    internal MuiNotification()
    {
        _hook = Mem.AllocFast((uint)Layout.HookSize);
        if (_hook != null)
        {
            delegate*<void*, void*, void*, nuint> entry = &Entry;
            *(void**)(_hook + Layout.HookEntryOffset) = (void*)entry;
            *(void**)(_hook + Layout.HookData) = Cast.ObjectToPtr(this);
        }
    }

    internal bool IsValid => _hook != null;

    internal void* HookAddress => _hook;

    // MUIM_CallHook passes a pointer to its parameters as the message: the first one is the trigger value.
    [HookEntry]
    private static nuint Entry(void* hook, void* obj, void* msg)
    {
        void* data = *(void**)((byte*)hook + Layout.HookData);
        if (data == null || msg == null) return 0;
        MuiNotification n = Cast.PtrToObject<MuiNotification>(data);
        n.Fire(*(nuint*)msg);
        return 0;
    }
#else
    internal MuiNotification() { }

    internal bool IsValid => true;

    internal void* HookAddress => Cast.ObjectToPtr(this);
#endif

    internal void Fire(nuint value)
    {
        Mui.HandlerDepth++;
        Action<int>? i = OnInt;
        Action<bool>? b = OnBool;
        Action<string?>? s = OnStr;
        Action? p = OnPlain;
        if (i != null) i((int)(nint)value);
        else if (b != null) b(value != 0);
        else if (s != null) s(value == 0 ? null : Cast.PtrToString((byte*)value));
        else if (p != null) p();
        Mui.HandlerDepth--;
    }

    internal void Free()
    {
#if AMIGA
        if (_hook != null)
        {
            *(void**)(_hook + Layout.HookData) = null;
            Mem.Free(_hook);
            _hook = null;
        }
#endif
        Heap.Free(ref OnInt);
        Heap.Free(ref OnBool);
        Heap.Free(ref OnStr);
        Heap.Free(ref OnPlain);
    }
}

/// <summary>
/// A MUI object (a BOOPSI object made by muimaster.library): any MUI class by name (<c>new MuiObject("Text.mui")</c>), or one of the
/// wrappers below. <c>Init</c>, <c>Set</c> and <c>Get</c> work as on <see cref="BoopsiObject"/> with the <see cref="Muia"/> attributes.
/// <para>
/// Ownership follows MUI: an object added to a group (<see cref="MuiGroup.Add"/>), a window's <see cref="MuiWindow.Content"/> and a window
/// added to an application belong to their parent, and MUI disposes them with it. So the program disposes and frees only the root,
/// normally the <see cref="MuiApplication"/>: <c>app.Dispose(); Heap.Free(ref app);</c> disposes the whole tree once (MUI_DisposeObject),
/// then frees every wrapper, its string copies and its notifications. An object that is not in a tree (never added, or its parent
/// could not be made) is disposed by itself. A group member or a window disposed by the program is taken out of its parent first.
/// </para>
/// <para>
/// Notifications: <see cref="WhenChanged(IntAttr, Action{int})"/> and its siblings run a C# handler (also a capturing lambda) each time an
/// attribute changes: when the user moves a slider, and also when the program sets the attribute. Handlers run on the program's own
/// task. In a handler: set attributes, print, call <see cref="MuiApplication.Quit"/>; never dispose or free MUI objects there (that is
/// refused with a message): quit, and dispose after <see cref="MuiApplication.Run"/> returns.
/// </para>
/// </summary>
public unsafe class MuiObject : BoopsiObject
{
    internal MuiObject? MuiParent;
    internal MuiObject? FirstChild;
    internal MuiObject? LastChild;
    internal MuiObject? NextSibling;
    internal bool ViaInit;            // given to the parent as a starting attribute (window root, listview list): not added with OM_ADDMEMBER
    internal uint InitTag;            // ... with this attribute
    internal bool Attached;           // the parent's system object holds it (disposes it)
    internal bool SystemGone;         // disposed with its parent by MUI
    private int _makeType;            // MUI_MakeObjectA type, 0 for MUI_NewObjectA
    private nuint _make0;
    private nuint _make1;
    private MuiNotification? _notes;

    /// <summary>An object of a MUI class by name (<c>"Text.mui"</c>, <c>"Group.mui"</c>, ...).</summary>
    public MuiObject(string muiClass) : base(muiClass) { }

    // An object made by MUI_MakeObjectA (a button, a check mark, a label, a space) with up to two parameters.
    internal MuiObject(int makeType, string? text, nuint second) : base("")
    {
        _makeType = makeType;
        _make0 = (nuint)KeepString(text, 0);
        _make1 = second;
    }

    internal void SetMakeNumber(nuint first) { _make0 = first; }

    /// <summary>The application this object is in (null if it is not in one yet).</summary>
    public MuiApplication? Application
    {
        get
        {
            MuiObject? o = this;
            while (o != null && !(o is MuiApplication)) o = o.MuiParent;
            return o as MuiApplication;
        }
    }

#if AMIGA
    internal override void* NewSystem(byte* className, nuint* tags)
    {
        if (!Mui.IsOpen) return null;
        void* o = null;
        if (_makeType != 0)
        {
            MuiMsg p = new MuiMsg();
            p.Put(0, _make0);
            p.Put(1, _make1);
            o = MuiMaster.MUI_MakeObjectA(_makeType, p.Ptr());
            if (o != null && tags[0] != 0) Intuition.SetAttrsA(o, (void*)tags);   // MUI_MakeObjectA takes no tags: the starting attributes follow
        }
        else o = MuiMaster.MUI_NewObjectA(className, (void*)tags);
        return o;
    }
#else
    // Host: the classes the wrappers use, plus "" for MUI_MakeObjectA; any other name fails as an unknown class would.
    internal override bool HostKnowsClass(string name)
    {
        string[] known = { "", "Notify.mui", "Area.mui", "Application.mui", "Window.mui", "Group.mui", "Text.mui", "String.mui", "Cycle.mui",
            "Numeric.mui", "Slider.mui", "Gauge.mui", "Rectangle.mui", "Image.mui", "List.mui", "Listview.mui" };
        return Mui.IsOpen && Array.IndexOf(known, name) >= 0;
    }
#endif

    internal override void AfterCreate()
    {
        Mui.LiveObjects++;
        for (MuiNotification? n = _notes; n != null; n = n.Next) Register(n);
    }

    // ---------------------------------------------------------- the tree

    // Create(): this object and everything under it (children first where the parent takes them at creation).
    internal override bool CreateAll() { return CreateTree(); }

    internal bool CreateTree()
    {
        if (IsCreated) return true;
        for (MuiObject? c = FirstChild; c != null; c = c.NextSibling)
        {
            if (!c.ViaInit) continue;
            if (!c.CreateTree()) return false;
            AddInit(c.InitTag, (nuint)c.Address, TagList.MaxPairs);
        }
        bool ok = CreateSelf();
        for (MuiObject? c = FirstChild; c != null; c = c.NextSibling)
        {
            if (!c.ViaInit) continue;
            // A parent that could not be made may or may not have disposed what it was given: treat it as gone (a leak at worst,
            // never a second dispose).
            if (ok) c.Attached = true; else c.SystemGone = true;
        }
        if (!ok) return false;
        for (MuiObject? c = FirstChild; c != null; c = c.NextSibling)
        {
            if (c.ViaInit) continue;
            if (!c.CreateTree()) return false;
            AttachSystem(c);
        }
        return true;
    }

    // True when this object is in a window that is open (a group must then be told before and after its children change).
    internal bool InOpenWindow()
    {
        for (MuiObject? o = this; o != null; o = o.MuiParent)
            if (o is MuiWindow) return o.IsCreated && o.Get(Muia.WindowOpen);
        return false;
    }

    // Puts a created child into this created object (OM_ADDMEMBER).
    internal virtual void AttachSystem(MuiObject child)
    {
        DoMethod(MuiId.OmAddMember, (nuint)child.Address, 0);
        child.Attached = true;
    }

    // Takes a child out of this object before the program disposes it.
    internal virtual void DetachSystem(MuiObject child)
    {
        DoMethod(MuiId.OmRemMember, (nuint)child.Address, 0);
        child.Attached = false;
    }

    // Links a child into this object's list; its system object is added now if this one is created already.
    internal bool AddChild(MuiObject child, bool viaInit, uint initTag)
    {
        if (child == this || child.HasOwner) return false;
        for (MuiObject? p = MuiParent; p != null; p = p.MuiParent)
            if (p == child) return false;
        if (viaInit && IsCreated) return false;   // a starting attribute: too late
        if (IsCreated)
        {
            if (!child.CreateTree()) return false;
            AttachSystem(child);
        }
        child.ViaInit = viaInit;
        child.InitTag = initTag;
        child.MuiParent = this;
        child.OwnerObject = this;
        if (LastChild == null) FirstChild = child; else LastChild.NextSibling = child;
        LastChild = child;
        return true;
    }

    private void Unlink(MuiObject child)
    {
        MuiObject? prev = null;
        for (MuiObject? p = FirstChild; p != null; p = p.NextSibling)
        {
            if (p == child)
            {
                if (prev == null) FirstChild = p.NextSibling; else prev.NextSibling = p.NextSibling;
                if (LastChild == p) LastChild = prev;
                break;
            }
            prev = p;
        }
        child.NextSibling = null;
        child.MuiParent = null;
        child.OwnerObject = null;
    }

    private void MarkGone()
    {
        for (MuiObject? c = FirstChild; c != null; c = c.NextSibling)
        {
            c.SystemGone = true;
            c.MarkGone();
        }
    }

    internal override bool RefuseDispose()
    {
        if (Mui.HandlerDepth > 0)
        {
            Mui.Warn("E#: a MUI object cannot be disposed inside a notification handler; call Quit and dispose after Run\n");
            return true;
        }
        if (ViaInit && MuiParent != null && Attached && !SystemGone)
        {
            Mui.Warn("E#: a window's content or a listview's list belongs to it; dispose the window or listview\n");
            return true;
        }
        return false;
    }

    internal override void DisposeSystem()
    {
        MuiObject? parent = MuiParent;
        if (parent != null) parent.Unlink(this);
        if (!SystemGone)
        {
            if (Attached && parent != null && parent.IsCreated && !parent.SystemGone) parent.DetachSystem(this);
            if (!Attached)
            {
#if AMIGA
                MuiMaster.MUI_DisposeObject(Address);
#endif
            }
        }
        Attached = false;
        MarkGone();
        Mui.LiveObjects--;
    }

    internal override void DisposeOwned()
    {
        if (MuiParent != null) MuiParent.Unlink(this);   // one that was never made is still in its parent's list
        // The system objects are gone (with this one): free the wrappers under it, then the notifications' hooks.
        MuiObject? c = FirstChild;
        FirstChild = null;
        LastChild = null;
        while (c != null)
        {
            MuiObject? next = c.NextSibling;
            c.NextSibling = null;
            c.MuiParent = null;
            c.OwnerObject = null;
            c.Attached = false;
            c.Dispose();
            Heap.Free(ref c);
            c = next;
        }
        MuiNotification? n = _notes;
        _notes = null;
        while (n != null)
        {
            MuiNotification? next = n.Next;
            n.Free();
            Heap.Free(ref n);
            n = next;
        }
    }

    // ---------------------------------------------------------- notifications

    /// <summary>Runs <paramref name="handler"/> with the new value each time the attribute changes. False if there was no memory for it.</summary>
    public bool WhenChanged(IntAttr a, [Owned] Action<int> handler)
    {
        MuiNotification n = new MuiNotification();
        n.OnInt = handler;
        return AddNote(n, a.Tag, MuiId.EveryTime);
    }

    public bool WhenChanged(BoolAttr a, [Owned] Action<bool> handler)
    {
        MuiNotification n = new MuiNotification();
        n.OnBool = handler;
        return AddNote(n, a.Tag, MuiId.EveryTime);
    }

    /// <summary>A string attribute: the handler gets the object's text (valid during the call).</summary>
    public bool WhenChanged(StrAttr a, [Owned] Action<string?> handler)
    {
        MuiNotification n = new MuiNotification();
        n.OnStr = handler;
        return AddNote(n, a.Tag, MuiId.EveryTime);
    }

    /// <summary>Runs <paramref name="handler"/> each time the attribute becomes <paramref name="value"/>.</summary>
    public bool WhenEquals(BoolAttr a, bool value, [Owned] Action handler)
    {
        MuiNotification n = new MuiNotification();
        n.OnPlain = handler;
        return AddNote(n, a.Tag, value ? 1u : 0u);
    }

    public bool WhenEquals(IntAttr a, int value, [Owned] Action handler)
    {
        MuiNotification n = new MuiNotification();
        n.OnPlain = handler;
        return AddNote(n, a.Tag, (nuint)(nint)value);
    }

    private bool AddNote(MuiNotification n, uint attr, nuint trigger)
    {
        if (!n.IsValid)
        {
            n.Free();
            Heap.Free(ref n);
            return false;
        }
        n.Attr = attr;
        n.Trigger = trigger;
        n.Next = _notes;
        _notes = n;
        if (IsCreated) Register(n);
        return true;
    }

    private void Register(MuiNotification n)
    {
        if (n.Registered) return;
        n.Registered = true;
#if AMIGA
        // MUIM_Notify: attribute, trigger value, destination (this object), 3 parameters: MUIM_CallHook, the hook, the new value.
        MuiMsg m = new MuiMsg();
        m.Put(0, MuiId.Notify);
        m.Put(1, n.Attr);
        m.Put(2, n.Trigger);
        m.Put(3, (nuint)Address);
        m.Put(4, 3);
        m.Put(5, MuiId.CallHook);
        m.Put(6, (nuint)n.HookAddress);
        m.Put(7, MuiId.TriggerValue);
        DoMethodMsg(m.Ptr());
#endif
    }

#if !AMIGA
    // Host: MUI's notification, done by hand (every set counts, as there is no class to drop an unchanged value).
    internal override void HostChanged(uint tag, nuint value)
    {
        for (MuiNotification? n = _notes; n != null; n = n.Next)
            if (n.Attr == tag && (n.Trigger == MuiId.EveryTime || n.Trigger == value)) n.Fire(value);
    }
#endif
}

/// <summary>
/// A MUI application: the root of the tree, which owns its windows. Make the windows and their contents, add the windows, then
/// <see cref="Create"/> makes every MUI object at once:
/// <code>
/// var app = new MuiApplication("E# demo", "ESHARPDEMO");
/// var win = new MuiWindow("Hello") { Content = group };
/// app.Add(win);
/// if (!app.Create() || !win.Open()) ...
/// app.Run();                              // until a window's close gadget (or Quit)
/// app.Dispose(); Heap.Free(ref app);      // the whole tree
/// </code>
/// </summary>
public unsafe class MuiApplication : MuiObject
{
    private bool _quit;

    /// <summary>An application with a title and a base name (upper case letters, no spaces: the ARexx port and settings name).</summary>
    public MuiApplication(string title, string baseName) : base("Application.mui")
    {
        Init(Muia.ApplicationTitle, title);
        Init(Muia.ApplicationBase, baseName);
        Init(Muia.ApplicationUseCommodities, false);
        Init(Muia.ApplicationUseRexx, false);
    }

    /// <summary>Adds a window (before or after <see cref="Create"/>). The application owns it from now on.</summary>
    public bool Add(MuiWindow window) { return AddChild(window, false, 0); }

    internal override void DetachSystem(MuiObject child)
    {
        child.Set(Muia.WindowOpen, false);
        base.DetachSystem(child);
    }

    /// <summary>Ends <see cref="Run"/> (also from a notification handler).</summary>
    public void Quit()
    {
        _quit = true;
        if (IsCreated) DoMethod(MuiId.ReturnId, MuiId.ReturnIdQuit, 0);
    }

    /// <summary>True after <see cref="Quit"/> (until the next <see cref="Run"/>).</summary>
    public bool QuitRequested => _quit;

    /// <summary>
    /// Handles input until <see cref="Quit"/> (a window's close gadget quits unless the window has its own handler) or Ctrl-C. The task
    /// sleeps between events; notification handlers run from here. On the host it returns at once.
    /// </summary>
    public void Run()
    {
        if (!IsCreated) return;
#if AMIGA
        uint signals = 0;
        while (!_quit)
        {
            nuint id = DoMethod(MuiId.NewInput, (nuint)(&signals), 0);
            if ((uint)id == MuiId.ReturnIdQuit || _quit) break;
            if (signals != 0)
            {
                signals = Exec.Wait(signals | Exec.SigBreakCtrlC);
                if ((signals & Exec.SigBreakCtrlC) != 0) break;
            }
        }
#endif
        _quit = false;
    }

    /// <summary>Handles the input that is waiting, without sleeping. False once the application should quit.</summary>
    public bool Step()
    {
        if (!IsCreated) return false;
#if AMIGA
        uint signals = 0;
        nuint id = DoMethod(MuiId.NewInput, (nuint)(&signals), 0);
        if ((uint)id == MuiId.ReturnIdQuit) _quit = true;
#endif
        return !_quit;
    }
}

/// <summary>A MUI window: a title and one content object (usually a <see cref="MuiGroup"/>), which it owns.</summary>
public unsafe class MuiWindow : MuiObject
{
    private Action? _onClose;

    public MuiWindow(string title) : base("Window.mui")
    {
        Init(Muia.WindowTitle, title);
        WhenEquals(Muia.WindowCloseRequest, true, () => CloseRequested());
    }

    /// <summary>The window's content (set it once, before the application is created). The window owns it.</summary>
    public MuiObject? Content
    {
        get
        {
            for (MuiObject? c = FirstChild; c != null; c = c.NextSibling) if (c.ViaInit) return c;
            return null;
        }
        set
        {
            if (value != null && Content == null) AddChild(value, true, Muia.WindowRootObject.Tag);
        }
    }

    /// <summary>A number that names the window in MUI's saved positions (0: none).</summary>
    public int Id { set { if (!IsCreated) Init(Muia.WindowId, value); } }

    /// <summary>Runs <paramref name="handler"/> instead of quitting when the close gadget is clicked.</summary>
    public void OnCloseRequest([Owned] Action handler)
    {
        Heap.Free(ref _onClose);
        _onClose = handler;
    }

    private void CloseRequested()
    {
        Action? h = _onClose;
        if (h != null) { h(); return; }
        MuiApplication? app = Application;
        if (app != null) app.Quit();
    }

    internal override void DisposeOwned()
    {
        base.DisposeOwned();
        Heap.Free(ref _onClose);
    }

    /// <summary>Opens the window (the application must be created). False if MUI could not open it.</summary>
    public bool Open()
    {
        if (!IsCreated || MuiParent == null) return false;
        Set(Muia.WindowOpen, true);
        return Get(Muia.WindowOpen);
    }

    public void Close() { if (IsCreated) Set(Muia.WindowOpen, false); }

    public bool IsOpen => IsCreated && Get(Muia.WindowOpen);

#if !AMIGA
    /// <summary>Host: the close gadget is clicked.</summary>
    public void HostCloseRequest() { HostChanged(Muia.WindowCloseRequest.Tag, 1); }
#endif
}

/// <summary>A group: children one under the other, side by side (<see cref="Horizontal"/>) or in <see cref="Columns"/>. It owns its children.</summary>
public unsafe class MuiGroup : MuiObject
{
    public MuiGroup() : base("Group.mui") { }

    /// <summary>Children side by side (set before the group is made).</summary>
    public bool Horizontal { set { if (!IsCreated) Init(Muia.GroupHoriz, value); } }

    /// <summary>Children in this many columns, row by row (set before the group is made).</summary>
    public int Columns { set { if (!IsCreated) Init(Muia.GroupColumns, value); } }

    /// <summary>A <see cref="MuiFrame"/> value (set before the group is made).</summary>
    public int Frame { set { if (!IsCreated) Init(Muia.Frame, value); } }

    /// <summary>A title in the frame (set before the group is made).</summary>
    public string? Title { set { if (!IsCreated) Init(Muia.FrameTitle, value); } }

    /// <summary>Adds a child (not a window or application). The group owns it from now on. False if it has an owner already.</summary>
    public bool Add(MuiObject child)
    {
        if (child is MuiWindow || child is MuiApplication) return false;
        return AddChild(child, false, 0);
    }

    // A group that is shown is told before and after its children change.
    internal override void AttachSystem(MuiObject child)
    {
        bool shown = InOpenWindow();
        if (shown) DoMethod(MuiId.GroupInitChange, 0, 0);
        base.AttachSystem(child);
        if (shown) DoMethod(MuiId.GroupExitChange, 0, 0);
    }

    internal override void DetachSystem(MuiObject child)
    {
        bool shown = InOpenWindow();
        if (shown) DoMethod(MuiId.GroupInitChange, 0, 0);
        base.DetachSystem(child);
        if (shown) DoMethod(MuiId.GroupExitChange, 0, 0);
    }
}

/// <summary>A line of text (MUI copies it).</summary>
public unsafe class MuiText : MuiObject
{
    public MuiText(string? text) : base("Text.mui") { Init(Muia.TextContents, text); }

    public string? Text
    {
        get => Get(Muia.TextContents);
        set { if (IsCreated) Set(Muia.TextContents, value); else Init(Muia.TextContents, value); }
    }

    /// <summary>A <see cref="MuiFrame"/> value (set before the object is made).</summary>
    public int Frame { set { if (!IsCreated) Init(Muia.Frame, value); } }
}

/// <summary>A label for a gadget next to it (MUI_MakeObject label).</summary>
public unsafe class MuiLabel : MuiObject
{
    public MuiLabel(string label) : base(MuiId.MakeLabel, label, 0) { }
}

/// <summary>A push button (MUI_MakeObject button); an underscore marks the key that presses it (<c>"_Hello"</c>).</summary>
public unsafe class MuiButton : MuiObject
{
    public MuiButton(string label) : base(MuiId.MakeButton, label, 0) { }

    /// <summary>Runs <paramref name="handler"/> each time the button is pressed and released.</summary>
    public bool OnPressed([Owned] Action handler) { return WhenEquals(Muia.Pressed, false, handler); }

#if !AMIGA
    /// <summary>Host: the button is clicked.</summary>
    public void HostPress() { HostChanged(Muia.Pressed.Tag, 0); }
#endif
}

/// <summary>A check mark (MUI_MakeObject checkmark).</summary>
public unsafe class MuiCheckMark : MuiObject
{
    public MuiCheckMark(bool selected, string? label) : base(MuiId.MakeCheckmark, label, 0) { Init(Muia.Selected, selected); }

    public bool Selected
    {
        get => Get(Muia.Selected);
        set { if (IsCreated) Set(Muia.Selected, value); else Init(Muia.Selected, value); }
    }

    public bool OnToggled([Owned] Action<bool> handler) { return WhenChanged(Muia.Selected, handler); }
}

/// <summary>A text entry field.</summary>
public unsafe class MuiString : MuiObject
{
    public MuiString(string? contents, int maxLen) : base("String.mui")
    {
        Init(Muia.Frame, MuiFrame.String);
        Init(Muia.StringMaxLen, maxLen);
        Init(Muia.StringContents, contents);
    }

    public string? Contents
    {
        get => Get(Muia.StringContents);
        set { if (IsCreated) Set(Muia.StringContents, value); else Init(Muia.StringContents, value); }
    }

    /// <summary>Each change of the text (typing, or the program setting it).</summary>
    public bool OnChanged([Owned] Action<string?> handler) { return WhenChanged(Muia.StringContents, handler); }

    /// <summary>Return was pressed in the field.</summary>
    public bool OnAcknowledged([Owned] Action<string?> handler) { return WhenChanged(Muia.StringAcknowledge, handler); }

#if !AMIGA
    /// <summary>Host: the user types <paramref name="text"/> and presses Return.</summary>
    public void HostType(string text)
    {
        Set(Muia.StringContents, text);
        HostChanged(Muia.StringAcknowledge.Tag, (nuint)Cast.StringToPtr(text));
    }
#endif
}

/// <summary>A cycle gadget: one of a fixed list of choices.</summary>
public unsafe class MuiCycle : MuiObject
{
    private byte* _entries;   // the null-ended array of label pointers MUI reads (the labels are copies this object keeps)

    public MuiCycle(Span<string> entries) : base("Cycle.mui")
    {
        int n = entries.Length;
        _entries = Mem.AllocFast((uint)((n + 1) * sizeof(nuint)));
        if (_entries == null) { FailInit(); return; }
        nuint* p = (nuint*)_entries;
        for (int i = 0; i < n; i++)
        {
            byte* copy = KeepString(entries[i], 0);
            p[i] = (nuint)copy;
        }
        p[n] = 0;
        Init(Muia.CycleEntries, (void*)_entries);
    }

    public int Active
    {
        get => Get(Muia.CycleActive);
        set { if (IsCreated) Set(Muia.CycleActive, value); else Init(Muia.CycleActive, value); }
    }

    public bool OnActiveChanged([Owned] Action<int> handler) { return WhenChanged(Muia.CycleActive, handler); }

    internal override void DisposeOwned()
    {
        base.DisposeOwned();
        if (_entries != null) { Mem.Free(_entries); _entries = null; }
    }
}

/// <summary>A horizontal slider for a number from <c>min</c> to <c>max</c>.</summary>
public unsafe class MuiSlider : MuiObject
{
    public MuiSlider(int min, int max, int value) : base("Slider.mui")
    {
        Init(Muia.SliderHoriz, true);
        Init(Muia.NumericMin, min);
        Init(Muia.NumericMax, max);
        Init(Muia.NumericValue, value);
    }

    public int Value
    {
        get => Get(Muia.NumericValue);
        set { if (IsCreated) Set(Muia.NumericValue, value); else Init(Muia.NumericValue, value); }
    }

    public bool OnValueChanged([Owned] Action<int> handler) { return WhenChanged(Muia.NumericValue, handler); }
}

/// <summary>A horizontal bar that shows how far something has got (<see cref="Current"/> of <c>max</c>).</summary>
public unsafe class MuiGauge : MuiObject
{
    public MuiGauge(int max) : base("Gauge.mui")
    {
        Init(Muia.Frame, MuiFrame.Gauge);
        Init(Muia.GaugeHoriz, true);
        Init(Muia.GaugeMax, max);
    }

    public int Current
    {
        get => Get(Muia.GaugeCurrent);
        set { if (IsCreated) Set(Muia.GaugeCurrent, value); else Init(Muia.GaugeCurrent, value); }
    }

    /// <summary>A text inside the bar; <c>%ld</c> shows <see cref="Current"/>.</summary>
    public string? InfoText { set { if (IsCreated) Set(Muia.GaugeInfoText, value); else Init(Muia.GaugeInfoText, value); } }
}

/// <summary>Empty space that takes what is left over (Rectangle.mui), or a fixed gap (<see cref="Gap"/>).</summary>
public unsafe class MuiRectangle : MuiObject
{
    public MuiRectangle() : base("Rectangle.mui") { }

    private MuiRectangle(int makeType, int space) : base(makeType, null, 0) { SetMakeNumber((nuint)(nint)space); }

    /// <summary>A gap of <paramref name="space"/> pixels side to side (MUI_MakeObject HSpace; 0 grows as needed).</summary>
    public static MuiRectangle Gap(int space) { return new MuiRectangle(MuiId.MakeHSpace, space); }
}

/// <summary>
/// A list of strings in a scrolling view (Listview.mui with a List.mui inside; MUI copies the strings). The list belongs to the
/// listview; <see cref="Active"/> is the selected entry or -1.
/// </summary>
public unsafe class MuiListview : MuiObject
{
    private MuiObject _list;
#if !AMIGA
    private string[] _hostItems;
    private int _hostCount;
#endif

    public MuiListview() : base("Listview.mui")
    {
        _list = new MuiObject("List.mui");
        _list.Init(Muia.Frame, MuiFrame.InputList);
        _list.InitRaw(Muia.ListConstructHook.Tag, MuiId.ListHookString);
        _list.InitRaw(Muia.ListDestructHook.Tag, MuiId.ListHookString);
        AddChild(_list, true, Muia.ListviewList.Tag);
#if !AMIGA
        _hostItems = new string[8];
#endif
    }

    /// <summary>Adds a line at the end (copied). False if the listview is not made yet.</summary>
    public bool Add(string text)
    {
        if (!_list.IsCreated) return false;
#if AMIGA
        int bottom = MuiId.ListInsertBottom;
        _list.DoMethod(MuiId.ListInsertSingle, (nuint)Cast.StringToPtr(text), (nuint)(nint)bottom);
#else
        if (_hostCount == _hostItems.Length) Array.Resize(ref _hostItems, _hostCount * 2);
        _hostItems[_hostCount++] = text;
        _list.SetRaw(Muia.ListEntries.Tag, (nuint)_hostCount);
#endif
        return true;
    }

    /// <summary>Removes every line.</summary>
    public void Clear()
    {
        if (!_list.IsCreated) return;
#if AMIGA
        _list.DoMethod(MuiId.ListClear, 0, 0);
#else
        _hostCount = 0;
        _list.SetRaw(Muia.ListEntries.Tag, 0);
        _list.Set(Muia.ListActive, MuiId.ListActiveOff);
#endif
    }

    public int Count => _list.Get(Muia.ListEntries);

    public int Active
    {
        get => _list.Get(Muia.ListActive);
        set { if (_list.IsCreated) _list.Set(Muia.ListActive, value); }
    }

    /// <summary>Line <paramref name="index"/> (the list's own copy, valid until the list changes), or null.</summary>
    public string? Entry(int index)
    {
        if (!_list.IsCreated || index < 0) return null;
#if AMIGA
        nuint slot = 0;
        _list.DoMethod(MuiId.ListGetEntry, (nuint)(nint)index, (nuint)(&slot));
        return slot == 0 ? null : Cast.PtrToString((byte*)slot);
#else
        return index < _hostCount ? _hostItems[index] : null;
#endif
    }

    public bool OnActiveChanged([Owned] Action<int> handler) { return _list.WhenChanged(Muia.ListActive, handler); }
}
#endif
