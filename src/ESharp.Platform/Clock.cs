namespace ESharp.Platform;

#if AMIGA
/// <summary>
/// timer.device functions (they are called through the device base, like a library). The device is in ROM;
/// <see cref="Clock"/> opens it and sets the base. Vectors from the classic fd file, checked against AROS.
/// </summary>
[LibraryBase("timer", "TimerBase", "struct Device")]
public static unsafe class TimerDevice
{
    /// <summary>Emitter intrinsic: sets the device base that the calls below use (io_Device of an open timer request, or null afterwards).</summary>
    public static extern void SetBase(void* device);

    /// <summary>Reads the E clock into <paramref name="dest"/> (a struct EClockVal: high and low 32 bits) and returns its frequency in ticks per second.</summary>
    [AmigaLib("timer", -60)] public static extern uint ReadEClock(void* dest);
}
#endif

/// <summary>
/// A fine-grained clock for frame timing and measuring code. On the Amiga it is timer.device's E clock (about
/// 709 kHz on PAL, 716 kHz on NTSC, so a tick is about 1.4 microseconds; hosted AROS reports only 200 per second,
/// so always use <see cref="Frequency"/>). On SDL it is SDL's clock in microseconds (1 MHz). On the host it is a fake that
/// starts at 0, runs at 1 MHz and only moves when <see cref="HostAdvance"/> or <c>Dos.Delay</c> is called, so
/// tests are repeatable. Call <see cref="Open"/> first; before that, and if it fails, every reading is 0.
/// </summary>
public static unsafe class Clock
{
#if AMIGA
    private static byte* _io;
    private static int _frequency;

    /// <summary>Opens timer.device (UNIT_MICROHZ). False if that fails (it should not: the device is in ROM).</summary>
    public static bool Open()
    {
        if (_io != null) return true;
        _io = Mem.AllocFast(Layout.TimeRequestSize);
        if (_io == null) return false;
        _io[Layout.OffType] = 5;                                        // ln_Type = NT_MESSAGE (no reply port: no I/O is sent)
        *(ushort*)(_io + Layout.OffLength) = Layout.TimeRequestSize;
        if (Exec.OpenDevice("timer.device", Layout.TimerUnitMicroHz, _io, 0) != 0) { Mem.Free(_io); _io = null; return false; }
        TimerDevice.SetBase(*(void**)(_io + Layout.OffDevice));
        uint[] ev = new uint[2];
        fixed (uint* p = ev) _frequency = (int)TimerDevice.ReadEClock(p);
        return true;
    }

    public static bool IsOpen { get { return _io != null; } }

    /// <summary>Ticks per second (0 when not open).</summary>
    public static int Frequency { get { return _frequency; } }

    /// <summary>The current count. It only means something as a difference between two readings.</summary>
    public static long Ticks
    {
        get
        {
            if (_io == null) return 0;
            uint[] ev = new uint[2];
            fixed (uint* p = ev)
            {
                TimerDevice.ReadEClock(p);
                uint hi = *(uint*)((byte*)p + Layout.EClockHi), lo = *(uint*)((byte*)p + Layout.EClockLo);
                return (long)(((ulong)hi << 32) | lo);
            }
        }
    }

    public static void Close()
    {
        if (_io == null) return;
        TimerDevice.SetBase(null);
        Exec.CloseDevice(_io);
        Mem.Free(_io);
        _io = null;
        _frequency = 0;
    }
#elif SDL
    // SDL: microseconds from SDL's monotonic nanosecond clock.
    private static bool _open;

    public static bool Open() { _open = true; return true; }

    public static bool IsOpen { get { return _open; } }

    public static int Frequency { get { return _open ? 1000000 : 0; } }

    public static long Ticks { get { return _open ? Crt.es_sdl_ticks_us() : 0; } }

    public static void Close() { _open = false; }
#else
    private static bool _open;
    private static long _now;

    public static bool Open() { _open = true; return true; }

    public static bool IsOpen { get { return _open; } }

    public static int Frequency { get { return _open ? 1000000 : 0; } }

    public static long Ticks { get { return _open ? _now : 0; } }

    public static void Close() { _open = false; }

    /// <summary>Host: moves the fake clock on by <paramref name="microseconds"/>.</summary>
    public static void HostAdvance(long microseconds) { _now += microseconds; }
#endif

    /// <summary>Microseconds between two <see cref="Ticks"/> readings (0 when not open).</summary>
    public static long Microseconds(long fromTicks, long toTicks)
    {
        int f = Frequency;
        if (f <= 0) return 0;
        long d = toTicks - fromTicks;
        return d / f * 1000000 + d % f * 1000000 / f;
    }

    /// <summary>Milliseconds between two <see cref="Ticks"/> readings (0 when not open).</summary>
    public static int Milliseconds(long fromTicks, long toTicks) { return (int)(Microseconds(fromTicks, toTicks) / 1000); }
}
