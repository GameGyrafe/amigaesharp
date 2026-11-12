// Amiga-only (GadTools, ASL, iffparse, diskfont, lowlevel): not part of the sdl target, where using it is a compile error (ES0102).
#if !SDL
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
    // Host: our own bit values (the Amiga ones come from the system headers).
    public const uint Up = 8, Down = 4, Left = 2, Right = 1;
    public const uint Fire = 1u << 22, Fire2 = 1u << 23;
    public const uint Yellow = 1u << 21, Green = 1u << 20;
    public const uint Forward = 1u << 19, Reverse = 1u << 18, Play = 1u << 17;

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

    /// <summary>Reads <paramref name="port"/> as a direction: x and y each -1, 0 or 1 (up is -1).</summary>
    public static (int X, int Y) Direction(int port)
    {
        uint s = Read(port);
        int x = (s & Left) != 0 ? -1 : ((s & Right) != 0 ? 1 : 0);
        int y = (s & Up) != 0 ? -1 : ((s & Down) != 0 ? 1 : 0);
        return (x, y);
    }
}
#endif
