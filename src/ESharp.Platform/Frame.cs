namespace ESharp.Platform;

/// <summary>
/// Frame timing for game loops: wait for the display's vertical blank, count frames, and know whether the machine
/// runs 50 Hz (PAL) or 60 Hz (NTSC). On the Amiga <see cref="WaitVBlank"/> is graphics.library WaitTOF, so drawing
/// syncs to the display; when WaitTOF comes back well inside a frame (a display without a vertical blank interrupt, such
/// as the emulated sm501 of AmigaOS 4 in QEMU), timer.device waits for the rest of the frame instead (<see cref="PacedFrames"/>).
/// The rate comes from the PAL or NTSC bit of GfxBase.DisplayFlags (set from the chips on classic
/// machines); when neither bit is set (hosted AROS) it is ExecBase.VBlankFrequency. On SDL each <see cref="WaitVBlank"/> shows the
/// screen and paces the loop to a steady 50 frames per second (vsync is not used). On the host it is a deterministic fake: each <see cref="WaitVBlank"/> advances the
/// fake <see cref="Clock"/> by one frame (20000 or 16666 microseconds), so tests can drive a game loop.
/// </summary>
public static unsafe class Frame
{
    private static int _count;
    private static int _fps;
    private static int _paced;

    /// <summary>
    /// Waits that timer.device finished because the display's wait came back too early (well inside a frame). Always 0
    /// on a display with a working vertical blank, on SDL and on the host.
    /// </summary>
    public static int PacedFrames { get { return _paced; } }

    /// <summary>Frames waited for since start (or the last <see cref="ResetCount"/>).</summary>
    public static int Count { get { return _count; } }

    public static void ResetCount() { _count = 0; }

    /// <summary>50 on a PAL machine, 60 on NTSC. Detected on first use.</summary>
    public static int FramesPerSecond
    {
        get
        {
            if (_fps == 0) _fps = Detect();
            return _fps;
        }
    }

    public static bool IsPal { get { return FramesPerSecond == 50; } }

    /// <summary>Microseconds in one frame (20000 on PAL, 16666 on NTSC).</summary>
    public static int FrameMicroseconds { get { return 1000000 / FramesPerSecond; } }

    /// <summary>Forgets the detected rate so the next use detects again.</summary>
    public static void Redetect() { _fps = 0; }

#if AMIGA
    private static int Detect()
    {
        int rate = 0;
        void* gfx = Exec.OpenLibrary("graphics.library", 39);
        if (gfx != null)
        {
            ushort flags = *(ushort*)((byte*)gfx + Layout.GfxDisplayFlags);
            if ((flags & Layout.GfxPalFlag) != 0) rate = 50;
            else if ((flags & Layout.GfxNtscFlag) != 0) rate = 60;
            Exec.CloseLibrary(gfx);
        }
        if (rate != 0) return rate;
        // Neither bit is set (hosted AROS): use exec's own vertical blank frequency. Opening exec.library gives ExecBase.
        void* sys = Exec.OpenLibrary("exec.library", 0);
        if (sys != null)
        {
            int hz = *((byte*)sys + Layout.ExecVBlankFrequency);
            Exec.CloseLibrary(sys);
            if (hz == 50 || hz == 60) return hz;
        }
        return 50;
    }

    // The fallback for a WaitTOF that does not wait: our own timer.device request (UNIT_MICROHZ) with a reply port,
    // opened on the first WaitVBlank and closed after the program's Main returns ([AtExit]).
    private static byte* _tio;
    private static void* _tport;
    private static void* _tbase;
    private static int _efreq;
    private static bool _tried;
    private static long _last;
    private static bool _haveLast;

    private static bool OpenTimer()
    {
        if (_tio != null) return true;
        if (_tried) return false;
        _tried = true;
        _tport = Exec.CreateMsgPort();
        if (_tport == null) return false;
        _tio = Mem.AllocFast(Layout.TimeRequestSize);
        if (_tio == null) { Exec.DeleteMsgPort(_tport); _tport = null; return false; }
        _tio[Layout.OffType] = 5;                                        // ln_Type = NT_MESSAGE
        *(void**)(_tio + Layout.OffReplyPort) = _tport;
        *(ushort*)(_tio + Layout.OffLength) = Layout.TimeRequestSize;
        if (Exec.OpenDevice("timer.device", Layout.TimerUnitMicroHz, _tio, 0) != 0)
        {
            Mem.Free(_tio);
            _tio = null;
            Exec.DeleteMsgPort(_tport);
            _tport = null;
            return false;
        }
        _tbase = *(void**)(_tio + Layout.OffDevice);
        TimerDevice.SetBase(_tbase);
        uint[] ev = new uint[2];
        fixed (uint* p = ev) _efreq = (int)TimerDevice.ReadEClock(p);
        if (_efreq <= 0) { CloseTimer(); return false; }
        return true;
    }

    private static long EClock()
    {
        TimerDevice.SetBase(_tbase);      // Clock.Close clears the shared base; every opener of timer.device gets the same one
        uint[] ev = new uint[2];
        fixed (uint* p = ev)
        {
            TimerDevice.ReadEClock(p);
            uint hi = *(uint*)((byte*)p + Layout.EClockHi), lo = *(uint*)((byte*)p + Layout.EClockLo);
            return (long)(((ulong)hi << 32) | lo);
        }
    }

    [AtExit]
    private static void CloseTimer()
    {
        if (_tio == null) return;
        Exec.CloseDevice(_tio);
        Mem.Free(_tio);
        _tio = null;
        Exec.DeleteMsgPort(_tport);
        _tport = null;
        _haveLast = false;
        _tofBroken = false;
        _quick = 0;
    }

    private static bool _tofBroken;
    private static int _quick;

    /// <summary>
    /// Waits for the top of the next frame (graphics.library WaitTOF) and counts it. The E clock is read around WaitTOF: if
    /// it returns within a tenth of a frame on three calls in a row, or less than half a frame after the previous
    /// WaitVBlank, it did not wait. After three quick returns the display is marked broken for good and from then on every
    /// frame is padded by timer.device to a whole frame from the previous return (so a loop with 0.7 frame of work still
    /// runs at the display rate). A working display never sets the flag and is not paced.
    /// </summary>
    public static void WaitVBlank()
    {
        bool timer = OpenTimer();
        long before = timer ? EClock() : 0;
        Graphics.WaitTOF();
        if (timer)
        {
            long now = EClock();
            long frame = _efreq / FramesPerSecond;                      // E clock ticks per frame
            long gap = now - _last;
            if (now - before >= 0 && now - before < frame / 10) { if (_quick < 3) _quick++; } else _quick = 0;
            if (_quick >= 3) _tofBroken = true;
            if (_haveLast && gap >= 0 && (_tofBroken ? gap < frame : gap < frame / 2))
            {
                long us = (frame - gap) * 1000000 / _efreq;
                *(ushort*)(_tio + Layout.TrCommand) = (ushort)Layout.TimerAddRequest;
                *(uint*)(_tio + Layout.TrSecs) = (uint)(us / 1000000);
                *(uint*)(_tio + Layout.TrMicro) = (uint)(us % 1000000);
                Exec.DoIO(_tio);
                now = EClock();
                _paced++;
            }
            _last = now;
            _haveLast = true;
        }
        _count++;
    }
#elif SDL
    // SDL: a steady 50 frames per second, as on a PAL Amiga, whatever the desktop's refresh rate; each wait shows the screen.
    private static int Detect() { return 50; }

    /// <summary>Shows the screen and waits for the next 1/50 second, then counts the frame.</summary>
    public static void WaitVBlank()
    {
        Crt.es_sdl_wait_frame(FramesPerSecond);
        _count++;
    }
#else
    /// <summary>Host: true for a 50 Hz machine (the default), false for 60 Hz. Call <see cref="Redetect"/> after changing it.</summary>
    public static bool HostPal = true;

    private static int Detect() { return HostPal ? 50 : 60; }

    /// <summary>Host: advances the fake clock by one frame, runs the vertical blank servers of <see cref="Interrupts"/> and counts it.</summary>
    public static void WaitVBlank()
    {
        Clock.HostAdvance(FrameMicroseconds);
        Interrupts.HostVBlank();
        _count++;
    }
#endif

    /// <summary>Waits for <paramref name="frames"/> vertical blanks.</summary>
    public static void Wait(int frames)
    {
        for (int i = 0; i < frames; i++) WaitVBlank();
    }
}

/// <summary>
/// A fixed-timestep helper: the game logic runs in steps of 1/<c>stepsPerSecond</c>, however long a frame took.
/// Call <see cref="Begin"/> once when the loop starts, then once per frame <see cref="Update"/>, which returns how
/// many logic steps are owed. A stall (a disk access, a requester) would owe many steps, so the count is capped at
/// <c>maxSteps</c> and the rest of the backlog is dropped rather than letting the game spiral. Needs <see cref="Clock"/> open.
/// </summary>
public sealed class FrameTimer
{
    private readonly int _stepUs;
    private readonly int _maxSteps;
    private long _last;
    private long _start;
    private long _acc;

    public FrameTimer(int stepsPerSecond, int maxSteps)
    {
        _stepUs = 1000000 / stepsPerSecond;
        _maxSteps = maxSteps;
    }

    /// <summary>Microseconds per logic step.</summary>
    public int StepMicroseconds { get { return _stepUs; } }

    /// <summary>Starts (or restarts) timing: nothing is owed yet.</summary>
    public void Begin()
    {
        _last = Clock.Ticks;
        _start = _last;
        _acc = 0;
    }

    /// <summary>Logic steps owed since the last call (0 up to maxSteps).</summary>
    public int Update()
    {
        long now = Clock.Ticks;
        _acc += Clock.Microseconds(_last, now);
        _last = now;
        long steps = _acc / _stepUs;
        if (steps > _maxSteps) { steps = _maxSteps; _acc = 0; }
        else _acc -= steps * _stepUs;
        return (int)steps;
    }

    /// <summary>Microseconds since <see cref="Begin"/>.</summary>
    public long ElapsedMicroseconds { get { return Clock.Microseconds(_start, Clock.Ticks); } }

    /// <summary>Milliseconds since <see cref="Begin"/>.</summary>
    public int ElapsedMilliseconds { get { return (int)(ElapsedMicroseconds / 1000); } }

    /// <summary>How far into the next step we are, 0..1000 (for interpolating drawing between steps).</summary>
    public int Fraction { get { return (int)(_acc * 1000 / _stepUs); } }
}
