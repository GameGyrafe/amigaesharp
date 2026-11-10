using System;
using System.IO;

namespace ESharp.Assets;

/// <summary>An 8-bit signed mono sample and the rate it was recorded at.</summary>
public sealed record SoundData(sbyte[] Data, int Rate);

/// <summary>Reads RIFF WAV (PCM, 8 or 16 bit, mono or stereo; stereo is averaged) and IFF 8SVX (raw or Fibonacci-delta, first octave) files.</summary>
public static class SoundFile
{
    private static uint Le32(byte[] d, int o) => (uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24);
    private static uint Be32(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];
    private static bool Is(byte[] d, int o, string t) => o >= 0 && o + 4 <= d.Length && d[o] == t[0] && d[o + 1] == t[1] && d[o + 2] == t[2] && d[o + 3] == t[3];

    public static SoundData Read(byte[] d)
    {
        if (Is(d, 0, "RIFF") && Is(d, 8, "WAVE")) return ReadWav(d);
        if (Is(d, 0, "FORM") && Is(d, 8, "8SVX")) return Read8svx(d);
        throw new InvalidDataException("not a WAV or 8SVX sound file");
    }

    private static SoundData ReadWav(byte[] d)
    {
        int format = 0, channels = 0, bits = 0, rate = 0, dataPos = -1, dataLen = 0;
        var p = 12;
        while (p + 8 <= d.Length)
        {
            var chunk = Le32(d, p + 4);
            var body = p + 8;
            var room = d.Length - body;
            var len = chunk > (uint)room ? room : (int)chunk;
            if (Is(d, p, "fmt ") && len >= 16)
            {
                format = d[body] | (d[body + 1] << 8);
                channels = d[body + 2] | (d[body + 3] << 8);
                rate = (int)Le32(d, body + 4);
                bits = d[body + 14] | (d[body + 15] << 8);
            }
            else if (Is(d, p, "data")) { dataPos = body; dataLen = len; break; }
            p = body + len + (len & 1);
        }
        if (format != 1 || channels < 1 || channels > 2 || (bits != 8 && bits != 16) || dataPos < 0 || rate <= 0)
            throw new InvalidDataException("WAV: only PCM, 8 or 16 bit, mono or stereo is supported");
        var bytesPer = bits / 8;
        var frames = dataLen / (channels * bytesPer);
        if (frames < 1) throw new InvalidDataException("WAV: no sample data");
        var data = new sbyte[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var o = dataPos + (i * channels + c) * bytesPer;
                if (bits == 8) sum += d[o] - 128;
                else
                {
                    var v = d[o] | (d[o + 1] << 8);
                    if (v >= 32768) v -= 65536;
                    sum += v >> 8;
                }
            }
            data[i] = (sbyte)(sum / channels);
        }
        return new SoundData(data, rate);
    }

    private static SoundData Read8svx(byte[] d)
    {
        int oneShot = 0, repeat = 0, rate = 0, octaves = 1, compression = 0, bodyPos = -1, bodyLen = 0;
        var p = 12;
        while (p + 8 <= d.Length)
        {
            var chunk = Be32(d, p + 4);
            var body = p + 8;
            var room = d.Length - body;
            var len = chunk > (uint)room ? room : (int)chunk;
            if (Is(d, p, "VHDR") && len >= 16)
            {
                oneShot = (int)Be32(d, body); repeat = (int)Be32(d, body + 4);
                rate = (d[body + 12] << 8) | d[body + 13];
                octaves = d[body + 14]; compression = d[body + 15];
            }
            else if (Is(d, p, "BODY")) { bodyPos = body; bodyLen = len; break; }
            p = body + len + (len & 1);
        }
        if (bodyPos < 0 || compression > 1 || bodyLen < 1) throw new InvalidDataException("8SVX: no BODY, or an unsupported compression");
        if (rate <= 0) rate = 8363;
        var first = oneShot + repeat;
        int count;
        if (compression == 0) count = bodyLen;
        else
        {
            if (bodyLen < 3) throw new InvalidDataException("8SVX: BODY is too short");
            count = (bodyLen - 2) * 2;
        }
        if (octaves > 1 && first > 0 && first < count) count = first;
        var data = new sbyte[count];
        if (compression == 0)
            for (var i = 0; i < count; i++) data[i] = (sbyte)d[bodyPos + i];
        else
        {
            int[] fib = { -34, -21, -13, -8, -5, -3, -2, -1, 0, 1, 2, 3, 5, 8, 13, 21 };
            int v = (sbyte)d[bodyPos + 1];
            for (var i = 0; i < count; i++)
            {
                var packed = d[bodyPos + 2 + i / 2];
                v += fib[(i & 1) == 0 ? (packed >> 4) & 15 : packed & 15];
                data[i] = (sbyte)v;
            }
        }
        return new SoundData(data, rate);
    }
}
