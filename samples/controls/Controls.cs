using ESharp;
using ESharp.Platform;

namespace ControlsSample;

/// <summary>
/// Game controls and timing: the timer.device E clock (<see cref="Clock"/>) and joysticks / CD32 pads through
/// lowlevel.library (<see cref="Joystick"/>). Prints "ok NAME" or "FAIL NAME". lowlevel.library lives in Libs: on
/// the Workbench disk, so on a system without it the program must still start and every joystick read is 0.
/// The E clock runs at about 709 kHz on PAL (716 kHz NTSC); hosted AROS reports 200 ticks per second.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    // On AmigaOS 4 lowlevel.library accepts a forced port type but, with no pad or joystick attached (as in a VM),
    // ReadJoyPort answers JP_TYPE_NOTAVAIL: the documented "nothing usable here" result, not an E# failure. Classic
    // Amigas, AROS and the host must report the forced kind exactly.
    static bool KindOrNotAvail(int wanted)
    {
        int kind = Joystick.Kind(1);
#if PPC
        if (kind == Joystick.KindNone)
        {
            Dos.PutStr("note: lowlevel reports port 1 as not available (no device attached); the forced kind cannot be read back\n");
            return true;
        }
#endif
        return kind == wanted;
    }

    [EntryPoint]
    public static void Main()
    {
        Check(Clock.Ticks == 0 && Clock.Frequency == 0, "the clock reads 0 before Open");
        Check(Clock.Open(), "timer.device opens");
        int f = Clock.Frequency;
        Dos.PutStr($"clock {f} ticks per second\n");
        Check(f > 0, "the clock has a frequency");
        Check(Clock.Microseconds(0, f) == 1000000 && Clock.Milliseconds(0, 3 * f) == 3000, "ticks convert to time");
        long t0 = Clock.Ticks;
        Dos.Delay(10);                                                    // 200 ms
        long t1 = Clock.Ticks;
        int ms = Clock.Milliseconds(t0, t1);
        Check(t1 > t0, "the clock moves");
        Check(ms >= 150 && ms <= 2000, "Delay(10) measures about 200 ms");
        Clock.Close();
        Check(!Clock.IsOpen && Clock.Ticks == 0, "the clock closes");

        if (Joystick.Open())
        {
            Dos.PutStr("lowlevel.library present\n");
            Joystick.Kind(1);                                             // the first read starts the sensing
            Dos.PutStr($"port 1 kind {Joystick.Kind(1)} bits {Joystick.Read(1)}\n");
            // lowlevel 40.x on 3.1 takes a forced port type; hosted AROS's lowlevel refuses it (returns false), which is not an error.
            if (Joystick.Force(1, true))
            {
                Check(KindOrNotAvail(Joystick.KindGamepad), "port 1 set to a CD32 pad reads as a pad");
                Check(Joystick.Force(1, false), "port 1 can be set to a joystick");
                Check(KindOrNotAvail(Joystick.KindJoystick), "port 1 then reads as a joystick");
            }
            else Dos.PutStr("this lowlevel.library does not take a forced port type\n");
            var (x, y) = Joystick.Direction(1);
            Check(x >= -1 && x <= 1 && y >= -1 && y <= 1, "a direction is -1, 0 or 1");
            Joystick.Close();
            Check(!Joystick.IsOpen && Joystick.Read(1) == 0, "lowlevel closes");
        }
        else
        {
            Dos.PutStr("no lowlevel.library\n");
            Check(Joystick.Read(1) == 0 && Joystick.Kind(1) == Joystick.KindNone, "without lowlevel every read is 0");
            Check(!Joystick.Force(1, true), "without lowlevel a port cannot be set");
        }
    }
}
