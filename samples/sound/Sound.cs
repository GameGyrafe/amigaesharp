using System;
using ESharp;
using ESharp.Platform;

namespace SoundSample;

/// <summary>
/// Plays generated samples through audio.device and checks that the device accepts them: a voice is busy while a
/// sample plays and free afterwards, voices are independent, Stop silences them. Prints "ok NAME" or "FAIL NAME".
/// What it cannot check is how it sounds. Where no audio hardware works (<see cref="Sound.Available"/> is false; on a
/// classic Amiga that is a failure) it prints a note and "skip NAME" for the playback checks.
/// </summary>
public static unsafe class Program
{
    static bool _skip;

    static void Check(bool ok, string name)
    {
        Dos.PutStr(_skip ? "skip " : ok ? "ok " : "FAIL ");
        Dos.PutStr(name);
        Dos.PutStr("\n");
        Dos.FlushOutput();      // so a program that hangs in the audio device still shows how far it got
    }

    static void Le16(Span<byte> b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    static void Le32(Span<byte> b, int o, int v) { Le16(b, o, v); Le16(b, o + 2, v >> 16); }
    static void Be16(Span<byte> b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    static void Be32(Span<byte> b, int o, int v) { Be16(b, o, v >> 16); Be16(b, o + 2, v); }
    static void Tag(Span<byte> b, int o, string t) { for (int i = 0; i < 4; i++) b[o + i] = (byte)t[i]; }

    /// <summary>A WAV header for <paramref name="dataLen"/> bytes of PCM; the data follows at offset 44.</summary>
    static void WavHeader(Span<byte> b, int channels, int rate, int bits, int dataLen)
    {
        Tag(b, 0, "RIFF"); Le32(b, 4, 36 + dataLen); Tag(b, 8, "WAVE");
        Tag(b, 12, "fmt "); Le32(b, 16, 16); Le16(b, 20, 1); Le16(b, 22, channels); Le32(b, 24, rate);
        Le32(b, 28, rate * channels * bits / 8); Le16(b, 32, channels * bits / 8); Le16(b, 34, bits);
        Tag(b, 36, "data"); Le32(b, 40, dataLen);
    }

    /// <summary>An 8SVX header for <paramref name="bodyLen"/> body bytes (even); the body follows at offset 48.</summary>
    static void SvxHeader(Span<byte> b, int rate, int compression, int bodyLen, int samples)
    {
        Tag(b, 0, "FORM"); Be32(b, 4, 4 + 28 + 8 + bodyLen); Tag(b, 8, "8SVX");
        Tag(b, 12, "VHDR"); Be32(b, 16, 20); Be32(b, 20, samples); Be32(b, 24, 0); Be32(b, 28, 0);
        Be16(b, 32, rate); b[34] = 1; b[35] = (byte)compression; Be32(b, 36, 0x10000);
        Tag(b, 40, "BODY"); Be32(b, 44, bodyLen);   // the VHDR chunk is 20 bytes, so BODY starts at 12 + 8 + 20
    }

    static bool SameBytes(in Sample s, ReadOnlySpan<byte> want)
    {
        if (s.Data == null || s.Length < want.Length) return false;
        for (int i = 0; i < want.Length; i++) if (s.Data[i] != want[i]) return false;
        return true;
    }

    static void LoadingChecks()
    {
        // WAV, 8 bit mono: unsigned bytes become signed.
        byte[] wav8 = new byte[52];
        WavHeader(wav8, 1, 22050, 8, 8);
        byte[] raw8 = { 128, 255, 128, 1, 128, 200, 60, 128 };
        for (int i = 0; i < 8; i++) wav8[44 + i] = raw8[i];
        Sample a = Sound.LoadWav(wav8);
        byte[] want8 = { 0, 127, 0, 129, 0, 72, 188, 0 };
        Check(a.Length == 8 && a.Rate == 22050 && SameBytes(in a, want8), "WAV 8 bit mono");

        // WAV, 16 bit stereo: the top byte of each channel, averaged.
        byte[] wav16 = new byte[44 + 16];
        WavHeader(wav16, 2, 11025, 16, 16);
        int[] pcm = { 16384, 8192, -16384, -8192, 0, 0, 32767, 32767 };
        for (int i = 0; i < 8; i++) Le16(wav16, 44 + i * 2, pcm[i]);
        Sample b = Sound.LoadWav(wav16);
        byte[] want16 = { 48, 208, 0, 127 };
        Check(b.Length == 4 && b.Rate == 11025 && SameBytes(in b, want16), "WAV 16 bit stereo");

        // 8SVX, uncompressed.
        byte[] svx = new byte[56];
        SvxHeader(svx, 8000, 0, 8, 8);
        byte[] body = { 0, 64, 127, 64, 0, 192, 129, 192 };
        for (int i = 0; i < 8; i++) svx[48 + i] = body[i];
        Sample c = Sound.Load8svx(svx);
        Check(c.Length == 8 && c.Rate == 8000 && SameBytes(in c, body), "8SVX");

        // 8SVX, Fibonacci-delta: start at 10, then +1 +2 +3 +5 -1 -2 -3 -5.
        byte[] fib = new byte[54];
        SvxHeader(fib, 8000, 1, 6, 8);
        byte[] packed = { 0, 10, 0x9A, 0xBC, 0x76, 0x54 };
        for (int i = 0; i < 6; i++) fib[48 + i] = packed[i];
        Sample d = Sound.Load8svx(fib);
        byte[] wantFib = { 11, 13, 16, 21, 20, 18, 15, 10 };
        Check(d.Length == 8 && SameBytes(in d, wantFib), "8SVX Fibonacci delta");

        // Not sounds at all, and files.
        byte[] junk = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        Sample j1 = Sound.LoadWav(junk);
        Sample j2 = Sound.Load8svx(junk);
        Check(j1.Data == null && j2.Data == null && Sound.LoadWav(svx).Data == null, "other data is refused");

#if AMIGA
        string wavPath = "RAM:esharp_test.wav";
        string svxPath = "RAM:esharp_test.8svx";
        string badPath = "RAM:esharp_test.bin";
#else
        string wavPath = "esharp_test.wav";
        string svxPath = "esharp_test.8svx";
        string badPath = "esharp_test.bin";
#endif
        bool wrote = Files.WriteAll(wavPath, wav8) && Files.WriteAll(svxPath, svx) && Files.WriteAll(badPath, junk);
        Sample f1 = Sound.Load(wavPath);
        Sample f2 = Sound.Load(svxPath);
        Sample f3 = Sound.Load(badPath);
        Sample f4 = Sound.Load("esharp_no_such_file.wav");
        Check(wrote && SameBytes(in f1, want8) && f1.Rate == 22050 && SameBytes(in f2, body) && f2.Rate == 8000, "files are written and loaded back");
        Check(f3.Data == null && f4.Data == null, "an unknown or missing file gives an empty sample");
        Files.Delete(wavPath);
        Files.Delete(svxPath);
        Files.Delete(badPath);
        Check(Sound.Load(wavPath).Data == null, "the file is deleted");
        Check(Sound.PeriodForRate(8000) == 443 && Sound.PeriodForRate(1) == 65535 && Sound.PeriodForRate(1000000) == 124, "period for a sample rate");

        Sound.Free(ref a); Sound.Free(ref b); Sound.Free(ref c); Sound.Free(ref d); Sound.Free(ref f1); Sound.Free(ref f2);
    }

    [EntryPoint]
    public static void Main()
    {
        LoadingChecks();

        Check(Sound.PeriodFor(440, 32) == 251 && Sound.PeriodFor(1, 2) == 65535 && Sound.PeriodFor(100000, 32) == 124, "period for a frequency");
        Sample square = Sound.Square(32);
        Sample tri = Sound.Triangle(32);
        Sample noise = Sound.Noise(256, 7);
        Check(square.Data != null && square.Length == 32 && tri.Length == 32 && noise.Length == 256, "samples are allocated");
        Check((sbyte)square.Data[0] == 127 && (sbyte)square.Data[31] == -127 && (sbyte)tri.Data[0] == -127 && (sbyte)tri.Data[16] == 127, "sample contents");

        if (!Sound.Available)
        {
#if AMIGAOS3
            Check(false, "audio.device opens");             // Paula and audio.device are always there on a classic Amiga
            return;
#else
            Dos.PutStr("note: no working sound hardware here (audio.device did not open), so the playback checks are skipped\n");
            _skip = true;
#endif
        }
        Sound.Open();
        Check(Sound.VoiceCount >= 1, "audio.device opens and gives a channel");

        int v = Sound.Play(in square, Sound.PeriodFor(440, 32), 40, 40);
        Check(v >= 0 && Sound.Busy(v), "a playing voice is busy");
        Dos.Delay(10);
        Check(!Sound.Busy(v), "the voice is free when the sample has finished");

        int a = Sound.Play(in tri, 300, 64, 0);
        Check(a >= 0 && Sound.Busy(a), "a looping sample keeps playing");
        if (_skip || Sound.VoiceCount >= 2)
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
        _skip = false;
        Sound.Free(ref square);
        Sound.Free(ref tri);
        Sound.Free(ref noise);
        Check(square.Data == null && square.Length == 0, "samples are freed");
    }
}
