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
    [AmigaLib("muimaster", -102), Since(19)] public static extern void MUI_Redraw(void* obj, uint flags);
    [AmigaLib("muimaster", -108), Since(19)] public static extern void* MUI_CreateCustomClass(void* libraryBase, byte* superName, void* superMcc, int dataSize, void* dispatcher);
    [AmigaLib("muimaster", -114), Since(19)] public static extern int MUI_DeleteCustomClass(void* mcc);
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
    public const uint OmNew = 0x101, OmDispose = 0x102, OmSet = 0x103, OmGet = 0x104;
    // Methods a custom class's dispatcher sees (Area.mui), and MUIM_Draw's flags (MADF_DRAWOBJECT, MADF_DRAWUPDATE).
    public const uint AskMinMax = 0x80423874, Draw = 0x80426f3f, Setup = 0x80428354, Cleanup = 0x8042d985, Show = 0x8042cc84, Hide = 0x8042f20f;
    public const uint DrawObject = 1, DrawUpdate = 2;
    // Input for custom classes: MUIM_HandleEvent, the window's event handler list, and MUI_EventHandlerRC_Eat.
    public const uint HandleEvent = 0x80426d66, WindowAddEventHandler = 0x804203b7, WindowRemEventHandler = 0x8042679e;
    public const uint EventEat = 1;
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
        MuiCustomClasses.DeleteUnused();
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

    /// <summary>Custom classes (for <see cref="MuiArea"/> and <see cref="MuiCustomGroup"/>) made and not yet deleted: at most one per MUI superclass.</summary>
    public static int CustomClasses => MuiCustomClasses.Live;

    /// <summary>Custom classes made so far (a class is made with the first object of it and deleted after its last one).</summary>
    public static int CustomClassesMade => MuiCustomClasses.Made;

    /// <summary>Objects of the custom classes that MUI made (OM_NEW) and has not yet disposed (OM_DISPOSE), as the dispatcher saw them.</summary>
    public static int CustomObjects => MuiCustomClasses.Objects;

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
    internal int CustomKind;          // MuiCustomClasses.Area or .Group for a custom class (MuiArea, MuiCustomGroup), 0 otherwise
    private bool _setUp;              // between MUIM_Setup and MUIM_Cleanup
    private bool _shown;              // between MUIM_Show and MUIM_Hide
    private uint _wantEvents;         // IDCMP classes OnEvent wants (WantEvents), 0 for none
#if AMIGA
    private void* _renderInfo;        // MUI's render info (from MUIM_Setup)
    private byte* _ehn;               // struct MUI_EventHandlerNode, one from MUIM_Setup to MUIM_Cleanup (freed only at MUIM_Cleanup)
    private void* _ehnWindow;         // the window object it is in, while it is in the window's list (null when not added)
    private void* _ehnClass;          // the custom class and object it calls (ehn_Class, ehn_Object)
    private void* _ehnObject;
#else
    private MuiMinMax _hostSize;      // host: what OnAskMinMax asked for (the fake's layout)
#endif

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
        if (CustomKind != 0) return MuiCustomClasses.NewObject(this, CustomKind, tags);
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
#if !AMIGA
        if (CustomKind != 0) MuiCustomClasses.HostNew(CustomKind);
#endif
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
#if !AMIGA
        // Host: MUI would hide, clean up and dispose a custom object here (inside the dispose of it or of its tree).
        if (CustomKind != 0)
        {
            HostShow(false);
            MuiCustomClasses.HostDisposed(CustomKind);
        }
#endif
        Attached = false;
        MarkGone();
        Mui.LiveObjects--;
        MuiCustomClasses.DeleteUnused();    // a class goes once its last object is gone
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
        if (CustomKind != 0) CallSet(tag, value);    // a custom class sees OM_SET before its superclass notifies
        for (MuiNotification? n = _notes; n != null; n = n.Next)
            if (n.Attr == tag && (n.Trigger == MuiId.EveryTime || n.Trigger == value)) n.Fire(value);
    }

    internal override void HostCreated(nuint* tags)
    {
        if (CustomKind == 0) return;
        for (int i = 0; tags[i] != 0; i += 2) CallSet((uint)tags[i], tags[i + 1]);   // OM_NEW: the starting attributes
    }

    internal override bool HostGet(uint tag, out nuint value)
    {
        if (CustomKind != 0 && CallGet(tag, out value)) return true;
        value = 0;     // an OnGet that answers false may have written to it
        return false;
    }

    internal override bool HostMethod(nuint* msg, out nuint result)
    {
        result = 0;
        return CustomKind != 0 && CallMethod((uint)msg[0], msg, out result);
    }

    // Host: what MUI does when the window opens (setup, size, show, draw) or closes (hide, cleanup), for this object and its children.
    internal void HostShow(bool on)
    {
        for (MuiObject? c = FirstChild; c != null; c = c.NextSibling) c.HostShow(on);
        if (CustomKind == 0) return;
        if (on && !_shown)
        {
            if (!_setUp)
            {
                Mui.HandlerDepth++;
                _setUp = OnSetup();
                Mui.HandlerDepth--;
                if (!_setUp) return;
            }
            MuiMinMax size = new MuiMinMax();
            Mui.HandlerDepth++;
            OnAskMinMax(ref size);
            Mui.HandlerDepth--;
            _hostSize = size;
            _shown = true;
            HostDraw(false);
        }
        else if (!on && _setUp)
        {
            _shown = false;
            _setUp = false;
            Mui.HandlerDepth++;
            OnCleanup();
            Mui.HandlerDepth--;
        }
    }

    /// <summary>
    /// Host: an input event as MUI would send it (MUIM_HandleEvent) to a shown custom object that wants this IDCMP class: x and y are
    /// relative to the contents box. True if <c>OnEvent</c> ate it; false if it was not delivered or not eaten.
    /// </summary>
    public bool HostEvent(uint idcmpClass, int code, int qualifier, int x, int y)
    {
        if (CustomKind == 0 || !_shown || (_wantEvents & idcmpClass) == 0) return false;
        MuiEvent e = new MuiEvent();
        e.Class = idcmpClass;
        e.Code = code;
        e.Qualifier = qualifier;
        e.MouseX = x;
        e.MouseY = y;
        e.Width = _hostSize.DefWidth > _hostSize.MinWidth ? _hostSize.DefWidth : _hostSize.MinWidth;
        e.Height = _hostSize.DefHeight > _hostSize.MinHeight ? _hostSize.DefHeight : _hostSize.MinHeight;
        e.MuiKey = -1;
        return CallEvent(e);
    }

    private void HostDraw(bool update)
    {
        int w = _hostSize.DefWidth > _hostSize.MinWidth ? _hostSize.DefWidth : _hostSize.MinWidth;
        int h = _hostSize.DefHeight > _hostSize.MinHeight ? _hostSize.DefHeight : _hostSize.MinHeight;
        MuiRender r = new MuiRender(0, 0, w, h, update);
        Mui.HandlerDepth++;
        OnDraw(r);
        Mui.HandlerDepth--;
    }
#endif

    // ---------------------------------------------------------- custom classes (MuiArea, MuiCustomGroup)
    // The dispatcher of MuiCustomClasses calls these for an object of a custom class; the protected virtual members are what the
    // program overrides (in a class derived from MuiArea or MuiCustomGroup, the only MUI classes a program can derive from).

    /// <summary>MUIM_Setup: the object is about to be shown in a window (MUI's render info, pens and fonts are there). False stops the window opening.</summary>
    protected virtual bool OnSetup() { return true; }

    /// <summary>MUIM_Cleanup: the window closed (the opposite of <see cref="OnSetup"/>).</summary>
    protected virtual void OnCleanup() { }

    /// <summary>
    /// MUIM_AskMinMax: the size of the object's contents, in pixels. Each number is added to what the superclass asks for (its frame and
    /// spacing; a group's children), so the defaults (all 0) leave the superclass's sizes; <see cref="MuiMinMax.Unlimited"/> as a maximum
    /// lets the object grow.
    /// </summary>
    protected virtual void OnAskMinMax(ref MuiMinMax size) { }

    /// <summary>MUIM_Draw: draw the contents (after the superclass drew the frame and background, and a group its children).</summary>
    protected virtual void OnDraw(MuiRender r) { }

    /// <summary>OM_SET (and the starting attributes, at OM_NEW): every attribute that is set, the object's own ones and MUI's. The superclass sees them too.</summary>
    protected virtual void OnSet(uint attr, nuint value) { }

    /// <summary>OM_GET: true and the value for the object's own attributes; false lets the superclass answer.</summary>
    protected virtual bool OnGet(uint attr, out nuint value) { value = 0; return false; }

    /// <summary>Any other method (the object's own ones, sent with <see cref="BoopsiObject.DoMethod"/>): true if handled, with its result.</summary>
    protected virtual bool OnMethod(uint methodId, nuint* msg, out nuint result) { result = 0; return false; }

    /// <summary>
    /// MUIM_HandleEvent: an input event of a class given to <see cref="WantEvents"/>, in the object's window while the object is shown
    /// (every such event, wherever the mouse is: see <see cref="MuiEvent.Inside"/>). True eats it (no other handler sees it).
    /// </summary>
    protected virtual bool OnEvent(MuiEvent e) { return false; }

    /// <summary>
    /// The IDCMP classes <see cref="OnEvent"/> wants (<see cref="MuiEvent.MouseButtons"/>, <see cref="MuiEvent.MouseMove"/>,
    /// <see cref="MuiEvent.RawKey"/>, or'ed; 0 for none). One MUI event handler node per object lives from MUIM_Setup to MUIM_Cleanup; it
    /// is in the window's list while the set is not 0. Changed while the object is set up (also from inside <c>OnEvent</c>, this object's
    /// or another one's), the node is taken out of the list, given the new set and put back: ask for <see cref="MuiEvent.MouseMove"/>
    /// only while a drag goes on, as every mouse move in the window costs a call.
    /// </summary>
    protected void WantEvents(uint idcmpClasses)
    {
        if (idcmpClasses == _wantEvents) return;
        _wantEvents = idcmpClasses;
#if AMIGA
        if (_setUp) UpdateHandler();
#endif
    }

    /// <summary>The IDCMP classes given to <see cref="WantEvents"/>.</summary>
    protected uint WantedEvents => _wantEvents;

    internal bool CallEvent(MuiEvent e)
    {
        Mui.HandlerDepth++;
        bool eat = OnEvent(e);
        Mui.HandlerDepth--;
        return eat;
    }

    /// <summary>True while the object is shown in an open window (between MUIM_Show and MUIM_Hide): it can draw then.</summary>
    protected bool IsShown => _shown;

    /// <summary>Draws the object again (MUI_Redraw with MADF_DRAWOBJECT) if it is shown; nothing otherwise.</summary>
    protected void Redraw()
    {
        if (!_shown || CustomKind == 0) return;
#if AMIGA
        if (Address != null) MuiMaster.MUI_Redraw(Address, MuiId.DrawObject);
#else
        HostDraw(false);
#endif
    }

    internal void CallSet(uint tag, nuint value)
    {
        Mui.HandlerDepth++;
        OnSet(tag, value);
        Mui.HandlerDepth--;
    }

    internal bool CallGet(uint tag, out nuint value)
    {
        Mui.HandlerDepth++;
        bool ok = OnGet(tag, out value);
        Mui.HandlerDepth--;
        return ok;
    }

    internal bool CallMethod(uint id, nuint* msg, out nuint result)
    {
        Mui.HandlerDepth++;
        bool ok = OnMethod(id, msg, out result);
        Mui.HandlerDepth--;
        return ok;
    }

#if AMIGA
    // The dispatcher's work for a live object of a custom class (OM_NEW and OM_DISPOSE are handled in MuiCustomClasses.Dispatch).
    internal nuint CustomDispatch(void* cls, void* obj, uint id, nuint* m)
    {
        nuint r = 0;
        if (id == MuiId.Setup)
        {
            r = BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
            if (r == 0) return 0;
            _renderInfo = (void*)m[1];
            MuiCustomClasses.FindAreaData(obj, _renderInfo);
            Mui.HandlerDepth++;
            bool ok = OnSetup();
            Mui.HandlerDepth--;
            if (!ok)
            {
                MuiMsg c = new MuiMsg();
                c.Put(0, MuiId.Cleanup);
                BoopsiNative.es_dosupermethod(cls, obj, (void*)c.Ptr());
                _renderInfo = null;
                return 0;
            }
            _setUp = true;
            _ehnClass = cls;
            _ehnObject = obj;
            AllocHandler();
            UpdateHandler();
            return r;
        }
        if (id == MuiId.Cleanup)
        {
            FreeHandler();
            if (_setUp)
            {
                Mui.HandlerDepth++;
                OnCleanup();
                Mui.HandlerDepth--;
            }
            _setUp = false;
            _shown = false;
            r = BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
            _renderInfo = null;
            return r;
        }
        if (id == MuiId.Show)
        {
            r = BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
            if (r != 0) _shown = true;
            return r;
        }
        if (id == MuiId.Hide)
        {
            _shown = false;
            return BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
        }
        if (id == MuiId.AskMinMax)
        {
            r = BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
            MuiMinMax size = new MuiMinMax();
            Mui.HandlerDepth++;
            OnAskMinMax(ref size);
            Mui.HandlerDepth--;
            size.AddTo((short*)m[1]);
            return r;
        }
        if (id == MuiId.Draw)
        {
            r = BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
            uint flags = (uint)m[1];
            if ((flags & (MuiId.DrawObject | MuiId.DrawUpdate)) != 0 && _renderInfo != null && MuiCustomClasses.AreaData > 0)
            {
                MuiRender rd = new MuiRender(obj, _renderInfo, (flags & MuiId.DrawObject) == 0);
                Mui.HandlerDepth++;
                OnDraw(rd);
                Mui.HandlerDepth--;
            }
            return r;
        }
        if (id == MuiId.HandleEvent && _ehn != null)
        {
            // MUIM_HandleEvent { imsg, muikey } for our node (the classes we asked for); anything else goes to the superclass.
            byte* im = (byte*)m[1];
            uint cls0 = im == null ? 0 : *(uint*)(im + Layout.MsgClass);
            if (im != null && (cls0 & _wantEvents) == 0) return BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
            if (!_shown) return 0;          // set up but hidden (a page not shown): its box is not valid, nothing to hit
            MuiEvent e = new MuiEvent();
            e.Class = cls0;
            e.MuiKey = (int)(nint)m[2];
            if (im != null)
            {
                e.Code = *(ushort*)(im + Layout.MsgCode);
                e.Qualifier = *(ushort*)(im + Layout.MsgQualifier);
                int left = 0, top = 0, width = 0, height = 0;
                MuiCustomClasses.ContentBox(obj, out left, out top, out width, out height);
                e.MouseX = *(short*)(im + Layout.MsgMouseX) - left;
                e.MouseY = *(short*)(im + Layout.MsgMouseY) - top;
                e.Width = width;
                e.Height = height;
            }
            return CallEvent(e) ? MuiId.EventEat : 0;
        }
        if (id == MuiId.OmSet)
        {
            MuiCustomClasses.SetEach(this, (nuint*)m[1]);
            return BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
        }
        if (id == MuiId.OmGet)
        {
            nuint v = 0;
            if (CallGet((uint)m[1], out v))
            {
                *(nuint*)m[2] = v;
                return 1;
            }
            return BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
        }
        nuint result = 0;
        if (CallMethod(id, m, out result)) return result;
        return BoopsiNative.es_dosupermethod(cls, obj, (void*)m);
    }

    // The object's one event handler node, allocated at MUIM_Setup (the render info names the window) and freed only at MUIM_Cleanup,
    // so a node is never freed while MUI may be going through the window's handler list (an OnEvent of any object can call WantEvents).
    private void AllocHandler()
    {
        if (_ehn != null || _renderInfo == null || _ehnObject == null) return;
        byte* n = Mem.AllocFast((uint)Layout.EhnSize);   // cleared: priority and flags 0
        if (n == null) return;
        *(void**)(n + Layout.EhnObject) = _ehnObject;
        *(void**)(n + Layout.EhnClass) = _ehnClass;   // MUI sends MUIM_HandleEvent to this class (CoerceMethod)
        _ehn = n;
    }

    // The node takes the current set: out of the window's list if it is in it (MUIM_Window_RemEventHandler), ehn_Events changed in
    // place, back in (MUIM_Window_AddEventHandler) unless the set is 0. The same node every time.
    private void UpdateHandler()
    {
        if (_ehn == null) return;
        if (_ehnWindow != null)
        {
            MuiMsg rem = new MuiMsg();
            rem.Put(0, MuiId.WindowRemEventHandler);
            rem.Put(1, (nuint)_ehn);
            BoopsiNative.es_domethod(_ehnWindow, (void*)rem.Ptr());
            _ehnWindow = null;
        }
        *(uint*)(_ehn + Layout.EhnEvents) = _wantEvents;
        if (_wantEvents == 0 || _renderInfo == null) return;
        void* win = *(void**)_renderInfo;   // struct MUI_RenderInfo starts with the window object
        if (win == null) return;
        MuiMsg add = new MuiMsg();
        add.Put(0, MuiId.WindowAddEventHandler);
        add.Put(1, (nuint)_ehn);
        BoopsiNative.es_domethod(win, (void*)add.Ptr());
        _ehnWindow = win;
    }

    // MUIM_Cleanup (before it reaches the superclass): the node out of the window's list and freed.
    private void FreeHandler()
    {
        if (_ehn == null) return;
        if (_ehnWindow != null)
        {
            MuiMsg msg = new MuiMsg();
            msg.Put(0, MuiId.WindowRemEventHandler);
            msg.Put(1, (nuint)_ehn);
            BoopsiNative.es_domethod(_ehnWindow, (void*)msg.Ptr());
        }
        Mem.Free(_ehn);
        _ehn = null;
        _ehnWindow = null;
    }

    // OM_DISPOSE reached the dispatcher: the system object is going.
    internal void CustomGone()
    {
        // MUIM_Cleanup removed the node. Should an object go without one, its window is no longer safe to call: only the memory goes.
        if (_ehn != null) Mem.Free(_ehn);
        _ehn = null;
        _ehnWindow = null;
        _ehnObject = null;
        _setUp = false;
        _shown = false;
        _renderInfo = null;
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
#if !AMIGA
        HostShow(true);
#endif
        return Get(Muia.WindowOpen);
    }

    public void Close()
    {
        if (!IsCreated) return;
        Set(Muia.WindowOpen, false);
#if !AMIGA
        HostShow(false);
#endif
    }

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

    // A custom class on Group.mui (MuiCustomGroup).
    internal MuiGroup(int customKind) : base("Group.mui") { CustomKind = customKind; }

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
#if !AMIGA
        if (shown) child.HostShow(true);
#endif
    }

    internal override void DetachSystem(MuiObject child)
    {
        bool shown = InOpenWindow();
#if !AMIGA
        if (shown) child.HostShow(false);
#endif
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
