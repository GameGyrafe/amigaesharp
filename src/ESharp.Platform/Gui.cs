// Amiga-only (GadTools, ASL, iffparse, diskfont, lowlevel): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// gadtools.library bindings (v36 and up; <c>GT_GetGadgetAttrsA</c> is v39). The library is in ROM on Kickstart 3.1. It is opened by
/// <see cref="GtWindow"/>, which calls these. Vectors from the classic fd file (checked with tools/BindGen).
/// </summary>
[LibraryBase("gadtools", "GadToolsBase", "struct Library")]
public static unsafe class GadTools
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    [AmigaLib("gadtools", -30), Since(36)] public static extern void* CreateGadgetA(uint kind, void* previous, void* newGadget, void* tags);
    [AmigaLib("gadtools", -36), Since(36)] public static extern void FreeGadgets(void* gadgetList);
    [AmigaLib("gadtools", -42), Since(36)] public static extern void GT_SetGadgetAttrsA(void* gadget, void* window, void* requester, void* tags);
    [AmigaLib("gadtools", -72), Since(36)] public static extern void* GT_GetIMsg(void* port);
    [AmigaLib("gadtools", -78), Since(36)] public static extern void GT_ReplyIMsg(void* message);
    [AmigaLib("gadtools", -84), Since(36)] public static extern void GT_RefreshWindow(void* window, void* requester);
    [AmigaLib("gadtools", -90), Since(36)] public static extern void GT_BeginRefresh(void* window);
    [AmigaLib("gadtools", -96), Since(36)] public static extern void GT_EndRefresh(void* window, int complete);
    /// <summary>Starts a gadget list: the pointer receives the list head, and the result is the "previous gadget" for the first <see cref="CreateGadgetA"/>.</summary>
    [AmigaLib("gadtools", -114), Since(36)] public static extern void* CreateContext(void** gadgetListPointer);
    [AmigaLib("gadtools", -126), Since(36)] public static extern void* GetVisualInfoA(void* screen, void* tags);
    [AmigaLib("gadtools", -132), Since(36)] public static extern void FreeVisualInfo(void* visualInfo);
    /// <summary>Reads gadget attributes; each tag's data is the address of where to store the value. Returns the number of tags handled.</summary>
    [AmigaLib("gadtools", -174), Since(39)] public static extern int GT_GetGadgetAttrsA(void* gadget, void* window, void* requester, void* tags);
}
#endif

/// <summary>What happened in a <see cref="GtWindow"/>.</summary>
public enum GuiEventKind
{
    /// <summary>The close gadget was clicked (or <see cref="GtWindow.Wait"/> was called on a window that is not open).</summary>
    Close,
    /// <summary>A <see cref="GtButton"/> was released over.</summary>
    Clicked,
    /// <summary>A <see cref="GtString"/> was accepted (Return pressed in it).</summary>
    Changed,
}

/// <summary>An event from <see cref="GtWindow.Wait"/>: its kind and the gadget it came from (null for <see cref="GuiEventKind.Close"/>).</summary>
public struct GuiEvent
{
    public GuiEventKind Kind;
    public GtGadget? Source;

    public GuiEvent(GuiEventKind kind, GtGadget? source)
    {
        Kind = kind;
        Source = source;
    }
}

/// <summary>
/// Handler for <see cref="GtWindow.Run"/>: implement it in a class of the program. An interface value, not a closure, so it works
/// without heap closures.
/// </summary>
public interface IGuiHandler
{
    /// <summary>A button was clicked.</summary>
    void Clicked(GtGadget gadget);

    /// <summary>A string gadget was accepted with Return; read its <see cref="GtString.Text"/>.</summary>
    void Changed(GtGadget gadget);
}

/// <summary>Copies of strings that gadgets and windows keep (the system points at them until the window closes).</summary>
internal static unsafe class GuiText
{
#if AMIGA
    internal static byte* Dup(string? s)
    {
        if (s == null) return null;
        byte* src = Cast.StringToPtr(s);
        int n = 0;
        while (src[n] != 0) n++;
        byte* d = Mem.AllocFast((uint)(n + 1));
        if (d == null) return null;
        for (int i = 0; i <= n; i++) d[i] = src[i];
        return d;
    }

    internal static void Free(byte* p) { Mem.Free(p); }
#else
    // The host's Cast keeps one native copy per distinct string, so a "copy" is the same pointer and needs no freeing.
    internal static byte* Dup(string? s) { return s == null ? null : Cast.StringToPtr(s); }

    internal static void Free(byte* p) { }
#endif
}

/// <summary>
/// A gadget of a <see cref="GtWindow"/>. Add it with <see cref="GtWindow.Add"/>; the window owns it from then on and frees it in
/// <see cref="GtWindow.Close"/>, so never free it yourself. <see cref="Label"/> is shown to the left of the gadget (on the button
/// itself for a <see cref="GtButton"/>); set it before the window opens.
/// </summary>
public unsafe class GtGadget
{
    public string? Label { get; set; }

    internal int KindId;           // 0 button, 1 string, 2 text
    internal GtGadget? Next;
    internal byte* LabelCopy;
    internal byte* TextCopy;       // the text set last (a copy this gadget owns)
#if AMIGA
    internal void* Handle;         // struct Gadget *, while the window is open
    internal void* WindowHandle;   // struct Window *, while the window is open
#endif

    /// <summary>Stores a new copy of the text and, while the window is open, points the gadget at it before the old copy is freed.
    /// If the copy cannot be made, the old text stays in place.</summary>
    internal void SetTextCopy(string? s)
    {
        byte* old = TextCopy;
        byte* fresh = GuiText.Dup(s);
        if (fresh == null && s != null) return;       // out of memory: keep the old copy, the gadget still points at it
#if AMIGA
        if (Handle != null)
        {
            if (fresh == null) fresh = GuiText.Dup("");
            if (fresh == null) return;
            TagList w = new TagList();
            w.Add(KindId == 1 ? Layout.GtstString : Layout.GttxText, fresh);
            nuint* t = w.End();
            if (t != null) GadTools.GT_SetGadgetAttrsA(Handle, WindowHandle, null, t);
        }
#endif
        TextCopy = fresh;
        if (old != null && old != fresh) GuiText.Free(old);
    }

    internal void FreeCopies()
    {
        if (LabelCopy != null) GuiText.Free(LabelCopy);
        if (TextCopy != null) GuiText.Free(TextCopy);
        LabelCopy = null;
        TextCopy = null;
    }
}

/// <summary>A push button. A click is a <see cref="GuiEventKind.Clicked"/> event.</summary>
public unsafe class GtButton : GtGadget
{
    public GtButton() { KindId = 0; }
}

/// <summary>
/// A text entry gadget. <see cref="Text"/> is what is in it: set it before or after the window opens, read it when
/// <see cref="GuiEventKind.Changed"/> arrives or at any time. <see cref="MaxChars"/> (default 40) is the longest text it takes.
/// </summary>
public unsafe class GtString : GtGadget
{
    public int MaxChars { get; set; }

    public GtString()
    {
        KindId = 1;
        MaxChars = 40;
    }

    /// <summary>
    /// The text in the gadget. Read from the live gadget while the window is open, so it includes what the user typed; the string
    /// stays valid until the gadget is edited or its text set again (copy it if it must live longer).
    /// </summary>
    public string? Text
    {
        get
        {
#if AMIGA
            if (Handle != null)
            {
                nuint[] result = new nuint[1];
                nuint[] tags = new nuint[4];
                fixed (nuint* r = result)
                fixed (nuint* t = tags)
                {
                    TagWriter w = new TagWriter(t, 4);
                    w.Add(Layout.GtstString, (void*)r);
                    if (w.End() && GadTools.GT_GetGadgetAttrsA(Handle, WindowHandle, null, t) != 0 && r[0] != 0)
                        return Cast.PtrToString((byte*)r[0]);
                }
            }
#endif
            if (TextCopy == null) return null;
            return Cast.PtrToString(TextCopy);
        }
        set
        {
            SetTextCopy(value);
        }
    }
}

/// <summary>A read-only text field. <see cref="Text"/> is copied (GTTX_CopyText), so it can be a short-lived string such as an interpolated one.</summary>
public unsafe class GtText : GtGadget
{
    public GtText() { KindId = 2; }

    public string? Text
    {
        get
        {
            if (TextCopy == null) return null;
            return Cast.PtrToString(TextCopy);
        }
        set
        {
            SetTextCopy(value);
        }
    }
}

/// <summary>
/// A GadTools window on the default public screen (Workbench), with its gadgets stacked in one column, each row as high as the
/// screen font needs, so it follows the user's font. Typical use:
/// <code>
/// var win = new GtWindow { Title = "Greeter", Width = 260 };
/// win.Add(name); win.Add(hello); win.Add(reply);
/// if (!win.Open()) return;
/// while (true) { GuiEvent e = win.Wait(); if (e.Kind == GuiEventKind.Close) break; ... }
/// win.Close(); Heap.Free(ref win);
/// </code>
/// <see cref="Close"/> releases the system objects in the right order (window, gadgets, visual info, screen lock) and frees the
/// gadgets that were added; free the window itself afterwards. Events come through GadTools' own message calls. On the host nothing is
/// shown: <see cref="HostClick"/>, <see cref="HostType"/> and <see cref="HostClose"/> drive the window the way a person would.
/// No menus, no hooks, no keyboard shortcuts (an underscore in a label is shown as is).
/// <para>
/// A program may also derive its own window from it (<c>class Greeter : GtWindow</c>, marked <see cref="ExtensibleAttribute"/>):
/// add the gadgets in the constructor, override <see cref="OnClicked"/>, <see cref="OnChanged"/> and <see cref="OnCloseRequest"/>,
/// and call <see cref="Run()"/>. The derived object is freed like the window (<see cref="Close"/>, then <c>Heap.Free</c>); anything
/// it owns besides the gadgets it frees itself before that.
/// </para>
/// </summary>
[Extensible]
public unsafe class GtWindow
{
    public string? Title { get; set; }

    /// <summary>Inner width in pixels (default 260).</summary>
    public int Width { get; set; }

    private GtGadget? _first;
    private GtGadget? _last;
    private int _count;
    private bool _open;
    private byte* _titleCopy;
#if AMIGA
    private static int _libUsers;
    private static void* _lib;
    private void* _screen;
    private void* _visualInfo;
    private void* _gadgetList;
    private void* _window;
#endif

    public GtWindow() { Width = 260; }

    /// <summary>Adds a gadget below the ones added before (before <see cref="Open"/>). False if the window is open already, the gadget is already in a window, or there are 16 gadgets.</summary>
    public bool Add(GtGadget gadget)
    {
        if (_open || _count >= 16 || gadget.Next != null || gadget == _last) return false;
        if (_last == null) _first = gadget; else _last.Next = gadget;
        _last = gadget;
        _count++;
        return true;
    }

    public bool IsOpen { get { return _open; } }

#if AMIGA
    private static bool AcquireLibrary()
    {
        if (_libUsers == 0)
        {
            _lib = Exec.OpenLibrary("gadtools.library", 39);
            if (_lib == null) return false;
            GadTools.SetBase(_lib);
        }
        _libUsers++;
        return true;
    }

    private static void ReleaseLibrary()
    {
        _libUsers--;
        if (_libUsers > 0) return;
        GadTools.SetBase(null);
        Exec.CloseLibrary(_lib);
        _lib = null;
    }

    /// <summary>Releases what is open, in the order the system needs: window, gadgets, visual info, screen lock, library.</summary>
    private void Teardown()
    {
        for (GtGadget? g = _first; g != null; g = g.Next) { g.Handle = null; g.WindowHandle = null; }
        if (_window != null) { Intuition.CloseWindow(_window); _window = null; }
        if (_gadgetList != null) { GadTools.FreeGadgets(_gadgetList); _gadgetList = null; }
        if (_visualInfo != null) { GadTools.FreeVisualInfo(_visualInfo); _visualInfo = null; }
        if (_screen != null) { Intuition.UnlockPubScreen(null, _screen); _screen = null; ReleaseLibrary(); }
        _open = false;
    }

    /// <summary>Opens the window on the Workbench screen. False if the library, the screen or memory is missing; nothing stays open then.</summary>
    public bool Open()
    {
        if (_open) return false;
        if (!AcquireLibrary()) return false;
        _screen = Intuition.LockPubScreen(null);
        if (_screen == null) { ReleaseLibrary(); return false; }
        _visualInfo = GadTools.GetVisualInfoA(_screen, null);
        byte* ng = Mem.AllocFast((uint)Layout.NewGadgetSize);
        if (_visualInfo == null || ng == null) { Mem.Free(ng); Teardown(); return false; }

        byte* scr = (byte*)_screen;
        void* textAttr = *(void**)(scr + Layout.ScreenFont);
        int fh = *(ushort*)((byte*)textAttr + Layout.TaYSize);
        int fw = fh / 2;
        void* font = Graphics.OpenFont(textAttr);
        if (font != null) { fw = *(ushort*)((byte*)font + Layout.TfXSize); Graphics.CloseFont(font); }

        // Layout: one column; labels to the left of the gadgets in a column as wide as the longest label.
        int rowHeight = fh + 6;
        int gap = 4;
        int labelChars = 0;
        for (GtGadget? g = _first; g != null; g = g.Next)
        {
            g.LabelCopy = GuiText.Dup(g.Label);
            if (g.KindId != 0 && g.LabelCopy != null)
            {
                int n = 0;
                while (g.LabelCopy[n] != 0) n++;
                if (n > labelChars) labelChars = n;
            }
        }
        int labelWidth = labelChars > 0 ? labelChars * fw + 8 : 0;
        int left = *(byte*)(scr + Layout.ScreenWBorLeft) + gap + labelWidth;
        int top = *(byte*)(scr + Layout.ScreenWBorTop) + fh + 1 + gap;
        int gadgetWidth = Width - 2 * gap - labelWidth;
        int innerHeight = gap + _count * (rowHeight + gap);

        void* list = null;
        void* previous = GadTools.CreateContext(&list);
        _gadgetList = list;
        int id = 1;
        bool ok = previous != null;
        for (GtGadget? g = _first; g != null && ok; g = g.Next)
        {
            for (int i = 0; i < Layout.NewGadgetSize; i++) ng[i] = 0;
            *(short*)(ng + Layout.NgLeftEdge) = (short)left;
            *(short*)(ng + Layout.NgTopEdge) = (short)top;
            *(short*)(ng + Layout.NgWidth) = (short)gadgetWidth;
            *(short*)(ng + Layout.NgHeight) = (short)rowHeight;
            *(byte**)(ng + Layout.NgGadgetText) = g.LabelCopy;
            *(void**)(ng + Layout.NgTextAttr) = textAttr;
            *(ushort*)(ng + Layout.NgGadgetID) = (ushort)id;
            *(uint*)(ng + Layout.NgFlags) = g.KindId == 0 ? 0u : Layout.PlaceTextLeft;
            *(void**)(ng + Layout.NgVisualInfo) = _visualInfo;
            uint kind = Layout.KindButton;
            TagList w = new TagList();
            if (g.KindId == 1)
            {
                kind = Layout.KindString;
                if (g.TextCopy != null) w.Add(Layout.GtstString, g.TextCopy);
                w.Add(Layout.GtstMaxChars, ((GtString)g).MaxChars);
            }
            else if (g.KindId == 2)
            {
                kind = Layout.KindText;
                if (g.TextCopy != null)
                {
                    w.Add(Layout.GttxText, g.TextCopy);
                    w.Add(Layout.GttxCopyText, true);
                }
                w.Add(Layout.GttxBorder, true);
            }
            nuint* t = w.End();
            ok = t != null;
            if (ok)
            {
                previous = GadTools.CreateGadgetA(kind, previous, ng, t);
                ok = previous != null;
            }
            g.Handle = previous;
            top += rowHeight + gap;
            id++;
        }
        Mem.Free(ng);
        if (ok)
        {
            _titleCopy = GuiText.Dup(Title);
            TagList w = new TagList();
            w.Add(Intuition.WA_Left, 40);
            w.Add(Intuition.WA_Top, 30);
            w.Add(Layout.WaInnerWidth, Width);
            w.Add(Layout.WaInnerHeight, innerHeight);
            if (_titleCopy != null) w.Add(Layout.WaTitle, _titleCopy);
            w.Add(Layout.WaGadgets, _gadgetList);
            w.Add(Layout.WaPubScreen, _screen);
            w.Add(Intuition.WA_IDCMP, Layout.IdcmpGadgetUp | Layout.IdcmpCloseWindow | Layout.IdcmpRefreshWindow);
            w.Add(Layout.WaDragBar, true);
            w.Add(Layout.WaDepthGadget, true);
            w.Add(Layout.WaCloseGadget, true);
            w.Add(Intuition.WA_Activate, true);
            nuint* t = w.End();
            if (t != null) _window = Intuition.OpenWindowTagList(null, t);
        }
        if (_window == null) { FreeOwnedStrings(); Teardown(); return false; }
        GadTools.GT_RefreshWindow(_window, null);
        for (GtGadget? g = _first; g != null; g = g.Next) g.WindowHandle = _window;
        _open = true;
        return true;
    }

    /// <summary>
    /// Waits for the next event (the task sleeps meanwhile). Refreshes of the window are handled inside. On a window that is not open
    /// it returns <see cref="GuiEventKind.Close"/> at once.
    /// </summary>
    public GuiEvent Wait()
    {
        if (!_open) return new GuiEvent(GuiEventKind.Close, null);
        void* port = *(void**)((byte*)_window + Layout.WindowUserPort);
        while (true)
        {
            byte* msg = (byte*)GadTools.GT_GetIMsg(port);
            if (msg == null) { Exec.WaitPort(port); continue; }
            uint cls = *(uint*)(msg + Layout.MsgClass);
            void* address = *(void**)(msg + Layout.MsgIAddress);
            GadTools.GT_ReplyIMsg(msg);
            if (cls == Layout.IdcmpCloseWindow) return new GuiEvent(GuiEventKind.Close, null);
            if (cls == Layout.IdcmpRefreshWindow)
            {
                GadTools.GT_BeginRefresh(_window);
                GadTools.GT_EndRefresh(_window, 1);
                continue;
            }
            if (cls == Layout.IdcmpGadgetUp)
            {
                for (GtGadget? g = _first; g != null; g = g.Next)
                {
                    if (g.Handle == address)
                        return new GuiEvent(g.KindId == 1 ? GuiEventKind.Changed : GuiEventKind.Clicked, g);
                }
            }
        }
    }

    private bool NoMoreEvents() { return false; }

    /// <summary>Closes the window and releases everything GadTools and Intuition gave it, then frees the gadgets that were added (do not use them afterwards).</summary>
    public void Close()
    {
        Teardown();
        FreeOwnedStrings();
        FreeGadgets();
    }
#else
    /// <summary>Host: set false to make <see cref="Open"/> fail, the way a missing library or screen would.</summary>
    public static bool HostCanOpen = true;

    // One queue for the fake (one window at a time).
    private static GuiEvent[] _queue = new GuiEvent[16];
    private static int _queued;
    private bool _drained;          // the last Wait found no event queued (its Close is made up, not a click)

    private bool NoMoreEvents() { return _drained; }

    public bool Open()
    {
        if (_open || !HostCanOpen) return false;
        _titleCopy = GuiText.Dup(Title);
        _queued = 0;
        _open = true;
        return true;
    }

    /// <summary>Host: the next event driven by <see cref="HostClick"/> or <see cref="HostClose"/>; <see cref="GuiEventKind.Close"/> when none is left (so a loop cannot hang).</summary>
    public GuiEvent Wait()
    {
        _drained = _queued == 0;
        if (!_open || _queued == 0) return new GuiEvent(GuiEventKind.Close, null);
        GuiEvent e = _queue[0];
        for (int i = 1; i < _queued; i++) _queue[i - 1] = _queue[i];
        _queued--;
        return e;
    }

    public void Close()
    {
        _open = false;
        _queued = 0;
        FreeOwnedStrings();
        FreeGadgets();
    }

    private static void Post(GuiEventKind kind, GtGadget? source)
    {
        if (_queued < 16) { _queue[_queued] = new GuiEvent(kind, source); _queued++; }
    }

    /// <summary>Host: clicks a button, or presses Return in a string gadget (a <see cref="GuiEventKind.Changed"/> event).</summary>
    public void HostClick(GtGadget gadget) { Post(gadget.KindId == 1 ? GuiEventKind.Changed : GuiEventKind.Clicked, gadget); }

    /// <summary>Host: types into a string gadget, replacing its text (no event until <see cref="HostClick"/> presses Return).</summary>
    public void HostType(GtString gadget, string text) { gadget.Text = text; }

    /// <summary>Host: clicks the close gadget.</summary>
    public void HostClose() { Post(GuiEventKind.Close, null); }

    /// <summary>Host: how many gadgets the window has.</summary>
    public int HostGadgetCount { get { return _count; } }
#endif

    /// <summary>Waits for events until the close gadget, calling <paramref name="handler"/> for each. Returns when the window is closed by the user (call <see cref="Close"/> afterwards).</summary>
    public void Run(IGuiHandler handler)
    {
        while (true)
        {
            GuiEvent e = Wait();
            if (e.Kind == GuiEventKind.Close) return;
            GtGadget? source = e.Source;
            if (source == null) continue;
            if (e.Kind == GuiEventKind.Clicked) handler.Clicked(source);
            else handler.Changed(source);
        }
    }

    /// <summary>
    /// Waits for events until the close gadget is accepted, handing each to <see cref="Dispatch"/>, which calls the window's own
    /// <see cref="OnClicked"/>, <see cref="OnChanged"/> and <see cref="OnCloseRequest"/> (override them in a class derived from
    /// <see cref="GtWindow"/>). Returns when <see cref="OnCloseRequest"/> says true (the default); call <see cref="Close"/> afterwards.
    /// </summary>
    public void Run()
    {
        while (true)
        {
            GuiEvent e = Wait();
            // Not open (or, on the host, no event left): nothing more can come, so that ends it whatever OnCloseRequest would say.
            if (e.Kind == GuiEventKind.Close && (!_open || NoMoreEvents())) return;
            if (!Dispatch(e)) return;
        }
    }

    /// <summary>
    /// Hands one event (from <see cref="Wait"/>) to the window's hooks: <see cref="OnClicked"/>, <see cref="OnChanged"/> or
    /// <see cref="OnCloseRequest"/>. False when it was a close request that was accepted (stop waiting), true otherwise. A pull loop
    /// can use it for the events it does not handle itself.
    /// </summary>
    public bool Dispatch(GuiEvent e)
    {
        if (e.Kind == GuiEventKind.Close) return !OnCloseRequest();
        GtGadget? source = e.Source;
        if (source == null) return true;
        if (e.Kind == GuiEventKind.Clicked) OnClicked(source);
        else OnChanged(source);
        return true;
    }

    /// <summary>A button of this window was clicked (from <see cref="Run()"/> or <see cref="Dispatch"/>). Does nothing unless overridden.</summary>
    protected virtual void OnClicked(GtGadget gadget) { }

    /// <summary>A string gadget of this window was accepted with Return (from <see cref="Run()"/> or <see cref="Dispatch"/>). Does nothing unless overridden.</summary>
    protected virtual void OnChanged(GtGadget gadget) { }

    /// <summary>The close gadget was clicked (from <see cref="Run()"/>): true ends <see cref="Run()"/>, false keeps the window open. True unless overridden.</summary>
    protected virtual bool OnCloseRequest() { return true; }

    private void FreeOwnedStrings()
    {
        if (_titleCopy != null) GuiText.Free(_titleCopy);
        _titleCopy = null;
        for (GtGadget? g = _first; g != null; g = g.Next)
        {
            if (g.LabelCopy != null) { GuiText.Free(g.LabelCopy); g.LabelCopy = null; }
        }
    }

    private void FreeGadgets()
    {
        GtGadget? g = _first;
        while (g != null)
        {
            GtGadget? next = g.Next;
            g.FreeCopies();
            Heap.Free(ref g);
            g = next;
        }
        _first = null;
        _last = null;
        _count = 0;
    }
}
#endif
