using ESharp;
using ESharp.Platform;

namespace SoundSample;

/// <summary>
/// Plays generated samples through audio.device and checks that the device accepts them: a voice is busy while a
/// sample plays and free afterwards, voices are independent, Stop silences them. Prints "ok NAME" or "FAIL NAME".
/// What it cannot check is how it sounds.
/// </summary>
public static unsafe class Program
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
        Check(Sound.PeriodFor(440, 32) == 251 && Sound.PeriodFor(1, 2) == 65535 && Sound.PeriodFor(100000, 32) == 124, "period for a frequency");
        Sample square = Sound.Square(32);
        Sample tri = Sound.Triangle(32);
        Sample noise = Sound.Noise(256, 7);
        Check(square.Data != null && square.Length == 32 && tri.Length == 32 && noise.Length == 256, "samples are allocated");
        Check((sbyte)square.Data[0] == 127 && (sbyte)square.Data[31] == -127 && (sbyte)tri.Data[0] == -127 && (sbyte)tri.Data[16] == 127, "sample contents");

        if (!Sound.Open())
        {
            Check(false, "audio.device opens");
            return;
        }
        Check(Sound.VoiceCount >= 1, "audio.device opens and gives a channel");

        int v = Sound.Play(in square, Sound.PeriodFor(440, 32), 40, 40);
        Check(v >= 0 && Sound.Busy(v), "a playing voice is busy");
        Dos.Delay(10);
        Check(!Sound.Busy(v), "the voice is free when the sample has finished");

        int a = Sound.Play(in tri, 300, 64, 0);
        Check(a >= 0 && Sound.Busy(a), "a looping sample keeps playing");
        if (Sound.VoiceCount >= 2)
        {
            int b = Sound.Play(in noise, 400, 20, 5);
            Check(b >= 0 && b != a && Sound.Busy(a) && Sound.Busy(b), "two voices play together");
        }
        Sound.Stop();
        Check(!Sound.Busy(a), "Stop silences every voice");
        Check(Sound.Play(in tri, 300, 64, 1) >= 0, "a voice can be reused after Stop");
        Sound.Stop();

        Sound.Close();
        Check(Sound.VoiceCount == 0 && Sound.Play(in tri, 300, 64, 1) == -1, "Close releases the channels");
        Sound.Free(ref square);
        Sound.Free(ref tri);
        Sound.Free(ref noise);
        Check(square.Data == null && square.Length == 0, "samples are freed");
    }
}
