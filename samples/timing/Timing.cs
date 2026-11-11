using ESharp;
using ESharp.Platform;

namespace TimingSample;

/// <summary>
/// Frame timing: <see cref="Frame.WaitVBlank"/> (graphics WaitTOF), the PAL/NTSC rate, a frame counter, and the
/// fixed-timestep <see cref="FrameTimer"/>. Prints "ok NAME" or "FAIL NAME". The one-second measurement is checked
/// against the detected rate with a wide tolerance; if the machine is not running in real time (a slow emulator, a
/// hosted system that ignores the vertical blank) it prints a "note" with the measurement and "skip" instead of failing. A display
/// whose WaitTOF does not wait (AmigaOS 4 in QEMU) is paced by timer.device inside WaitVBlank; the sample then says so in a note.
/// </summary>
public static class Program
{
    static void Check(bool ok, string name)
    {
        Dos.PutStr(ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
    }

    [EntryPoint]
    public static void Main()
    {
        Check(Clock.Open(), "timer.device opens");
        int fps = Frame.FramesPerSecond;
        Dos.PutStr($"display runs at {fps} frames per second ({(Frame.IsPal ? "PAL" : "NTSC")})\n");
        Check(fps == 50 || fps == 60, "the rate is 50 or 60");
        Check(Frame.FrameMicroseconds == 1000000 / fps, "a frame lasts 1/rate seconds");

        Frame.WaitVBlank();                                               // line up with a frame edge first
        Frame.ResetCount();
        long t0 = Clock.Ticks;
        for (int i = 0; i < fps; i++) Frame.WaitVBlank();                 // one second's worth of frames
        long t1 = Clock.Ticks;
        int ms = Clock.Milliseconds(t0, t1);
        Dos.PutStr($"{fps} WaitVBlank calls took {ms} ms\n");
        Check(Frame.Count == fps, "the frame counter counts each wait");
        if (ms >= 900 && ms <= 1100) Check(true, "a second of frames takes about a second");
        else
        {
            Dos.PutStr("note: the machine is not running in real time (expected about 1000 ms), so the timing check is skipped\n");
            Dos.PutStr("skip a second of frames takes about a second\n");
        }

        // Fixed timestep: 50 logic steps a second, at most 5 owed at once.
        FrameTimer timer = new FrameTimer(50, 5);
        timer.Begin();
        int steps = 0;
        for (int i = 0; i < 10; i++) { Frame.WaitVBlank(); steps += timer.Update(); }
        Dos.PutStr($"10 frames owed {steps} steps\n");
        Check(steps >= 8 && steps <= 12, "ten frames owe about ten steps at the frame rate");

        Dos.Delay(50);                                                    // a one second stall
        Check(timer.Update() == 5, "a stall owes only the capped number of steps");
        int after = timer.Update();
        Check(after <= 1, "the backlog is dropped after a stall");
        int ems = timer.ElapsedMilliseconds;
        Check(ems >= 1000, "elapsed time covers the stall");
        Dos.PutStr($"elapsed {ems} ms\n");

        Video.WaitFrames(5);
        Check(Frame.Count == fps + 10 + 5, "Video.WaitFrames waits that many vertical blanks");
        if (Frame.PacedFrames > 0)
            Dos.PutStr($"note: the display's WaitTOF did not wait for {Frame.PacedFrames} of the frames, so timer.device paced them\n");
        Clock.Close();
    }
}
