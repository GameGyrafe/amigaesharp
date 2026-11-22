using ESharp.Assets;

// Makes the input files of samples/assets (original, generated here; MIT like the rest of the repository):
//   gradient.png  96x32 horizontal colour ramp with 96 distinct colours (reduced to 8 by `esharp assets`, with and without dithering)
//   badge.iff     37x24 ILBM, 3 bitplanes, ByteRun1, with a mask plane (odd width on purpose)
//   beep.wav      8-bit mono PCM at 8000 Hz, 1600 samples of a 440 Hz sine
// Usage: dotnet run --project tools/AssetSamples -- samples/assets
//
// With --tween, the assets of the sample library samples/packages/Tween instead (an E# library package with assets):
//   ball.png      16x16 shaded ball in three colours, transparent outside the circle (a masked sprite)
//   blip.wav      8-bit mono PCM at 8000 Hz, 480 samples of a falling square-ish tone with a decay
// Usage: dotnet run --project tools/AssetSamples -- --tween samples/packages/Tween
if (args.Length > 0 && args[0] == "--tween")
{
    var tweenDir = args.Length > 1 ? args[1] : "samples/packages/Tween";
    Directory.CreateDirectory(tweenDir);
    WriteBall(Path.Combine(tweenDir, "ball.png"));
    WriteBlip(Path.Combine(tweenDir, "blip.wav"));
    Console.WriteLine($"Wrote ball.png and blip.wav to {tweenDir}");
    return;
}
var dir = args.Length > 0 ? args[0] : "samples/assets";
Directory.CreateDirectory(dir);

// Gradient: dark blue through purple and orange to pale yellow.
const int GW = 96, GH = 32;
var rgba = new byte[GW * GH * 4];
for (var y = 0; y < GH; y++)
    for (var x = 0; x < GW; x++)
    {
        var t = x / (double)(GW - 1);
        var o = (y * GW + x) * 4;
        rgba[o] = (byte)Math.Round(20 + 235 * t);
        rgba[o + 1] = (byte)Math.Round(10 + 200 * t * t);
        rgba[o + 2] = (byte)Math.Round(120 * (1 - t) + 80 * t * t);
        rgba[o + 3] = 255;
    }
File.WriteAllBytes(Path.Combine(dir, "gradient.png"), Png.Write(new RgbaImage(GW, GH, rgba)));

// Badge: diagonal bands of seven colours with the corners cut off (those pixels are masked out).
const int BW = 37, BH = 24;
var indices = new byte[BW * BH];
var mask = new bool[BW * BH];
for (var y = 0; y < BH; y++)
    for (var x = 0; x < BW; x++)
    {
        var edgeX = Math.Min(x, BW - 1 - x);
        var edgeY = Math.Min(y, BH - 1 - y);
        mask[y * BW + x] = edgeX + edgeY >= 5;
        indices[y * BW + x] = mask[y * BW + x] ? (byte)(1 + ((x / 4 + y / 4) % 7)) : (byte)0;
    }
byte[] palette =
{
    0, 0, 0,  255, 0, 0,  0, 255, 0,  0, 0, 255,  255, 255, 0,  255, 0, 255,  0, 255, 255,  255, 255, 255,
};
File.WriteAllBytes(Path.Combine(dir, "badge.iff"), Ilbm.Write(BW, BH, 3, indices, palette, byteRun1: true, maskPlane: mask));

// Beep: 440 Hz sine, unsigned 8-bit PCM.
const int Rate = 8000, N = 1600;
var wav = new List<byte>();
void Tag(string t) { foreach (var c in t) wav.Add((byte)c); }
void Le32(int v) { for (var i = 0; i < 4; i++) wav.Add((byte)(v >> (8 * i))); }
void Le16(int v) { wav.Add((byte)v); wav.Add((byte)(v >> 8)); }
Tag("RIFF"); Le32(36 + N); Tag("WAVE"); Tag("fmt "); Le32(16); Le16(1); Le16(1); Le32(Rate); Le32(Rate); Le16(1); Le16(8); Tag("data"); Le32(N);
for (var i = 0; i < N; i++) wav.Add((byte)(128 + (int)Math.Round(100 * Math.Sin(2 * Math.PI * 440 * i / Rate))));
File.WriteAllBytes(Path.Combine(dir, "beep.wav"), wav.ToArray());
Console.WriteLine($"Wrote gradient.png, badge.iff and beep.wav to {dir}");

// The ball: a circle of radius 7.5 lit from the upper left, in three shades plus a highlight; outside it is transparent (the mask).
static void WriteBall(string path)
{
    const int S = 16;
    byte[][] shades = { new byte[] { 0, 0, 0 }, new byte[] { 0, 68, 170 }, new byte[] { 34, 136, 238 }, new byte[] { 238, 255, 255 } };
    var px = new byte[S * S * 4];
    for (var y = 0; y < S; y++)
        for (var x = 0; x < S; x++)
        {
            double dx = x + 0.5 - 8, dy = y + 0.5 - 8;
            var shade = 0;
            if (dx * dx + dy * dy <= 7.5 * 7.5)
            {
                double hx = x + 0.5 - 5.5, hy = y + 0.5 - 5.5;   // distance from the light spot
                var d = Math.Sqrt(hx * hx + hy * hy);
                shade = d < 2 ? 3 : d < 6.5 ? 2 : 1;
            }
            var o = (y * S + x) * 4;
            px[o] = shades[shade][0];
            px[o + 1] = shades[shade][1];
            px[o + 2] = shades[shade][2];
            px[o + 3] = (byte)(shade == 0 ? 0 : 255);   // transparent outside the circle: the mask
        }
    File.WriteAllBytes(path, Png.Write(new RgbaImage(S, S, px)));
}

// The blip: a square wave falling from 800 Hz to 400 Hz while it fades out, unsigned 8-bit PCM.
static void WriteBlip(string path)
{
    const int Rate = 8000, N = 480;
    var wav = new List<byte>();
    void Tag(string t) { foreach (var c in t) wav.Add((byte)c); }
    void Le32(int v) { for (var i = 0; i < 4; i++) wav.Add((byte)(v >> (8 * i))); }
    void Le16(int v) { wav.Add((byte)v); wav.Add((byte)(v >> 8)); }
    Tag("RIFF"); Le32(36 + N); Tag("WAVE"); Tag("fmt "); Le32(16); Le16(1); Le16(1); Le32(Rate); Le32(Rate); Le16(1); Le16(8); Tag("data"); Le32(N);
    var phase = 0.0;
    for (var i = 0; i < N; i++)
    {
        var t = i / (double)N;
        phase += (800 - 400 * t) / Rate;
        var level = (int)Math.Round(90 * (1 - t));
        wav.Add((byte)(128 + (phase % 1.0 < 0.5 ? level : -level)));
    }
    File.WriteAllBytes(path, wav.ToArray());
}
