using ESharp.Assets;
using List = System.Collections.Generic.List<byte>;

namespace ESharp.Compiler.Tests;

/// <summary>Dithering, ILBM input and sound assets (issue #13).</summary>
public class AssetFormatTests : IDisposable
{
    private readonly System.Collections.Generic.List<string> _scratch = new();

    public void Dispose()
    {
        foreach (var d in _scratch)
            try { Directory.Delete(d, true); } catch (IOException) { }
    }

    private string Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-assetfmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _scratch.Add(dir);
        return dir;
    }

    private static RgbaImage Image(int w, int h, Func<int, int, (int r, int g, int b, int a)> f)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, a) = f(x, y);
                var o = (y * w + x) * 4;
                rgba[o] = (byte)r; rgba[o + 1] = (byte)g; rgba[o + 2] = (byte)b; rgba[o + 3] = (byte)a;
            }
        return new RgbaImage(w, h, rgba);
    }

    private static int Changes(PlanarImage p, int y)
    {
        var n = 0;
        for (var x = 1; x < p.Width; x++) if (p.Indices[y * p.Width + x] != p.Indices[y * p.Width + x - 1]) n++;
        return n;
    }

    // ---- dithering ----

    [Theory]
    [InlineData(Dither.FloydSteinberg)]
    [InlineData(Dither.Ordered)]
    public void Dithering_a_gradient_mixes_indices_inside_the_flat_bands(Dither dither)
    {
        var image = Image(128, 8, (x, _) => (x * 2, x * 2, x * 2, 255));
        var flat = Planar.Convert(image, 4);
        var dithered = Planar.Convert(image, 4, dither: dither);
        Assert.Equal(flat.Palette, dithered.Palette);                       // same palette, different mapping
        Assert.True(Changes(flat, 3) <= 3);                                  // one band per palette entry
        Assert.True(Changes(dithered, 3) > 3 * Changes(flat, 3) + 6);        // neighbouring indices alternate inside a band
        // Within a flat band the dithered row uses at least two indices.
        var band = Enumerable.Range(0, 128).Where(x => flat.Indices[x] == flat.Indices[64]).ToList();
        Assert.True(band.Select(x => (int)dithered.Indices[x]).Distinct().Count() > 1);
        // The average colour of the row is closer to the source than the flat one's.
        double Mean(PlanarImage p) => Enumerable.Range(0, 128).Average(x => ((p.Palette[p.Indices[x]] >> 8) & 15) * 17.0);
        var source = Enumerable.Range(0, 128).Average(x => x * 2.0);
        if (dither == Dither.FloydSteinberg) Assert.True(Math.Abs(Mean(dithered) - source) < Math.Abs(Mean(flat) - source));
    }

    [Fact]
    public void Dithering_works_on_aga_images_too()
    {
        var image = Image(64, 4, (x, _) => (x * 4, 255 - x * 4, 128, 255));
        var flat = Planar.Convert(image, 8 /* ignored limit check */ * 4, aga: true);
        var dithered = Planar.Convert(image, 4, aga: true, dither: Dither.FloydSteinberg);
        Assert.True(Changes(dithered, 1) > 3);
        Assert.Equal(4, dithered.Palette24.Length);
        Assert.NotNull(flat);
    }

    [Theory]
    [InlineData(Dither.FloydSteinberg)]
    [InlineData(Dither.Ordered)]
    public void Dithering_leaves_transparent_pixels_and_the_mask_alone(Dither dither)
    {
        var image = Image(40, 8, (x, y) => (x * 6, x * 6, x * 6, (x / 4 + y) % 5 == 0 ? 0 : 255));
        var flat = Planar.Convert(image, 4);
        var dithered = Planar.Convert(image, 4, dither: dither);
        Assert.Equal(flat.Mask, dithered.Mask);
        for (var i = 0; i < 40 * 8; i++)
            if (image.Rgba[i * 4 + 3] == 0) Assert.Equal(0, dithered.Indices[i]);
        // Transparent pixels give no error to their neighbours: an image that is only transparent apart from one pixel maps like flat.
        var lone = Image(5, 1, (x, _) => x == 2 ? (100, 100, 100, 255) : (255, 255, 255, 0));
        Assert.Equal(Planar.Convert(lone, 4).Indices, Planar.Convert(lone, 4, dither: dither).Indices);
    }

    [Theory]
    [InlineData(Dither.FloydSteinberg)]
    [InlineData(Dither.Ordered)]
    public void An_exact_mapping_is_not_dithered(Dither dither)
    {
        var image = Image(16, 2, (x, _) => x < 8 ? (255, 0, 0, 255) : (0, 0, 255, 255));
        Assert.Equal(Planar.Convert(image, 4).Indices, Planar.Convert(image, 4, dither: dither).Indices);
    }

    [Fact]
    public void Dither_manifest_values_parse_and_a_wrong_one_is_an_error()
    {
        Assert.Equal(Dither.None, Planar.ParseDither(null));
        Assert.Equal(Dither.FloydSteinberg, Planar.ParseDither("fs"));
        Assert.Equal(Dither.Ordered, Planar.ParseDither("ordered"));
        Assert.Throws<InvalidDataException>(() => Planar.ParseDither("jarvis"));
    }

    // ---- ILBM ----

    private static (byte[] indices, byte[] palette) Picture(int w, int h, int depth)
    {
        var idx = new byte[w * h];
        for (var i = 0; i < idx.Length; i++) idx[i] = (byte)((i * 7 + i / w) % (1 << depth));
        var pal = new byte[3 * (1 << depth)];
        for (var c = 0; c < 1 << depth; c++) { pal[c * 3] = (byte)(c * 30); pal[c * 3 + 1] = (byte)(255 - c * 20); pal[c * 3 + 2] = (byte)(c * 11 + 3); }
        return (idx, pal);
    }

    [Theory]
    [InlineData(false, 16, 3)]
    [InlineData(true, 16, 3)]
    [InlineData(false, 21, 4)]     // odd width: rows are padded to 16 pixels
    [InlineData(true, 21, 4)]
    [InlineData(true, 33, 8)]
    public void Ilbm_reads_pixels_and_palette(bool byteRun1, int w, int depth)
    {
        const int h = 5;
        var (idx, pal) = Picture(w, h, depth);
        var image = Ilbm.Read(Ilbm.Write(w, h, depth, idx, pal, byteRun1));
        Assert.Equal(w, image.Width);
        Assert.Equal(h, image.Height);
        for (var i = 0; i < w * h; i++)
        {
            Assert.Equal(pal[idx[i] * 3], image.Rgba[i * 4]);
            Assert.Equal(pal[idx[i] * 3 + 1], image.Rgba[i * 4 + 1]);
            Assert.Equal(pal[idx[i] * 3 + 2], image.Rgba[i * 4 + 2]);
            Assert.Equal(255, image.Rgba[i * 4 + 3]);
        }
    }

    [Fact]
    public void Ilbm_mask_plane_and_transparent_colour_become_alpha()
    {
        const int w = 19, h = 3;
        var (idx, pal) = Picture(w, h, 2);
        var mask = new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = i % 3 != 0;
        var masked = Ilbm.Read(Ilbm.Write(w, h, 2, idx, pal, true, maskPlane: mask));
        for (var i = 0; i < mask.Length; i++) Assert.Equal(mask[i] ? 255 : 0, masked.Rgba[i * 4 + 3]);

        var keyed = Ilbm.Read(Ilbm.Write(w, h, 2, idx, pal, false, transparentIndex: 2));
        for (var i = 0; i < idx.Length; i++) Assert.Equal(idx[i] == 2 ? 0 : 255, keyed.Rgba[i * 4 + 3]);
    }

    [Fact]
    public void Ilbm_camg_without_ham_or_ehb_is_accepted_and_ham_ehb_are_rejected_clearly()
    {
        var (idx, pal) = Picture(16, 2, 3);
        Assert.Equal(16, Ilbm.Read(Ilbm.Write(16, 2, 3, idx, pal, false, camg: 0x8000 | 0x4)).Width);   // hires + interlace flags
        var ham = Assert.Throws<InvalidDataException>(() => Ilbm.Read(Ilbm.Write(16, 2, 3, idx, pal, false, camg: 0x800)));
        Assert.Contains("HAM", ham.Message);
        var ehb = Assert.Throws<InvalidDataException>(() => Ilbm.Read(Ilbm.Write(16, 2, 3, idx, pal, false, camg: 0x80)));
        Assert.Contains("Extra-Half-Brite", ehb.Message);
    }

    [Fact]
    public void Ilbm_reader_rejects_huge_pictures_before_allocating()
    {
        var (idx, pal) = Picture(16, 4, 3);
        foreach (var (w, h) in new[] { (65535, 65535), (40000, 40000) })
        {
            var f = Ilbm.Write(16, 4, 3, idx, pal, false);
            f[12 + 8] = (byte)(w >> 8); f[12 + 9] = (byte)w; f[12 + 10] = (byte)(h >> 8); f[12 + 11] = (byte)h;
            var ex = Assert.Throws<InvalidDataException>(() => Ilbm.Read(f));
            Assert.Contains("too large", ex.Message);
        }
    }

    [Theory]
    [InlineData(Dither.FloydSteinberg)]
    [InlineData(Dither.Ordered)]
    public void Dithering_also_runs_when_the_12_bit_rounding_alone_fits_the_budget(Dither dither)
    {
        var image = Image(256, 4, (x, _) => (x, x, x, 255));      // 16 levels after rounding, which fits 32 colours
        var flat = Planar.Convert(image, 32);
        var dithered = Planar.Convert(image, 32, dither: dither);
        Assert.Equal(flat.Palette, dithered.Palette);
        var band = Enumerable.Range(0, 256).Where(x => flat.Indices[x] == flat.Indices[128]).ToList();
        Assert.True(band.Select(x => (int)dithered.Indices[x]).Distinct().Count() > 1);
        // Exactly representable colours still map exactly.
        var exact = Image(16, 1, (x, _) => (x * 17, x * 17, x * 17, 255));
        Assert.Equal(Planar.Convert(exact, 32).Indices, Planar.Convert(exact, 32, dither: dither).Indices);
    }

    [Fact]
    public void Ilbm_reader_rejects_bad_files()
    {
        Assert.Throws<InvalidDataException>(() => Ilbm.Read(new byte[] { 1, 2, 3 }));
        var (idx, pal) = Picture(16, 4, 3);
        var good = Ilbm.Write(16, 4, 3, idx, pal, false);
        Assert.Throws<InvalidDataException>(() => Ilbm.Read(good.Take(good.Length - 10).ToArray()));   // truncated BODY
        var lasso = (byte[])good.Clone();
        lasso[12 + 8 + 9] = 3;                                                                          // masking = lasso
        Assert.Throws<InvalidDataException>(() => Ilbm.Read(lasso));
    }

    [Fact]
    public void Ilbm_true_colour_24_planes_reads()
    {
        // One row of 2 pixels, written by hand: 24 planes, red/green/blue bytes spread over planes.
        var body = new byte[24 * 2];
        void Set(int x, int r, int g, int b)
        {
            for (var i = 0; i < 8; i++)
            {
                if (((r >> i) & 1) != 0) body[i * 2] |= (byte)(0x80 >> x);
                if (((g >> i) & 1) != 0) body[(8 + i) * 2] |= (byte)(0x80 >> x);
                if (((b >> i) & 1) != 0) body[(16 + i) * 2] |= (byte)(0x80 >> x);
            }
        }
        Set(0, 10, 200, 33); Set(1, 255, 0, 128);
        var file = new List();
        void Tag(string t) { foreach (var c in t) file.Add((byte)c); }
        void B32(int v) { file.Add((byte)(v >> 24)); file.Add((byte)(v >> 16)); file.Add((byte)(v >> 8)); file.Add((byte)v); }
        var bmhd = new byte[20]; bmhd[1] = 2; bmhd[3] = 1; bmhd[8] = 24;
        Tag("FORM"); B32(4 + 8 + 20 + 8 + body.Length); Tag("ILBM"); Tag("BMHD"); B32(20); file.AddRange(bmhd); Tag("BODY"); B32(body.Length); file.AddRange(body);
        var image = Ilbm.Read(file.ToArray());
        Assert.Equal(new byte[] { 10, 200, 33, 255, 255, 0, 128, 255 }, image.Rgba);
    }

    // ---- sounds ----

    private static byte[] Wav(int channels, int rate, int bits, byte[] data)
    {
        var w = new List();
        void Tag(string t) { foreach (var c in t) w.Add((byte)c); }
        void Le32(int v) { for (var i = 0; i < 4; i++) w.Add((byte)(v >> (8 * i))); }
        void Le16(int v) { w.Add((byte)v); w.Add((byte)(v >> 8)); }
        Tag("RIFF"); Le32(36 + data.Length); Tag("WAVE"); Tag("fmt "); Le32(16); Le16(1); Le16(channels); Le32(rate);
        Le32(rate * channels * bits / 8); Le16(channels * bits / 8); Le16(bits); Tag("data"); Le32(data.Length); w.AddRange(data);
        return w.ToArray();
    }

    [Fact]
    public void Sound_files_read_wav_8_and_16_bit_stereo_and_8svx()
    {
        var w8 = SoundFile.Read(Wav(1, 8000, 8, new byte[] { 128, 255, 0, 200 }));
        Assert.Equal(8000, w8.Rate);
        Assert.Equal(new sbyte[] { 0, 127, -128, 72 }, w8.Data);

        // 16-bit stereo: frames (0x4000, 0x2000) -> (64 + 32) / 2 = 48 and (-0x4000, -0x4000) -> -64.
        var w16 = SoundFile.Read(Wav(2, 11025, 16, new byte[] { 0x00, 0x40, 0x00, 0x20, 0x00, 0xC0, 0x00, 0xC0 }));
        Assert.Equal(11025, w16.Rate);
        Assert.Equal(new sbyte[] { 48, -64 }, w16.Data);

        var svx = new List();
        void Tag(string t) { foreach (var c in t) svx.Add((byte)c); }
        void B32(int v) { svx.Add((byte)(v >> 24)); svx.Add((byte)(v >> 16)); svx.Add((byte)(v >> 8)); svx.Add((byte)v); }
        Tag("FORM"); B32(4 + 28 + 8 + 4); Tag("8SVX"); Tag("VHDR"); B32(20); B32(4); B32(0); B32(0); svx.Add(0x2E); svx.Add(0xE0); svx.Add(1); svx.Add(0); B32(0x10000);
        Tag("BODY"); B32(4); svx.AddRange(new byte[] { 1, 2, 0xFE, 0x80 });
        var s = SoundFile.Read(svx.ToArray());
        Assert.Equal(12000, s.Rate);
        Assert.Equal(new sbyte[] { 1, 2, -2, -128 }, s.Data);

        Assert.Throws<InvalidDataException>(() => SoundFile.Read(new byte[] { 1, 2, 3, 4 }));
    }

    // ---- manifest ----

    private static string Manifest(string dir, string json)
    {
        var path = Path.Combine(dir, "assets.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Manifest_builds_ilbm_dithered_and_sample_entries()
    {
        var dir = Scratch();
        var (idx, pal) = Picture(21, 5, 4);
        File.WriteAllBytes(Path.Combine(dir, "pic.iff"), Ilbm.Write(21, 5, 4, idx, pal, true));
        File.WriteAllBytes(Path.Combine(dir, "ramp.png"), Png.Write(Image(64, 4, (x, _) => (x * 4, x * 4, x * 4, 255))));
        File.WriteAllBytes(Path.Combine(dir, "tick.wav"), Wav(1, 8363, 8, new byte[] { 128, 200, 128, 56, 128 }));
        var manifest = Manifest(dir, @"{ ""namespace"": ""T"", ""class"": ""A"",
            ""images"": [ { ""name"": ""Pic"", ""file"": ""pic.iff"", ""colors"": 16 }, { ""name"": ""Ramp"", ""file"": ""ramp.png"", ""colors"": 4, ""dither"": ""fs"" } ],
            ""samples"": [ { ""name"": ""Tick"", ""file"": ""tick.wav"" } ] }");
        var output = AssetBuilder.Build(manifest);
        Assert.Contains("public const int PicWidth = 21;", output.Source);
        Assert.Contains("public const int TickLength = 6;", output.Source);     // 5 samples, padded to even
        Assert.Contains("public const int TickRate = 8363;", output.Source);
        Assert.Contains("[ChipRam] public static byte[] TickData", output.Source);
        Assert.Contains("0x00, 0x48, 0x00, 0xB8, 0x00, 0x00", output.Source);
        Assert.Empty(output.Warnings!);

        // Dithering changes only the entry that asks for it.
        var flatRamp = AssetBuilder.Build(Manifest(dir, @"{ ""images"": [ { ""name"": ""Ramp"", ""file"": ""ramp.png"", ""colors"": 4 } ] }"));
        var fsRamp = AssetBuilder.Build(Manifest(dir, @"{ ""images"": [ { ""name"": ""Ramp"", ""file"": ""ramp.png"", ""colors"": 4, ""dither"": ""fs"" } ] }"));
        Assert.NotEqual(flatRamp.Source, fsRamp.Source);
        var plain = AssetBuilder.Build(Manifest(dir, @"{ ""images"": [ { ""name"": ""Ramp"", ""file"": ""ramp.png"", ""colors"": 4, ""dither"": ""none"" } ] }"));
        Assert.Equal(flatRamp.Source, plain.Source);
    }

    [Fact]
    public void A_long_sample_is_cut_to_the_audio_device_limit_with_a_warning()
    {
        var dir = Scratch();
        File.WriteAllBytes(Path.Combine(dir, "long.wav"), Wav(1, 8000, 8, new byte[140000]));
        var output = AssetBuilder.Build(Manifest(dir, @"{ ""samples"": [ { ""name"": ""Long"", ""file"": ""long.wav"" } ] }"));
        Assert.Contains("public const int LongLength = 131070;", output.Source);
        Assert.Single(output.Warnings!);
        Assert.Contains("131,070", output.Warnings![0].Replace(' ', ',').Replace(".", ","));
        Assert.Equal(AssetBuilder.MaxSampleLength, ESharp.Platform.Sound.MaxLength);
    }

    [Fact]
    public void Sync_treats_ilbm_and_sound_files_like_pngs()
    {
        var dir = Scratch();
        var (idx, pal) = Picture(16, 2, 2);
        var iff = Path.Combine(dir, "pic.lbm");
        var wav = Path.Combine(dir, "tick.wav");
        File.WriteAllBytes(iff, Ilbm.Write(16, 2, 2, idx, pal, false));
        File.WriteAllBytes(wav, Wav(1, 8000, 8, new byte[] { 128, 129, 130, 131 }));
        var manifest = Manifest(dir, @"{ ""images"": [ { ""name"": ""Pic"", ""file"": ""pic.lbm"", ""colors"": 4 } ], ""samples"": [ { ""name"": ""Tick"", ""file"": ""tick.wav"" } ] }");
        Assert.Contains(Path.GetFullPath(iff), AssetBuilder.Inputs(manifest));
        Assert.Contains(Path.GetFullPath(wav), AssetBuilder.Inputs(manifest));
        Assert.Equal(AssetBuilder.SyncStatus.Created, AssetBuilder.Sync(manifest).Status);
        var old = DateTime.UtcNow.AddHours(-1);
        foreach (var f in Directory.GetFiles(dir)) File.SetLastWriteTimeUtc(f, old);
        File.SetLastWriteTimeUtc(Path.Combine(dir, "Assets.g.cs"), DateTime.UtcNow.AddMinutes(-30));
        Assert.Equal(AssetBuilder.SyncStatus.UpToDate, AssetBuilder.Sync(manifest).Status);

        File.SetLastWriteTimeUtc(iff, DateTime.UtcNow);
        Assert.True(AssetBuilder.IsStale(manifest));
        Assert.Equal(AssetBuilder.SyncStatus.Touched, AssetBuilder.Sync(manifest).Status);
        Assert.Equal(AssetBuilder.SyncStatus.UpToDate, AssetBuilder.Sync(manifest).Status);

        File.SetLastWriteTimeUtc(wav, DateTime.UtcNow.AddSeconds(5));
        Assert.True(AssetBuilder.IsStale(manifest));
        File.WriteAllBytes(wav, Wav(1, 8000, 8, new byte[] { 128, 129, 130, 140 }));
        File.SetLastWriteTimeUtc(wav, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(AssetBuilder.SyncStatus.Updated, AssetBuilder.Sync(manifest).Status);
    }

    [Fact]
    public void Build_step_reports_a_ham_picture_as_a_message()
    {
        var dir = Scratch();
        var (idx, pal) = Picture(16, 2, 3);
        File.WriteAllBytes(Path.Combine(dir, "ham.iff"), Ilbm.Write(16, 2, 3, idx, pal, false, camg: 0x800));
        Manifest(dir, @"{ ""images"": [ { ""name"": ""Ham"", ""file"": ""ham.iff"", ""colors"": 8 } ] }");
        var error = ESharp.Cli.Program.SyncAssets(dir);
        Assert.NotNull(error);
        Assert.Contains("HAM", error);
    }

    // ---- the sample ----

    [Fact]
    public void Committed_assets_sample_matches_what_the_pipeline_generates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "assets");
        var generated = AssetBuilder.Build(Path.Combine(dir, "assets.json"));
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "Assets.g.cs")), generated.Source);
        Assert.Empty(generated.Warnings!);
    }
}
