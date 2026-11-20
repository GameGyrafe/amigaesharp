// Amiga-only (MUI): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
using System;

namespace ESharp.Platform;

// MUI custom classes (issues #93, #114). MUI facts (method numbers, message layouts, struct MUI_CustomClass, MUI_MinMax, the render info, the area data and
// the event handler node) are taken from the AROS Zune headers; the muimaster.library vectors from the AROS fd file.

/// <summary>
/// A MUI object whose class is written in C#: a subclass of Area.mui. Derive from it and override the members MUI calls
/// (<c>OnAskMinMax</c>, <c>OnDraw</c>, <c>OnSetup</c>, <c>OnCleanup</c>, <c>OnSet</c>, <c>OnGet</c>, <c>OnMethod</c>):
/// <code>
/// sealed class Dial : MuiArea
/// {
///     public static IntAttr Value => new IntAttr(0x8E5A0001);       // an attribute of its own (TAG_USER and a number of the program's)
///     int _value;
///     protected override void OnAskMinMax(ref MuiMinMax size) { size.MinWidth = 40; size.MinHeight = 20; size.DefWidth = 80; size.DefHeight = 40; }
///     protected override void OnDraw(MuiRender r) { r.Fill(MuiPen.Fill, 0, 0, r.Width * _value / 100, r.Height); }
///     protected override void OnSet(uint attr, nuint value) { if (attr == Value.Tag) { _value = (int)value; Redraw(); } }
///     protected override bool OnGet(uint attr, out nuint value) { value = (nuint)_value; return attr == Value.Tag; }
/// }
/// </code>
/// The object is used like any other MUI object (added to a group, <c>Set</c>/<c>Get</c> of its attributes, notifications on them).
/// One MUI class (MUI_CreateCustomClass on Area.mui) serves every C# class derived from MuiArea: it is made with the first object and
/// deleted after the last one is disposed. Its dispatcher finds the C# object through the instance data and calls the virtual members
/// above, on the program's own task (inside the application's input handling, or inside the call that sent the method), so they may
/// allocate, print and set attributes; disposing MUI objects in them is refused, as in a notification handler. For mouse and key input
/// the class calls <c>WantEvents</c> with the IDCMP classes it wants (<see cref="MuiEvent.MouseButtons"/>, ...) and overrides
/// <c>OnEvent</c> (MUIM_HandleEvent); see <see cref="MuiEvent"/>.
/// </summary>
[Extensible]
public unsafe class MuiArea : MuiObject
{
    protected MuiArea() : base("Area.mui") { CustomKind = MuiCustomClasses.Area; }

    /// <summary>A <see cref="MuiFrame"/> value (set before the object is made).</summary>
    public int Frame { set { if (!IsCreated) Init(Muia.Frame, value); } }
}

/// <summary>
/// A group whose class is written in C#: a subclass of Group.mui, with the same members to override as <see cref="MuiArea"/> and the
/// group's own (<see cref="MuiGroup.Add"/>, <see cref="MuiGroup.Horizontal"/>, ...). The group draws its children first; <c>OnDraw</c>
/// then draws over them. One MUI class serves every C# class derived from it.
/// </summary>
[Extensible]
public unsafe class MuiCustomGroup : MuiGroup
{
    protected MuiCustomGroup() : base(MuiCustomClasses.Group) { }
}

/// <summary>The size MUIM_AskMinMax asks of a custom class, in pixels (added to the superclass's sizes).</summary>
public struct MuiMinMax
{
    /// <summary>MUI_MAXMAX: no limit. Use it as a maximum to let the object grow.</summary>
    public const int Unlimited = 10000;
    public int MinWidth, MinHeight, MaxWidth, MaxHeight, DefWidth, DefHeight;

    // Adds the sizes to the struct MUI_MinMax (six WORDs) the superclass filled in; minimum <= default <= maximum <= Unlimited.
    internal unsafe void AddTo(short* mm)
    {
        if (mm == null) return;
        int minW = Clamp(mm[0] + MinWidth), minH = Clamp(mm[1] + MinHeight);
        int maxW = Clamp(mm[2] + MaxWidth), maxH = Clamp(mm[3] + MaxHeight);
        int defW = Clamp(mm[4] + DefWidth), defH = Clamp(mm[5] + DefHeight);
        if (defW < minW) defW = minW;
        if (defH < minH) defH = minH;
        if (maxW < defW) maxW = defW;
        if (maxH < defH) maxH = defH;
        mm[0] = (short)minW;
        mm[1] = (short)minH;
        mm[2] = (short)maxW;
        mm[3] = (short)maxH;
        mm[4] = (short)defW;
        mm[5] = (short)defH;
    }

    private static int Clamp(int v) { return v < 0 ? 0 : v > Unlimited ? Unlimited : v; }
}

/// <summary>
/// An input event for a custom class (MUIM_HandleEvent): the IntuiMessage's class, code and qualifier, and the mouse position relative
/// to the object's contents box (the top left corner inside its frame, as <see cref="MuiRender"/> draws). A class asks for events with
/// <c>WantEvents(MuiEvent.MouseButtons | MuiEvent.MouseMove)</c> (in its constructor or <c>OnSetup</c>); MUI then calls <c>OnEvent</c>
/// for each such event in the object's window while it is shown, wherever the mouse is (check <see cref="Inside"/>). <c>OnEvent</c>
/// returning true eats the event (MUI_EventHandlerRC_Eat): no other handler sees it.
/// <code>
/// protected override bool OnEvent(MuiEvent e)
/// {
///     if (e.Class == MuiEvent.MouseButtons &amp;&amp; e.Code == MuiEvent.SelectDown &amp;&amp; e.Inside) { Set(Value, e.MouseX * 100 / e.Width); return true; }
///     return false;
/// }
/// </code>
/// </summary>
public struct MuiEvent
{
#if AMIGA
    /// <summary>IDCMP_MOUSEBUTTONS: a mouse button went down or up (<see cref="Code"/>: <see cref="SelectDown"/>, <see cref="SelectUp"/>, ...).</summary>
    public const uint MouseButtons = Layout.IdcmpMouseButtons;
    /// <summary>IDCMP_MOUSEMOVE: the mouse moved.</summary>
    public const uint MouseMove = Layout.IdcmpMouseMove;
    /// <summary>IDCMP_RAWKEY: a key went down or up (<see cref="Code"/> is the raw key code; see <see cref="Keys"/>).</summary>
    public const uint RawKey = Layout.IdcmpRawKey;
    /// <summary>The left mouse button went down (a <see cref="MouseButtons"/> code).</summary>
    public const int SelectDown = (int)Layout.SelectDown;
    /// <summary>The left mouse button went up.</summary>
    public const int SelectUp = (int)Layout.SelectUp;
#else
    public const uint MouseButtons = 0x8, MouseMove = 0x10, RawKey = 0x400;
    public const int SelectDown = 0x68, SelectUp = 0xE8;
#endif

    /// <summary>The IDCMP class (<see cref="MouseButtons"/>, <see cref="MouseMove"/>, <see cref="RawKey"/>, ...); 0 without a message.</summary>
    public uint Class;
    /// <summary>The IntuiMessage's code: the button for <see cref="MouseButtons"/>, the raw key for <see cref="RawKey"/>.</summary>
    public int Code;
    /// <summary>The qualifier (shift, alt, buttons held: <see cref="Keys"/>' constants).</summary>
    public int Qualifier;
    /// <summary>The mouse position relative to the contents box's top left corner (negative or past the size when outside).</summary>
    public int MouseX, MouseY;
    /// <summary>The contents box's size.</summary>
    public int Width, Height;
    /// <summary>MUI's own key code for the event (MUIKEY_*: -1 for none, 0 for "press", ...), as MUI's settings map keys to actions.</summary>
    public int MuiKey;

    /// <summary>True when the mouse is inside the contents box.</summary>
    public bool Inside => MouseX >= 0 && MouseY >= 0 && MouseX < Width && MouseY < Height;
}

/// <summary>MUI's pens (MPEN_*), for <see cref="MuiRender"/>: the user's colours from the MUI settings.</summary>
public static class MuiPen
{
    public const int Shine = 0, HalfShine = 1, Background = 2, HalfShadow = 3, Shadow = 4, Text = 5, Fill = 6, Mark = 7;
}

/// <summary>
/// What <c>OnDraw</c> draws with: the object's contents box (<see cref="Left"/>, <see cref="Top"/>, <see cref="Width"/>,
/// <see cref="Height"/>: inside its frame and spacing) and MUI's rast port and pens. The drawing calls take coordinates relative to the
/// contents' top left corner and a <see cref="MuiPen"/>. <see cref="Fill"/> is clipped to the object's whole box (frame included) and
/// <see cref="Line"/> moves its ends into it, so neither lands on a neighbour; <see cref="Text"/> is not clipped.
/// </summary>
public unsafe struct MuiRender
{
    public int Left, Top, Width, Height;
    /// <summary>True for MADF_DRAWUPDATE (the object asked for an update), false for a full redraw.</summary>
    public bool Update;
    // The object's whole box (frame and spacing included), for clipping.
    private int _x0, _y0, _x1, _y1;
#if AMIGA
    private void* _rp;
    private ushort* _pens;
    private void* _font;

    internal MuiRender(void* obj, void* renderInfo, bool update)
    {
        int p = sizeof(nuint);
        // struct MUI_AreaData (found in the object by MuiCustomClasses.FindAreaData): three pointers (render info, background, font),
        // MUI_MinMax (12 bytes), the box (an IBox: four WORDs), then addleft, addtop, subwidth, subheight (BYTEs).
        byte* ad = (byte*)obj + MuiCustomClasses.AreaData;
        short* box = (short*)(ad + 3 * p + 12);
        sbyte* add = (sbyte*)(ad + 3 * p + 20);
        _x0 = box[0];
        _y0 = box[1];
        _x1 = box[0] + box[2] - 1;
        _y1 = box[1] + box[3] - 1;
        Left = box[0] + add[0];
        Top = box[1] + add[1];
        Width = box[2] - add[2];
        Height = box[3] - add[3];
        Update = update;
        _font = *(void**)(ad + 2 * p);
        // struct MUI_RenderInfo: window object, screen, draw info, pens (UWORD *), window, rast port, ...
        _pens = *(ushort**)((byte*)renderInfo + 3 * p);
        _rp = *(void**)((byte*)renderInfo + 5 * p);
    }

    /// <summary>MUI's rast port for this drawing (for graphics.library calls of your own; coordinates are window coordinates).</summary>
    public void* RastPort => _rp;

    /// <summary>The system pen number of a <see cref="MuiPen"/>.</summary>
    public int Pen(int muiPen) { return _pens == null || muiPen < 0 || muiPen > 7 ? 1 : _pens[muiPen]; }
#else
    internal MuiRender(int left, int top, int width, int height, bool update)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Update = update;
        _x0 = left;
        _y0 = top;
        _x1 = left + width - 1;
        _y1 = top + height - 1;
    }

    /// <summary>Host: no rast port.</summary>
    public void* RastPort => null;

    public int Pen(int muiPen) { return muiPen; }

    /// <summary>Host: drawing calls made so far (rectangles, lines, texts), for tests.</summary>
    public static int HostFills, HostLines, HostTexts;
#endif

    /// <summary>A filled rectangle at x, y (relative to the contents), clipped to the object.</summary>
    public void Fill(int muiPen, int x, int y, int width, int height)
    {
        int x0 = Left + x, y0 = Top + y, x1 = x0 + width - 1, y1 = y0 + height - 1;
        if (x0 < _x0) x0 = _x0;
        if (y0 < _y0) y0 = _y0;
        if (x1 > _x1) x1 = _x1;
        if (y1 > _y1) y1 = _y1;
        if (x1 < x0 || y1 < y0) return;
#if AMIGA
        Graphics.SetAPen(_rp, Pen(muiPen));
        Graphics.RectFill(_rp, x0, y0, x1, y1);
#else
        HostFills++;
#endif
    }

    /// <summary>A line (relative to the contents); both ends are moved into the object's box first.</summary>
    public void Line(int muiPen, int xFrom, int yFrom, int xTo, int yTo)
    {
        int ax = ClampX(Left + xFrom), ay = ClampY(Top + yFrom), bx = ClampX(Left + xTo), by = ClampY(Top + yTo);
#if AMIGA
        Graphics.SetAPen(_rp, Pen(muiPen));
        Graphics.Move(_rp, ax, ay);
        Graphics.Draw(_rp, bx, by);
#else
        HostLines++;
#endif
    }

    /// <summary>Text in the object's font with its top at y (relative to the contents); not clipped: keep it short.</summary>
    public void Text(int muiPen, int x, int y, string text)
    {
#if AMIGA
        if (_font != null) Graphics.SetFont(_rp, _font);
        int baseline = _font == null ? 6 : *(ushort*)((byte*)_font + Layout.TfBaseline);
        Graphics.SetAPen(_rp, Pen(muiPen));
        Graphics.SetDrMd(_rp, 0);   // JAM1
        Graphics.Move(_rp, Left + x, Top + y + baseline);
        Graphics.Text(_rp, text, text.Length);
#else
        HostTexts++;
#endif
    }

    private int ClampX(int x) { return x < _x0 ? _x0 : x > _x1 ? _x1 : x; }

    private int ClampY(int y) { return y < _y0 ? _y0 : y > _y1 ? _y1 : y; }
}

/// <summary>
/// The MUI classes behind <see cref="MuiArea"/> and <see cref="MuiCustomGroup"/>: one per MUI superclass, made with
/// MUI_CreateCustomClass when the first object of it is made and deleted (MUI_DeleteCustomClass) once MUI has disposed its last object.
/// Each object's instance data is one pointer: the C# object (set in OM_NEW, cleared in OM_DISPOSE).
/// </summary>
internal static unsafe class MuiCustomClasses
{
    internal const int Area = 1, Group = 2;
    internal static int Live, Made, Objects;
    private static int _areaObjects, _groupObjects;
#if AMIGA
    private static void* _areaMcc, _groupMcc;       // struct MUI_CustomClass *
    private static void* _areaClass, _groupClass;   // their mcc_Class
    private static MuiObject? _creating;            // the C# object whose OM_NEW is under way
    /// <summary>Where struct MUI_AreaData starts in an object: 0 not found out yet, -1 not found.</summary>
    internal static int AreaData;

    // Makes the system object of a custom class (and the class, with the first object).
    internal static void* NewObject(MuiObject o, int kind, nuint* tags)
    {
        void* mcc = kind == Area ? _areaMcc : _groupMcc;
        if (mcc == null)
        {
            delegate*<void*, void*, void*, nuint> entry = &Dispatch;
            mcc = MuiMaster.MUI_CreateCustomClass(null, Cast.StringToPtr(kind == Area ? "Area.mui" : "Group.mui"), null, sizeof(nuint), (void*)entry);
            if (mcc == null) return null;
            void* cl = *(void**)((byte*)mcc + 6 * sizeof(nuint));   // mcc_Class, after mcc_UserData, four library bases and mcc_Super
            if (kind == Area) { _areaMcc = mcc; _areaClass = cl; } else { _groupMcc = mcc; _groupClass = cl; }
            Live++;
            Made++;
        }
        _creating = o;
        void* obj = Intuition.NewObjectA(kind == Area ? _areaClass : _groupClass, null, (void*)tags);
        _creating = null;
        return obj;
    }

    // The dispatcher of both classes: MUI calls it for every method sent to an object of them.
    [HookEntry]
    private static nuint Dispatch(void* cls, void* obj, void* msg)
    {
        nuint* m = (nuint*)msg;
        uint id = (uint)m[0];
        if (id == MuiId.OmNew)
        {
            MuiObject? self = _creating;
            _creating = null;
            void* o = (void*)BoopsiNative.es_dosupermethod(cls, obj, msg);
            if (o == null) return 0;
            void** slot = (void**)BoopsiNative.es_inst_data(cls, o);
            *slot = self == null ? null : Cast.ObjectToPtr(self);
            if (self != null)
            {
                Count(cls, 1);                          // counted with its pointer: OM_DISPOSE counts it off only when the pointer is set
                SetEach(self, (nuint*)m[1]);            // the starting attributes
            }
            return (nuint)o;
        }
        void** data = (void**)BoopsiNative.es_inst_data(cls, obj);
        if (id == MuiId.OmDispose)
        {
            if (*data != null)
            {
                MuiObject gone = Cast.PtrToObject<MuiObject>(*data);
                gone.CustomGone();
                Count(cls, -1);
            }
            *data = null;
            return BoopsiNative.es_dosupermethod(cls, obj, msg);
        }
        if (*data == null) return BoopsiNative.es_dosupermethod(cls, obj, msg);
        MuiObject me = Cast.PtrToObject<MuiObject>(*data);
        return me.CustomDispatch(cls, obj, id, m);
    }

    private static void Count(void* cls, int d)
    {
        if (cls == _areaClass) _areaObjects += d; else _groupObjects += d;
        Objects += d;
    }

    // OM_SET and OM_NEW: each attribute of a tag list (TAG_DONE 0, TAG_IGNORE 1, TAG_MORE 2, TAG_SKIP 3).
    internal static void SetEach(MuiObject o, nuint* t)
    {
        while (t != null)
        {
            uint tag = (uint)t[0];
            if (tag == 0) return;
            if (tag == 2) { t = (nuint*)t[1]; continue; }
            if (tag == 3) { t += 2 * ((int)t[1] + 1); continue; }
            if (tag != 1) o.CallSet(tag, t[1]);
            t += 2;
        }
    }

    // The object's contents box (_mleft, _mtop, _mwidth, _mheight of the Zune headers: the box inside the frame and spacing), in window
    // coordinates; false while the area data is not known.
    internal static bool ContentBox(void* obj, out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;
        if (AreaData <= 0) return false;
        int p = sizeof(nuint);
        byte* ad = (byte*)obj + AreaData;
        short* box = (short*)(ad + 3 * p + 12);
        sbyte* add = (sbyte*)(ad + 3 * p + 20);
        left = box[0] + add[0];
        top = box[1] + add[1];
        width = box[2] - add[2];
        height = box[3] - add[3];
        return true;
    }

    // At the first MUIM_Setup: find struct MUI_AreaData in the object by the render info pointer it starts with. It follows
    // struct MUI_NotifyData (seven pointer-sized fields), possibly aligned further (newer AROS aligns class instance data).
    internal static void FindAreaData(void* obj, void* renderInfo)
    {
        if (AreaData != 0 || renderInfo == null) return;
        for (int k = 7; k <= 16; k++)
        {
            if (*(void**)((byte*)obj + k * sizeof(nuint)) == renderInfo)
            {
                AreaData = k * sizeof(nuint);
                return;
            }
        }
        AreaData = -1;
        Mui.Warn("E#: MUI's area data was not found in a custom object; OnDraw is not called\n");
    }

    /// <summary>Deletes the classes whose objects are all gone (never while one is being made).</summary>
    internal static void DeleteUnused()
    {
        if (_creating != null) return;
        if (_areaMcc != null && _areaObjects == 0 && MuiMaster.MUI_DeleteCustomClass(_areaMcc) != 0)
        {
            _areaMcc = null;
            _areaClass = null;
            Live--;
        }
        if (_groupMcc != null && _groupObjects == 0 && MuiMaster.MUI_DeleteCustomClass(_groupMcc) != 0)
        {
            _groupMcc = null;
            _groupClass = null;
            Live--;
        }
    }
#else
    private static bool _hostArea, _hostGroup;

    internal static void HostNew(int kind)
    {
        if (kind == Area)
        {
            if (!_hostArea) { _hostArea = true; Live++; Made++; }
            _areaObjects++;
        }
        else
        {
            if (!_hostGroup) { _hostGroup = true; Live++; Made++; }
            _groupObjects++;
        }
        Objects++;
    }

    internal static void HostDisposed(int kind)
    {
        if (kind == Area) _areaObjects--; else _groupObjects--;
        Objects--;
    }

    internal static void DeleteUnused()
    {
        if (_hostArea && _areaObjects == 0) { _hostArea = false; Live--; }
        if (_hostGroup && _groupObjects == 0) { _hostGroup = false; Live--; }
    }
#endif
}
#endif
