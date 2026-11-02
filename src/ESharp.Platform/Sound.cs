using System;

namespace ESharp.Platform;

/// <summary>A looping or one-shot 8-bit signed sample in chip RAM (what the Paula audio channels read).</summary>
public unsafe struct Sample
{
    public byte* Data;
    public int Length;
}

/// <summary>
/// Sound through audio.device: up to four voices (one per Paula channel, allocated when <see cref="Open"/> runs),
/// each playing an 8-bit sample at a period and volume. Samples are made in code (<see cref="Square"/>,
/// <see cref="Triangle"/>, <see cref="Noise"/>) so no sound file is needed. On the host nothing is played: the
/// calls are recorded so tests can check them. Everything is a no-op until <see cref="Open"/> succeeds.
/// </summary>
public static unsafe class Sound
{
    public const int Voices = 4;

    /// <summary>Period for a sample of <paramref name="sampleLength"/> bytes to repeat at <paramref name="hz"/> (PAL clock, 3546895 / period).</summary>
    public static int PeriodFor(int hz, int sampleLength)
    {
        if (hz <= 0 || sampleLength <= 0) return 65535;
        int p = 3546895 / (hz * sampleLength);
        return p < 124 ? 124 : (p > 65535 ? 65535 : p);
    }

    /// <summary>A square wave (full volume), <paramref name="length"/> bytes (even, at least 4). Free it with <see cref="Free"/>.</summary>
    public static Sample Square(int length)
    {
        Sample s = Alloc(length);
        for (int i = 0; i < s.Length; i++) s.Data[i] = (byte)(i < s.Length / 2 ? 127 : 129);
        return s;
    }

    /// <summary>A triangle wave, <paramref name="length"/> bytes.</summary>
    public static Sample Triangle(int length)
    {
        Sample s = Alloc(length);
        int half = s.Length / 2;
        for (int i = 0; i < s.Length; i++)
        {
            int v = i < half ? -127 + (254 * i) / half : 127 - (254 * (i - half)) / half;
            s.Data[i] = (byte)(v & 255);
        }
        return s;
    }

    /// <summary>White-ish noise from a small linear congruential generator, <paramref name="length"/> bytes.</summary>
    public static Sample Noise(int length, uint seed)
    {
        Sample s = Alloc(length);
        uint x = seed | 1;
        for (int i = 0; i < s.Length; i++)
        {
            x = x * 1103515245 + 12345;
            s.Data[i] = (byte)((x >> 16) & 255);
        }
        return s;
    }

    private static Sample Alloc(int length)
    {
        Sample s = default;
        if (length < 4) length = 4;
        length &= ~1;
        s.Data = Mem.AllocChip((uint)length);
        s.Length = s.Data == null ? 0 : length;
        return s;
    }

    /// <summary>Frees a sample made here. A voice that is still playing it must be stopped first (<see cref="Stop"/>).</summary>
    public static void Free(ref Sample sample)
    {
        Mem.Free(sample.Data);
        sample.Data = null;
        sample.Length = 0;
    }

#if AMIGA
    private static uint[] _io = new uint[Voices];
    private static bool[] _pending = new bool[Voices];
    private static byte* _map;
    private static void* _port;
    private static int _open;
    private static int _next;

    private const int IoSize = 68;               // struct IOAudio
    private const int OffReplyPort = 14, OffLength = 18, OffDevice = 20, OffUnit = 24, OffCommand = 28, OffFlags = 30, OffError = 31;
    private const int OffAllocKey = 32, OffData = 34, OffDataLength = 38, OffPeriod = 42, OffVolume = 44, OffCycles = 46;
    private const int CmdWrite = 3, AdcmdAllocate = 32, AdcmdFree = 33, AdIoPerVol = 16, AdIoNoWait = 64;

    /// <summary>Opens audio.device and tries to allocate a channel for each voice. True if at least one voice is available.</summary>
    public static bool Open()
    {
        if (_open != 0) return true;
        _port = Exec.CreateMsgPort();
        if (_port == null) return false;
        _map = Mem.AllocFast(4);
        if (_map == null) { Exec.DeleteMsgPort(_port); _port = null; return false; }
        _map[0] = 1; _map[1] = 2; _map[2] = 4; _map[3] = 8;      // any one channel
        for (int v = 0; v < Voices; v++)
        {
            byte* io = Mem.AllocFast(IoSize);
            if (io == null) continue;
            io[8] = 5;                                            // ln_Type = NT_MESSAGE
            *(void**)(io + OffReplyPort) = _port;
            *(ushort*)(io + OffLength) = IoSize;
            *(ushort*)(io + OffCommand) = AdcmdAllocate;
            io[OffFlags] = AdIoNoWait;
            *(ushort*)(io + OffAllocKey) = 0;
            *(void**)(io + OffData) = _map;
            *(uint*)(io + OffDataLength) = 4;
            if (Exec.OpenDevice("audio.device", 0, io, 0) != 0) { Mem.Free(io); continue; }
            _io[_open++] = (uint)io;
        }
        if (_open == 0) { Mem.Free(_map); _map = null; Exec.DeleteMsgPort(_port); _port = null; return false; }
        return true;
    }

    /// <summary>The number of voices that <see cref="Open"/> got a channel for (0 before Open).</summary>
    public static int VoiceCount { get { return _open; } }

    private static bool Finished(int v)
    {
        if (!_pending[v]) return true;
        if (Exec.CheckIO((void*)_io[v]) == null) return false;
        Exec.WaitIO((void*)_io[v]);
        _pending[v] = false;
        return true;
    }

    /// <summary>True while the voice is still playing.</summary>
    public static bool Busy(int voice) { return voice >= 0 && voice < _open && !Finished(voice); }

    private static void Halt(int v)
    {
        if (!_pending[v]) return;
        Exec.AbortIO((void*)_io[v]);
        Exec.WaitIO((void*)_io[v]);
        _pending[v] = false;
    }

    /// <summary>
    /// Plays a sample on a free voice (or the voice that was started longest ago), <paramref name="cycles"/> times
    /// (0 = until stopped). <paramref name="period"/> is 124..65535 (see <see cref="PeriodFor"/>), <paramref name="volume"/> 0..64.
    /// Returns the voice used, or -1 if sound is not open or the sample is empty.
    /// </summary>
    public static int Play(in Sample sample, int period, int volume, int cycles)
    {
        if (_open == 0 || sample.Data == null || sample.Length < 4) return -1;
        int v = -1;
        for (int i = 0; i < _open; i++) if (Finished(i)) { v = i; break; }
        if (v < 0) { v = _next % _open; Halt(v); }
        _next = v + 1;
        byte* io = (byte*)_io[v];
        *(ushort*)(io + OffCommand) = CmdWrite;
        io[OffFlags] = AdIoPerVol;
        io[OffError] = 0;
        *(void**)(io + OffData) = sample.Data;
        *(uint*)(io + OffDataLength) = (uint)sample.Length;
        *(ushort*)(io + OffPeriod) = (ushort)period;
        *(ushort*)(io + OffVolume) = (ushort)(volume < 0 ? 0 : (volume > 64 ? 64 : volume));
        *(ushort*)(io + OffCycles) = (ushort)cycles;
        Exec.BeginIO((void*)io);
        _pending[v] = true;
        return v;
    }

    /// <summary>Stops every voice.</summary>
    public static void Stop()
    {
        for (int v = 0; v < _open; v++) Halt(v);
    }

    /// <summary>Stops everything and gives the channels back.</summary>
    public static void Close()
    {
        if (_open == 0) return;
        Stop();
        for (int v = 0; v < _open; v++)
        {
            Exec.CloseDevice((void*)_io[v]);
            Mem.Free((byte*)_io[v]);
            _io[v] = 0;
        }
        _open = 0;
        Mem.Free(_map);
        _map = null;
        Exec.DeleteMsgPort(_port);
        _port = null;
    }
#else
    private static int _open;
    private static int _plays;
    private static int _lastPeriod;
    private static int _lastVolume;
    private static int _lastCycles;
    private static int _lastLength;
    private static int _next;
    private static readonly long[] Remaining = new long[Voices];   // 0 free, -1 until stopped, else 1/3546895 s units left

    public static bool Open() { _open = Voices; return true; }

    public static int VoiceCount { get { return _open; } }

    public static bool Busy(int voice) { return voice >= 0 && voice < _open && Remaining[voice] != 0; }

    public static int Play(in Sample sample, int period, int volume, int cycles)
    {
        if (_open == 0 || sample.Data == null || sample.Length < 4) return -1;
        int v = -1;
        for (int i = 0; i < _open; i++) if (Remaining[i] == 0) { v = i; break; }
        if (v < 0) v = _next % _open;
        _next = v + 1;
        _lastPeriod = period;
        _lastVolume = volume < 0 ? 0 : (volume > 64 ? 64 : volume);
        _lastCycles = cycles;
        _lastLength = sample.Length;
        _plays++;
        Remaining[v] = cycles == 0 ? -1 : (long)cycles * sample.Length * period;   // one byte per period
        return v;
    }

    public static void Stop() { for (int v = 0; v < Voices; v++) Remaining[v] = 0; }

    public static void Close() { Stop(); _open = 0; }

    /// <summary>Host: lets <paramref name="ticks"/> 50ths of a second pass (<see cref="Dos.Delay"/> calls this), finishing voices whose sample has played out.</summary>
    internal static void HostAdvance(int ticks)
    {
        long units = (long)ticks * 3546895 / 50;
        for (int v = 0; v < Voices; v++)
            if (Remaining[v] > 0) Remaining[v] = Remaining[v] > units ? Remaining[v] - units : 0;
    }

    /// <summary>Host: how many samples have been played since <see cref="HostReset"/>.</summary>
    public static int HostPlays { get { return _plays; } }

    public static int HostLastPeriod { get { return _lastPeriod; } }

    public static int HostLastVolume { get { return _lastVolume; } }

    public static int HostLastCycles { get { return _lastCycles; } }

    public static int HostLastLength { get { return _lastLength; } }

    public static void HostReset() { Stop(); _open = 0; _plays = 0; _lastPeriod = 0; _lastVolume = 0; _lastCycles = 0; _lastLength = 0; _next = 0; }
#endif
}
