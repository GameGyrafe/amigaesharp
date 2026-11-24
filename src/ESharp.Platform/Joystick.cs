namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// lowlevel.library bindings (v40). The library is on disk (<c>Libs:</c>), not in the Kickstart ROM, so
/// <see cref="Joystick"/> opens it first and only then calls these. Vectors from the classic fd file, checked
/// against the AROS headers (tools/BindGen compare).
/// </summary>
[LibraryBase("lowlevel", "LowLevelBase", "struct Library")]
public static unsafe class LowLevel
{
    /// <summary>Emitter intrinsic: sets the library base that the calls below use (the result of OpenLibrary, or null afterwards).</summary>
    public static extern void SetBase(void* library);

    [AmigaLib("lowlevel", -30), Since(40)] public static extern uint ReadJoyPort(uint port);
    [AmigaLib("lowlevel", -132), Since(40)] public static extern int SetJoyPortAttrsA(uint port, void* tags);
}
#endif

/// <summary>
/// Joysticks and CD32 game pads through lowlevel.library. Port 1 is the usual joystick port, port 0 the mouse
/// port. <see cref="Read"/> returns the direction and button bits below. A CD32 pad also sets the extra buttons
/// and an ordinary joystick only <see cref="Fire"/> (and <see cref="Fire2"/> on two-button sticks).
/// lowlevel.library is on the Workbench disk, not in ROM: if it is not installed, <see cref="Open"/> returns false
/// and everything reads as 0, so a game should fall back to the keyboard. On the host nothing is connected
/// until a test calls <see cref="HostSet"/>.
/// <para>
/// On SDL the ports are game pads (SDL's gamepad API): port 1 is the first pad connected, port 0 the second, and a pad
/// connected later is picked up by the next <see cref="Read"/> or <see cref="Kind"/> (when the port-1 pad goes away, the
/// port-0 one moves to port 1). Fire is the south face button (A on an Xbox pad), Fire2 east (B), Yellow west (X), Green
/// north (Y), Forward/Reverse the right/left shoulder buttons and Play Start. The directions come from the d-pad and from
/// the left stick past a dead zone of a quarter of its travel. <see cref="Kind"/> is <see cref="KindGamepad"/> for a port
/// with a pad; without one, port 0 is <see cref="KindMouse"/> (the desktop pointer, as the Amiga mouse port) and port 1
/// <see cref="KindNone"/>, or <see cref="KindJoystick"/> while the <see cref="KeyboardStandIn"/> is on. The bit values are
/// the host fake's (the same bits as lowlevel's), so game code is the same on every target.
/// </para>
/// </summary>
public static unsafe class Joystick
{
    // What a port has plugged in (Kind).
    public const int KindNone = 0, KindGamepad = 1, KindMouse = 2, KindJoystick = 3, KindUnknown = 4;

#if AMIGA
    // Direction and button bits, measured from the system headers per target (Layout.g.cs).
    public const uint Up = Layout.JpUp, Down = Layout.JpDown, Left = Layout.JpLeft, Right = Layout.JpRight;
    /// <summary>The fire button (red on a CD32 pad).</summary>
    public const uint Fire = Layout.JpButtonRed;
    /// <summary>The second button (blue on a CD32 pad).</summary>
    public const uint Fire2 = Layout.JpButtonBlue;
    public const uint Yellow = Layout.JpButtonYellow, Green = Layout.JpButtonGreen;
    public const uint Forward = Layout.JpButtonForward, Reverse = Layout.JpButtonReverse, Play = Layout.JpButtonPlay;

    private static void* _base;

    /// <summary>Opens lowlevel.library (v40). False if it is not installed.</summary>
    public static bool Open()
    {
        if (_base != null) return true;
        _base = Exec.OpenLibrary("lowlevel.library", 40);
        if (_base == null) return false;
        LowLevel.SetBase(_base);
        return true;
    }

    public static bool IsOpen { get { return _base != null; } }

    /// <summary>The direction and button bits of <paramref name="port"/> (0 when not open or nothing usable is plugged in).</summary>
    public static uint Read(int port)
    {
        if (_base == null) return 0;
        uint state = LowLevel.ReadJoyPort((uint)port);
        uint type = state & Layout.JpTypeMask;
        if (type != Layout.JpTypeJoystk && type != Layout.JpTypeGameCtlr) return 0;   // a mouse's low bits are counters, not directions
        return state & ~Layout.JpTypeMask;
    }

    /// <summary>What is plugged into <paramref name="port"/> (one of the Kind constants). lowlevel senses it on the first reads, so a fresh port may say <see cref="KindNone"/> once.</summary>
    public static int Kind(int port)
    {
        if (_base == null) return KindNone;
        uint type = LowLevel.ReadJoyPort((uint)port) & Layout.JpTypeMask;
        if (type == Layout.JpTypeGameCtlr) return KindGamepad;
        if (type == Layout.JpTypeMouse) return KindMouse;
        if (type == Layout.JpTypeJoystk) return KindJoystick;
        if (type == Layout.JpTypeNotAvail) return KindNone;
        return KindUnknown;
    }

    /// <summary>Tells lowlevel what is in <paramref name="port"/> instead of letting it guess: a CD32 pad (true) or a plain joystick (false). False if not open or refused (hosted AROS refuses).</summary>
    public static bool Force(int port, bool gamepad)
    {
        if (_base == null) return false;
        TagList t = new TagList();
        t.Add(Layout.SjaType, gamepad ? Layout.SjaTypeGameCtlr : Layout.SjaTypeJoystk);
        nuint* p = t.End();
        if (p == null) return false;
        return LowLevel.SetJoyPortAttrsA((uint)port, p) != 0;
    }

    /// <summary>Gives the ports back to automatic sensing and closes the library.</summary>
    public static void Close()
    {
        if (_base == null) return;
        TagList t = new TagList();
        t.Add(Layout.SjaType, Layout.SjaTypeAuto);
        nuint* p = t.End();
        if (p != null) { LowLevel.SetJoyPortAttrsA(0, p); LowLevel.SetJoyPortAttrsA(1, p); }
        LowLevel.SetBase(null);
        Exec.CloseLibrary(_base);
        _base = null;
    }
#else
    // Host and SDL: our own bit values (the Amiga ones come from the system headers; these are the same bits). es_sdl.h uses them too.
    public const uint Up = 8, Down = 4, Left = 2, Right = 1;
    public const uint Fire = 1u << 22, Fire2 = 1u << 23;
    public const uint Yellow = 1u << 21, Green = 1u << 20;
    public const uint Forward = 1u << 19, Reverse = 1u << 18, Play = 1u << 17;
#endif

#if SDL
    private static bool _open;
    private static int _force0, _force1;   // a kind set with Force (KindNone: not forced)

    /// <summary>Starts SDL's gamepad support. False if SDL cannot (then everything reads as 0).</summary>
    public static bool Open()
    {
        if (_open) return true;
        _open = Crt.es_sdl_joy_open();
        return _open;
    }

    public static bool IsOpen { get { return _open; } }

    /// <summary>The direction and button bits of the pad on <paramref name="port"/> (0 when not open or nothing is there). A port
    /// forced to a joystick reports only the directions, <see cref="Fire"/> and <see cref="Fire2"/>, as lowlevel does.</summary>
    public static uint Read(int port)
    {
        if (!_open || port < 0 || port > 1) return 0;
        uint bits = Crt.es_sdl_joy_read(port, _keyboard);
        int forced = port == 0 ? _force0 : _force1;
        if (forced == KindJoystick) bits = bits & (Up | Down | Left | Right | Fire | Fire2);
        return bits;
    }

    /// <summary>What is on <paramref name="port"/>: <see cref="KindGamepad"/> for a pad, otherwise <see cref="KindMouse"/> on port 0 and
    /// <see cref="KindNone"/> on port 1 (<see cref="KindJoystick"/> with the keyboard stand-in on), or the kind set with <see cref="Force"/>.</summary>
    public static int Kind(int port)
    {
        if (!_open || port < 0 || port > 1) return KindNone;
        int forced = port == 0 ? _force0 : _force1;
        if (forced != KindNone) return forced;
        return Crt.es_sdl_joy_kind(port, _keyboard);
    }

    /// <summary>Makes <paramref name="port"/> report a CD32 pad (true) or a plain joystick (false) from now on, as lowlevel's SetJoyPortAttrs does:
    /// <see cref="Kind"/> says so, and a joystick reads only the directions and two buttons. Undone by <see cref="Close"/>.</summary>
    public static bool Force(int port, bool gamepad)
    {
        if (!_open || port < 0 || port > 1) return false;
        if (port == 0) _force0 = gamepad ? KindGamepad : KindJoystick; else _force1 = gamepad ? KindGamepad : KindJoystick;
        return true;
    }

    /// <summary>Gives the ports back to automatic sensing and lets go of the pads.</summary>
    public static void Close()
    {
        if (!_open) return;
        Crt.es_sdl_joy_close();
        _force0 = KindNone;
        _force1 = KindNone;
        _open = false;
    }

    // ---- SDL test hooks: a virtual gamepad (SDL_AttachVirtualJoystick) that a program's self-checks press and move ----

    /// <summary>SDL: connects a virtual gamepad (as if plugged in) and returns its id, or 0. Starts SDL's gamepad support if needed.</summary>
    public static int SdlAttachVirtual() { return Crt.es_sdl_joy_virtual_attach(); }

    /// <summary>SDL: presses or releases a button of a virtual pad. <paramref name="button"/> is an SDL_GamepadButton number: 0 south, 1 east,
    /// 2 west, 3 north, 6 start, 9 left shoulder, 10 right shoulder, 11-14 d-pad up, down, left, right.</summary>
    public static void SdlVirtualButton(int id, int button, bool down) { Crt.es_sdl_joy_virtual_set(id, 0, button, down ? 1 : 0); }

    /// <summary>SDL: moves an axis of a virtual pad (-32768..32767). <paramref name="axis"/> is an SDL_GamepadAxis number: 0 left stick x, 1 left stick y (down is positive).</summary>
    public static void SdlVirtualAxis(int id, int axis, int value) { Crt.es_sdl_joy_virtual_set(id, 1, axis, value); }

    /// <summary>SDL: disconnects a virtual pad.</summary>
    public static void SdlDetachVirtual(int id) { Crt.es_sdl_joy_virtual_detach(id); }

    /// <summary>SDL: a key going down or up in the window, as the keyboard would send it (read at the next <see cref="Input.Poll"/>). Only the
    /// stand-in keys: <paramref name="rawKey"/> is the Amiga raw code of a cursor key (0x4C-0x4F), Space (0x40) or Left Alt (0x64).</summary>
    public static void SdlVirtualKey(int rawKey, bool down) { Crt.es_sdl_joy_virtual_key(rawKey, down); }
#elif !AMIGA
    private static bool _open;
    private static uint _state0, _state1;
    private static int _kind0, _kind1;

    public static bool Open() { _open = true; return true; }

    public static bool IsOpen { get { return _open; } }

    public static uint Read(int port)
    {
        if (!_open) return 0;
        int kind = port == 0 ? _kind0 : (port == 1 ? _kind1 : KindNone);
        if (kind != KindJoystick && kind != KindGamepad) return 0;
        return port == 0 ? _state0 : _state1;
    }

    public static int Kind(int port) { return !_open ? KindNone : (port == 0 ? _kind0 : (port == 1 ? _kind1 : KindNone)); }

    public static bool Force(int port, bool gamepad)
    {
        if (!_open || port < 0 || port > 1) return false;
        if (port == 0) _kind0 = gamepad ? KindGamepad : KindJoystick; else _kind1 = gamepad ? KindGamepad : KindJoystick;
        return true;
    }

    public static void Close() { _open = false; }

    /// <summary>Host: what is plugged into <paramref name="port"/> (0 or 1) and which bits it reports.</summary>
    public static void HostSet(int port, int kind, uint bits)
    {
        if (port == 0) { _kind0 = kind; _state0 = bits; }
        else if (port == 1) { _kind1 = kind; _state1 = bits; }
    }
#endif

    private static bool _keyboard;

    /// <summary>
    /// SDL: when no pad is on port 1, the cursor keys, Space (Fire) and Left Alt (Fire2) read as a joystick there (off by default). The keys
    /// are seen through the <see cref="Video"/> window, so the program must call <see cref="Input.Poll"/> each frame as usual. Other targets
    /// keep the setting and ignore it: an Amiga has a real joystick port, and the host is driven by <c>HostSet</c>.
    /// </summary>
    public static bool KeyboardStandIn { get { return _keyboard; } set { _keyboard = value; } }

    /// <summary>Reads <paramref name="port"/> as a direction: x and y each -1, 0 or 1 (up is -1).</summary>
    public static (int X, int Y) Direction(int port)
    {
        uint s = Read(port);
        int x = (s & Left) != 0 ? -1 : ((s & Right) != 0 ? 1 : 0);
        int y = (s & Up) != 0 ? -1 : ((s & Down) != 0 ? 1 : 0);
        return (x, y);
    }
}
