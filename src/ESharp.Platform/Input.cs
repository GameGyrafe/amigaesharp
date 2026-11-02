namespace ESharp.Platform;

/// <summary>
/// Mouse and keyboard through the <see cref="Video"/> window (IDCMP messages). Call <see cref="Poll"/> once per frame;
/// it reads everything that arrived since the last call. Positions are screen pixels, keys are Amiga raw key codes.
/// On the host nothing arrives by itself: tests feed events with the Host* methods.
/// </summary>
public static unsafe class Input
{
    public const int KeyEscape = 0x45, KeySpace = 0x40, KeyReturn = 0x44;

    private static int _mouseX;
    private static int _mouseY;
    private static bool _leftDown;
    private static bool _leftPressed;
    private static bool _leftReleased;
    private static int _lastKey;

    public static int MouseX { get { return _mouseX; } }
    public static int MouseY { get { return _mouseY; } }

    /// <summary>The left button is down.</summary>
    public static bool LeftDown { get { return _leftDown; } }

    /// <summary>The left button went down since the last <see cref="Poll"/>.</summary>
    public static bool LeftPressed { get { return _leftPressed; } }

    /// <summary>The left button went up since the last <see cref="Poll"/>.</summary>
    public static bool LeftReleased { get { return _leftReleased; } }

    /// <summary>The raw key code of the last key pressed since the last <see cref="Poll"/>, or 0.</summary>
    public static int LastKey { get { return _lastKey; } }

    /// <summary>Reads pending input. Clears the "since last poll" flags first.</summary>
    public static void Poll()
    {
        _leftPressed = false;
        _leftReleased = false;
        _lastKey = 0;
#if !AMIGA
        Apply();
#else
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
            Exec.ReplyMsg(msg);
            _mouseX = x;
            _mouseY = y;
            if (cls == Intuition.IDCMP_MOUSEBUTTONS)
            {
                if (code == 0x68) { _leftDown = true; _leftPressed = true; }      // SELECTDOWN
                else if (code == 0xE8) { _leftDown = false; _leftReleased = true; } // SELECTUP
            }
            else if (cls == Intuition.IDCMP_RAWKEY)
            {
                if ((code & 0x80) == 0) _lastKey = code;
            }
        }
#endif
    }

#if !AMIGA
    /// <summary>Host: moves the pointer. Takes effect for the next <see cref="Poll"/> readers like any real event.</summary>
    public static void HostMove(int x, int y) { _mouseX = x; _mouseY = y; }

    /// <summary>Host: presses the left button; the following <see cref="Poll"/> sees it as <see cref="LeftPressed"/>.</summary>
    public static void HostPress() { _pendingPress = true; }

    public static void HostRelease() { _pendingRelease = true; }

    public static void HostKey(int code) { _pendingKey = code; }

    private static bool _pendingPress;
    private static bool _pendingRelease;
    private static int _pendingKey;

    /// <summary>Host: applies queued events (called by <see cref="Poll"/> through <see cref="Apply"/>).</summary>
    internal static void Apply()
    {
        if (_pendingPress) { _leftDown = true; _leftPressed = true; _pendingPress = false; }
        if (_pendingRelease) { _leftDown = false; _leftReleased = true; _pendingRelease = false; }
        if (_pendingKey != 0) { _lastKey = _pendingKey; _pendingKey = 0; }
    }
#endif
}
