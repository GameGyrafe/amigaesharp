namespace ESharp.Platform;

/// <summary>
/// Frame timing for game loops: wait for the display's vertical blank, count frames, and know whether the machine
/// runs 50 Hz (PAL) or 60 Hz (NTSC). On the Amiga <see cref="WaitVBlank"/> is graphics.library WaitTOF, so drawing
/// syncs to the display; the rate comes from the PAL or NTSC bit of GfxBase.DisplayFlags (set from the chips on classic
/// machines); when neither bit is set (hosted AROS) it is ExecBase.VBlankFrequency. On the host it is a deterministic fake: each <see cref="WaitVBlank"/> advances the
/// fake <see cref="Clock"/> by one frame (20000 or 16666 microseconds), so tests can drive a game loop.
/// </summary>
public static unsafe class Frame
{
    private static int _count;
    private static int _fps;

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

    /// <summary>Waits for the top of the next frame (graphics.library WaitTOF) and counts it.</summary>
    public static void WaitVBlank()
    {
        Graphics.WaitTOF();
        _count++;
    }
#else
    /// <summary>Host: true for a 50 Hz machine (the default), false for 60 Hz. Call <see cref="Redetect"/> after changing it.</summary>
    public static bool HostPal = true;

    private static int Detect() { return HostPal ? 50 : 60; }

    /// <summary>Host: advances the fake clock by one frame and counts it.</summary>
    public static void WaitVBlank()
    {
        Clock.HostAdvance(FrameMicroseconds);
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
