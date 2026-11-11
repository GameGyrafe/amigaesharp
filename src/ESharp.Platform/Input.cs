namespace ESharp.Platform;

/// <summary>
/// Mouse and keyboard through the <see cref="Video"/> window (IDCMP messages). Call <see cref="Poll"/> once per frame;
/// it reads everything that arrived since the last call. Positions are screen pixels, keys are Amiga raw key codes.
/// On SDL the events come from the window: the mouse in screen pixels (the window is scaled), keys mapped to the Amiga raw codes of the
/// same keys (US positions), and closing the window counts as Esc. On the host nothing arrives by itself: tests feed events with the Host* methods.
/// </summary>
public static unsafe class Input
{
    public const int KeyEscape = 0x45, KeySpace = 0x40, KeyReturn = 0x44;

    private static int _mouseX;
    private static int _mouseY;
    private static bool _leftDown;
    private static bool _leftPressed;
    private static bool _leftReleased;
    private static int _pressX;
    private static int _pressY;
    private static int _lastKey;
    private static int _lastChar;
    private static int _lastQualifier;

    public static int MouseX { get { return _mouseX; } }
    public static int MouseY { get { return _mouseY; } }

    /// <summary>The left button is down.</summary>
    public static bool LeftDown { get { return _leftDown; } }

    /// <summary>The left button went down since the last <see cref="Poll"/>.</summary>
    public static bool LeftPressed { get { return _leftPressed; } }

    /// <summary>Where the pointer was when the left button last went down (what a click should act on, even if the pointer moved before <see cref="Poll"/>).</summary>
    public static int PressX { get { return _pressX; } }

    public static int PressY { get { return _pressY; } }

    /// <summary>The left button went up since the last <see cref="Poll"/>.</summary>
    public static bool LeftReleased { get { return _leftReleased; } }

    /// <summary>The raw key code of the last key pressed since the last <see cref="Poll"/>, or 0.</summary>
    public static int LastKey { get { return _lastKey; } }

    /// <summary>
    /// The character the last key pressed since the last <see cref="Poll"/> typed, mapped with the user's keymap
    /// (<see cref="Keys"/>): an ISO-8859-1 code, or 0 when it typed nothing (cursor, function and modifier keys, or a
    /// dead key waiting for the next one). Return is 13, backspace 8, escape 27.
    /// </summary>
    public static int LastChar { get { return _lastChar; } }

    /// <summary>The modifier keys held for <see cref="LastKey"/> (the <see cref="Keys"/> qualifier bits, such as <see cref="Keys.Shift"/>).</summary>
    public static int LastQualifier { get { return _lastQualifier; } }

    /// <summary>Reads pending input. Clears the "since last poll" flags first.</summary>
    public static void Poll()
    {
        _leftPressed = false;
        _leftReleased = false;
        _lastKey = 0;
        _lastChar = 0;
        _lastQualifier = 0;
#if AMIGA
        void* window = Video.Window;
        if (window == null) return;
        void* port = *(void**)((byte*)window + Intuition.WindowUserPort);
        while (true)
        {
            byte* msg = (byte*)Exec.GetMsg(port);
            if (msg == null) break;
            uint cls = *(uint*)(msg + Intuition.MsgClass);
            int code = *(ushort*)(msg + Intuition.MsgCode);
            int x = *(short*)(msg + Intuition.MsgMouseX);
            int y = *(short*)(msg + Intuition.MsgMouseY);
            if (cls == Intuition.IDCMP_RAWKEY && (code & 0x80) == 0)
            {
                // Map while the message is still ours: IAddress points at the previous-keys data for dead keys.
                int qual = *(ushort*)(msg + Layout.MsgQualifier);
                void** prev = *(void***)(msg + Layout.MsgIAddress);
                _lastKey = code;
                _lastQualifier = qual;
                _lastChar = Keys.Map(code, qual, prev != null ? *prev : null);
            }
            Exec.ReplyMsg(msg);
            _mouseX = x;
            _mouseY = y;
            if (cls == Intuition.IDCMP_MOUSEBUTTONS)
            {
                if (code == 0x68) { _leftDown = true; _leftPressed = true; _pressX = x; _pressY = y; }      // SELECTDOWN
                else if (code == 0xE8) { _leftDown = false; _leftReleased = true; } // SELECTUP
            }
        }
#elif SDL
        // The C layer keeps the state (window events, and the scripted input of esharp run --input); the "since last poll" parts are cleared first.
        Crt.es_sdl_input_clear();
        Crt.es_sdl_poll();
        _mouseX = Crt.es_sdl_input(0);
        _mouseY = Crt.es_sdl_input(1);
        _leftDown = Crt.es_sdl_input(2) != 0;
        _leftPressed = Crt.es_sdl_input(3) != 0;
        _leftReleased = Crt.es_sdl_input(4) != 0;
        _pressX = Crt.es_sdl_input(5);
        _pressY = Crt.es_sdl_input(6);
        _lastKey = Crt.es_sdl_input(7);
        _lastQualifier = Crt.es_sdl_input(8);
        if (_lastKey != 0) _lastChar = Keys.Char(_lastKey, _lastQualifier);
#else
        Apply();
#endif
    }

#if SDL
    // SDL: the same Host* calls as the dotnet host, so a program's input self-checks run on SDL too. They feed the C layer as if
    // the events came from the window: the move at once, the rest at the next Poll.

    public static void HostMove(int x, int y) { _mouseX = x; _mouseY = y; Crt.es_sdl_inject(0, x, y); }

    public static void HostPress() { Crt.es_sdl_inject(1, 0, 0); }

    public static void HostRelease() { Crt.es_sdl_inject(2, 0, 0); }

    public static void HostKey(int code) { Crt.es_sdl_inject(3, code, 0); }

    public static void HostKey(int code, int qualifier) { Crt.es_sdl_inject(3, code, qualifier); }
#endif

#if !AMIGA && !SDL
    /// <summary>Host: moves the pointer. Takes effect for the next <see cref="Poll"/> readers like any real event.</summary>
    public static void HostMove(int x, int y) { _mouseX = x; _mouseY = y; }

    /// <summary>Host: presses the left button; the following <see cref="Poll"/> sees it as <see cref="LeftPressed"/>.</summary>
    public static void HostPress() { _pendingPress = true; }

    public static void HostRelease() { _pendingRelease = true; }

    public static void HostKey(int code) { _pendingKey = code; _pendingQualifier = 0; }

    /// <summary>Host: a key pressed with modifiers held (<see cref="Keys"/> qualifier bits).</summary>
    public static void HostKey(int code, int qualifier) { _pendingKey = code; _pendingQualifier = qualifier; }

    private static bool _pendingPress;
    private static bool _pendingRelease;
    private static int _pendingKey;
    private static int _pendingQualifier;

    /// <summary>Host: applies queued events (called by <see cref="Poll"/> through <see cref="Apply"/>).</summary>
    internal static void Apply()
    {
        if (_pendingPress) { _leftDown = true; _leftPressed = true; _pressX = _mouseX; _pressY = _mouseY; _pendingPress = false; }
        if (_pendingRelease) { _leftDown = false; _leftReleased = true; _pendingRelease = false; }
        if (_pendingKey != 0)
        {
            _lastKey = _pendingKey;
            _lastQualifier = _pendingQualifier;
            _lastChar = Keys.Char(_pendingKey, _pendingQualifier);
            _pendingKey = 0;
        }
    }
#endif
}
