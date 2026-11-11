using System;

namespace ESharp.Platform;

/// <summary>A looping or one-shot 8-bit signed sample in chip RAM (what the Paula audio channels read).</summary>
public unsafe struct Sample
{
    public byte* Data;
    public int Length;
    /// <summary>Samples per second the sound was recorded at (0 for the generated ones); <see cref="Sound.PeriodForRate"/> turns it into a period.</summary>
    public int Rate;
}

/// <summary>
/// Sound through audio.device: up to four voices (one per Paula channel, allocated when <see cref="Open"/> runs),
/// each playing an 8-bit sample at a period and volume. Samples are made in code (<see cref="Square"/>,
/// <see cref="Triangle"/>, <see cref="Noise"/>) so no sound file is needed. On SDL each voice is an SDL audio stream. On the host nothing is played: the
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

    /// <summary>The longest sample audio.device takes in one write (a longer file is cut off).</summary>
    public const int MaxLength = 131070;

    /// <summary>Period that plays a sample recorded at <paramref name="rate"/> samples per second at its natural pitch.</summary>
    public static int PeriodForRate(int rate)
    {
        if (rate <= 0) return 65535;
        int p = 3546895 / rate;
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

    private static uint Le32(ReadOnlySpan<byte> d, int o) => (uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24);

    private static uint Be32(ReadOnlySpan<byte> d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | (uint)d[o + 3];

    private static int Be16(ReadOnlySpan<byte> d, int o) => (d[o] << 8) | d[o + 1];

    private static bool Is(ReadOnlySpan<byte> d, int o, string tag) =>
        o >= 0 && o + 4 <= d.Length && d[o] == tag[0] && d[o + 1] == tag[1] && d[o + 2] == tag[2] && d[o + 3] == tag[3];

    /// <summary>
    /// Turns a RIFF WAV file (PCM, 8 or 16 bit, mono or stereo) already in memory into an 8-bit mono sample in chip RAM; stereo is averaged.
    /// <see cref="Sample.Rate"/> is the file's rate. An empty sample (<c>Data == null</c>) means the file is not such a WAV or memory ran out.
    /// </summary>
    public static Sample LoadWav(ReadOnlySpan<byte> d)
    {
        Sample none = default;
        if (d.Length < 12 || !Is(d, 0, "RIFF") || !Is(d, 8, "WAVE")) return none;
        int format = 0, channels = 0, bits = 0, rate = 0, dataPos = -1, dataLen = 0;
        int p = 12;
        while (p + 8 <= d.Length)
        {
            uint chunk = Le32(d, p + 4);
            int body = p + 8;
            int room = d.Length - body;
            int len = chunk > (uint)room ? room : (int)chunk;
            if (Is(d, p, "fmt ") && len >= 16)
            {
                format = d[body] | (d[body + 1] << 8);
                channels = d[body + 2] | (d[body + 3] << 8);
                rate = (int)Le32(d, body + 4);
                bits = d[body + 14] | (d[body + 15] << 8);
            }
            else if (Is(d, p, "data"))
            {
                dataPos = body;
                dataLen = len;
                break;
            }
            p = body + len + (len & 1);
        }
        if (format != 1 || channels < 1 || channels > 2 || (bits != 8 && bits != 16) || dataPos < 0 || rate <= 0) return none;
        int bytesPer = bits / 8;
        int frames = dataLen / (channels * bytesPer);
        if (frames < 1) return none;
        if (frames > MaxLength) frames = MaxLength;
        Sample s = Alloc(frames);
        if (s.Data == null) return none;
        s.Rate = rate;
        for (int i = 0; i < s.Length && i < frames; i++)
        {
            int sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int o = dataPos + (i * channels + c) * bytesPer;
                if (bits == 8) sum += d[o] - 128;
                else
                {
                    int v = d[o] | (d[o + 1] << 8);
                    if (v >= 32768) v -= 65536;
                    sum += v >> 8;
                }
            }
            s.Data[i] = (byte)((sum / channels) & 255);
        }
        return s;
    }

    /// <summary>
    /// Turns an IFF 8SVX file already in memory (uncompressed or Fibonacci-delta) into a sample in chip RAM. Only the first (highest) octave is
    /// kept and the loop points are ignored: the whole sample plays. <see cref="Sample.Rate"/> is the file's rate.
    /// An empty sample (<c>Data == null</c>) means the file is not such an 8SVX or memory ran out.
    /// </summary>
    public static Sample Load8svx(ReadOnlySpan<byte> d)
    {
        Sample none = default;
        if (d.Length < 12 || !Is(d, 0, "FORM") || !Is(d, 8, "8SVX")) return none;
        int oneShot = 0, repeat = 0, rate = 0, octaves = 1, compression = 0, bodyPos = -1, bodyLen = 0;
        int p = 12;
        while (p + 8 <= d.Length)
        {
            uint chunk = Be32(d, p + 4);
            int body = p + 8;
            int room = d.Length - body;
            int len = chunk > (uint)room ? room : (int)chunk;
            if (Is(d, p, "VHDR") && len >= 16)
            {
                oneShot = (int)Be32(d, body);
                repeat = (int)Be32(d, body + 4);
                rate = Be16(d, body + 12);
                octaves = d[body + 14];
                compression = d[body + 15];
            }
            else if (Is(d, p, "BODY"))
            {
                bodyPos = body;
                bodyLen = len;
                break;
            }
            p = body + len + (len & 1);
        }
        if (bodyPos < 0 || compression > 1 || bodyLen < 1) return none;
        if (rate <= 0) rate = 8363;
        int first = oneShot + repeat;
        int count;
        if (compression == 0)
        {
            count = bodyLen;
            if (octaves > 1 && first > 0 && first < count) count = first;
        }
        else
        {
            if (bodyLen < 3) return none;
            count = (bodyLen - 2) * 2;
            if (octaves > 1 && first > 0 && first < count) count = first;
        }
        if (count > MaxLength) count = MaxLength;
        Sample s = Alloc(count);
        if (s.Data == null) return none;
        s.Rate = rate;
        if (compression == 0)
        {
            for (int i = 0; i < s.Length && i < count; i++) s.Data[i] = d[bodyPos + i];
        }
        else
        {
            int[] fib = { -34, -21, -13, -8, -5, -3, -2, -1, 0, 1, 2, 3, 5, 8, 13, 21 };
            int v = d[bodyPos + 1];
            if (v >= 128) v -= 256;
            for (int i = 0; i < s.Length && i < count; i++)
            {
                int packed = d[bodyPos + 2 + i / 2];
                int nibble = (i & 1) == 0 ? (packed >> 4) & 15 : packed & 15;
                v += fib[nibble];
                s.Data[i] = (byte)(v & 255);
            }
        }
        return s;
    }

    /// <summary>
    /// Loads a sound file (WAV or IFF 8SVX, told apart by the file's first bytes) from <paramref name="path"/> into chip RAM.
    /// An empty sample (<c>Data == null</c>) means the file is missing, is some other format or memory ran out. Free it with <see cref="Free"/>.
    /// </summary>
    public static Sample Load(string path)
    {
        Sample none = default;
        byte* file = Files.ReadAll(path, out int size, false);
        if (file == null) return none;
        ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>(file, size);
        Sample s = Is(bytes, 0, "RIFF") ? LoadWav(bytes) : Load8svx(bytes);
        Mem.Free(file);
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
    private static nuint[] _io = new nuint[Voices];
    private static bool[] _pending = new bool[Voices];
    private static byte* _map;
    private static void* _port;
    private static int _open;
    private static int _next;

    // struct IOAudio: size and field offsets, measured per target (Layout.g.cs).
    private const int IoSize = Layout.IoSize;
    private const int OffType = Layout.OffType, OffReplyPort = Layout.OffReplyPort, OffLength = Layout.OffLength, OffCommand = Layout.OffCommand;
    private const int OffFlags = Layout.OffFlags, OffError = Layout.OffError, OffAllocKey = Layout.OffAllocKey, OffData = Layout.OffData;
    private const int OffDataLength = Layout.OffDataLength, OffPeriod = Layout.OffPeriod, OffVolume = Layout.OffVolume, OffCycles = Layout.OffCycles;
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
            io[OffType] = 5;                                            // ln_Type = NT_MESSAGE
            *(void**)(io + OffReplyPort) = _port;
            *(ushort*)(io + OffLength) = IoSize;
            *(ushort*)(io + OffCommand) = AdcmdAllocate;
            io[OffFlags] = AdIoNoWait;
            *(ushort*)(io + OffAllocKey) = 0;
#if AROS
            // AROS's audio.device shares its channels between allocations: with "any one channel" a later voice takes an earlier
            // one's channel and that voice's writes fail with ADIOERR_NOALLOCATION. Asking for one fixed channel per voice avoids that.
            _map[0] = (byte)(1 << v);
            *(uint*)(io + OffDataLength) = 1;
#else
            *(uint*)(io + OffDataLength) = 4;
#endif
            *(void**)(io + OffData) = _map;
            if (Exec.OpenDevice("audio.device", 0, io, 0) != 0) { Mem.Free(io); continue; }
            _io[_open++] = (nuint)io;
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
#elif SDL
    // SDL: four SDL audio streams on the default output device, each fed the signed 8-bit sample at 3546895 / period
    // samples per second (the rate Paula would read it at), with the volume as the stream's gain.
    private static int _open;
    private static int _next;

    public static bool Open()
    {
        if (_open != 0) return true;
        _open = Crt.es_sdl_sound_open();
        return _open > 0;
    }

    public static int VoiceCount { get { return _open; } }

    public static bool Busy(int voice) { return voice >= 0 && voice < _open && Crt.es_sdl_sound_busy(voice); }

    public static int Play(in Sample sample, int period, int volume, int cycles)
    {
        if (_open == 0 || sample.Data == null || sample.Length < 4) return -1;
        int v = -1;
        for (int i = 0; i < _open; i++) if (!Crt.es_sdl_sound_busy(i)) { v = i; break; }
        if (v < 0) v = _next % _open;
        _next = v + 1;
        int p = period < 124 ? 124 : (period > 65535 ? 65535 : period);
        Crt.es_sdl_sound_play(v, sample.Data, sample.Length, 3546895 / p, volume < 0 ? 0 : (volume > 64 ? 64 : volume), cycles);
        return v;
    }

    public static void Stop() { for (int v = 0; v < _open; v++) Crt.es_sdl_sound_stop(v); }

    public static void Close()
    {
        if (_open == 0) return;
        Crt.es_sdl_sound_close();
        _open = 0;
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
