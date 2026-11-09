using ESharp.Assets;

namespace ESharp.Compiler.Tests;

public class AssetTests
{
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

    [Fact]
    public void Png_round_trips()
    {
        var image = Image(7, 5, (x, y) => (x * 30, y * 40, (x + y) * 10, x == 3 ? 128 : 255));
        var back = Png.Read(Png.Write(image));
        Assert.Equal(7, back.Width);
        Assert.Equal(5, back.Height);
        Assert.Equal(image.Rgba, back.Rgba);
    }

    [Fact]
    public void Png_reader_rejects_other_files()
    {
        Assert.Throws<InvalidDataException>(() => Png.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
    }

    [Fact]
    public void Few_colours_map_exactly_and_pack_into_planes()
    {
        // 16 pixels in a row: red for x < 4, green for x >= 12, black between.
        var image = Image(16, 1, (x, _) => x < 4 ? (255, 0, 0, 255) : x >= 12 ? (0, 255, 0, 255) : (0, 0, 0, 255));
        var planar = Planar.Convert(image, 16);
        Assert.Equal(2, planar.Depth);
        Assert.Equal(3, planar.Palette.Count(p => p != 0) + 1);          // black, red, green (+ one unused slot)
        var red = Array.IndexOf(planar.Palette, (ushort)0xF00);
        var green = Array.IndexOf(planar.Palette, (ushort)0x0F0);
        Assert.True(red > 0 && green > 0);
        Assert.Equal(red, planar.Indices[0]);
        Assert.Equal(green, planar.Indices[15]);
        // Two planes of one 16-pixel row = two bytes each; leftmost pixel is the most significant bit.
        Assert.Equal(4, planar.Planes.Length);
        var redBit0 = (red & 1) != 0;
        Assert.Equal(redBit0 ? 0xF0 : 0x00, planar.Planes[0] & 0xF0);
    }

    [Fact]
    public void Rows_are_padded_to_whole_words()
    {
        var image = Image(20, 3, (x, y) => (x == 19 ? (255, 255, 255, 255) : (0, 0, 0, 255)));
        var planar = Planar.Convert(image, 2);
        Assert.Equal(1, planar.Depth);
        Assert.Equal(4, planar.BytesPerRow);                       // 20 pixels -> 2 words
        Assert.Equal(4 * 3, planar.Planes.Length);
        Assert.Equal(0x10, planar.Planes[2]);                      // pixel 19 = bit 3 of byte 2
        Assert.Equal(0x10, planar.Planes[4 + 2]);                  // same on the next row
    }

    [Fact]
    public void Too_many_colours_are_reduced_to_the_requested_count()
    {
        var image = Image(32, 32, (x, y) => (x * 8, y * 8, (x + y) * 4, 255));
        var planar = Planar.Convert(image, 16);
        Assert.True(planar.Depth <= 4);
        var used = planar.Indices.Distinct().Count();
        Assert.InRange(used, 2, 16);
        Assert.All(planar.Indices, i => Assert.True(i < planar.Palette.Length));
        // The reduced picture stays close to the original: average error per channel under 5 of 15.
        double error = 0;
        for (var i = 0; i < planar.Indices.Length; i++)
        {
            var p = planar.Palette[planar.Indices[i]];
            error += Math.Abs(((p >> 8) & 15) - (image.Rgba[i * 4] * 15 + 127) / 255)
                   + Math.Abs(((p >> 4) & 15) - (image.Rgba[i * 4 + 1] * 15 + 127) / 255)
                   + Math.Abs((p & 15) - (image.Rgba[i * 4 + 2] * 15 + 127) / 255);
        }
        Assert.True(error / (planar.Indices.Length * 3) < 5, $"average error {error / (planar.Indices.Length * 3):0.00}");
    }

    [Fact]
    public void Transparent_pixels_become_palette_entry_zero_which_is_black()
    {
        var image = Image(4, 1, (x, _) => x < 2 ? (255, 0, 0, 0) : (0, 0, 255, 255));
        var planar = Planar.Convert(image, 4);
        Assert.Equal(0, planar.Palette[0]);
        Assert.Equal(0, planar.Indices[0]);
        Assert.Equal(0, planar.Indices[1]);
        Assert.NotEqual(0, planar.Indices[2]);
        Assert.Equal(0x00F, planar.Palette[planar.Indices[2]]);
    }

    [Fact]
    public void Mask_plane_marks_opaque_pixels_and_keeps_black_art_distinct_from_transparency()
    {
        // Pixels: transparent, opaque black, opaque blue, ... on a 20 pixel wide row (rows are padded to 32 bits).
        var image = Image(20, 1, (x, _) => x % 3 == 0 ? (0, 0, 0, 0) : x % 3 == 1 ? (0, 0, 0, 255) : (0, 0, 255, 255));
        var planar = Planar.Convert(image, 4);
        Assert.Equal(4, planar.Mask.Length);
        for (var x = 0; x < 20; x++)
        {
            var bit = (planar.Mask[x >> 3] & (0x80 >> (x & 7))) != 0;
            Assert.Equal(x % 3 != 0, bit);
        }
        Assert.Equal(0, planar.Mask[2] & 0x0F);      // padding bits beyond the width stay clear
        Assert.Equal(0, planar.Mask[3]);
        Assert.Equal(planar.Indices[0], planar.Indices[1]); // black and transparent share index 0; only the mask tells them apart
    }

    [Fact]
    public void Colour_count_outside_2_to_32_is_rejected()
    {
        var image = Image(2, 2, (_, _) => (0, 0, 0, 255));
        Assert.Throws<ArgumentOutOfRangeException>(() => Planar.Convert(image, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Planar.Convert(image, 33));
    }

    [Fact]
    public void Committed_Assets_g_cs_matches_what_the_pipeline_generates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "screen");
        var generated = AssetBuilder.Build(Path.Combine(dir, "assets.json"));
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "Assets.g.cs")), generated.Source);
        Assert.Equal(2112, generated.Reports.Single().ChipBytes);
    }

    [Fact]
    public void Committed_cards_Assets_g_cs_matches_what_the_pipeline_generates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "cards");
        var generated = AssetBuilder.Build(Path.Combine(dir, "assets.json"));
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "Assets.g.cs")), generated.Source);
        Assert.Contains("CardsMask", generated.Source);
        Assert.Equal(44800, generated.Reports.Single().ChipBytes); // 3 planes + mask
    }

    [Fact]
    public void Committed_rummy_Assets_g_cs_matches_what_the_pipeline_generates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "rummy");
        var generated = AssetBuilder.Build(Path.Combine(dir, "assets.json"));
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "Assets.g.cs")), generated.Source);
    }

    [Fact]
    public void Committed_aga_Assets_g_cs_matches_what_the_pipeline_generates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "aga");
        var generated = AssetBuilder.Build(Path.Combine(dir, "assets.json"));
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "Assets.g.cs")), generated.Source);
        Assert.Contains("uint[] TilePalette", generated.Source);
    }

    [Fact]
    public void Aga_conversion_keeps_eight_bits_per_channel_and_up_to_256_colours()
    {
        // 200 distinct colours that differ only in the low 4 bits of a channel: OCS would merge them, AGA keeps them.
        var image = Image(200, 1, (x, _) => (x, 255 - x, 17 * (x % 3), 255));
        var planar = Planar.Convert(image, 256, aga: true);
        Assert.Equal(8, planar.Depth);
        Assert.Equal(256, planar.Palette24.Length);
        Assert.Empty(planar.Palette);
        Assert.Equal(200, planar.Indices.Distinct().Count());
        for (var x = 0; x < 200; x++)
            Assert.Equal((uint)((x << 16) | ((255 - x) << 8) | (17 * (x % 3))), planar.Palette24[planar.Indices[x]]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Planar.Convert(image, 257, aga: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => Planar.Convert(image, 64));
    }

    [Fact]
    public void Aga_conversion_reduces_more_than_256_colours_with_median_cut()
    {
        var image = Image(32, 32, (x, y) => (x * 8, y * 8, (x + y) * 4, 255));
        var planar = Planar.Convert(image, 256, aga: true);
        Assert.Equal(8, planar.Depth);
        Assert.True(planar.Indices.Distinct().Count() <= 256);
        // Close to the source: every pixel lands within a few levels per channel.
        for (var i = 0; i < planar.Indices.Length; i++)
        {
            var c = planar.Palette24[planar.Indices[i]];
            var x = i % 32; var y = i / 32;
            Assert.InRange((int)((c >> 16) & 255) - x * 8, -24, 24);
            Assert.InRange((int)((c >> 8) & 255) - y * 8, -24, 24);
        }
    }

    // Incremental build step: copy samples/screen to a scratch directory and play with timestamps.
    private static string ScratchSample()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "screen");
        var dir = Path.Combine(Path.GetTempPath(), "esharp-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { "assets.json", "card.png", "Assets.g.cs" })
            File.Copy(Path.Combine(src, f), Path.Combine(dir, f));
        var old = DateTime.UtcNow.AddHours(-1);
        foreach (var f in Directory.GetFiles(dir)) File.SetLastWriteTimeUtc(f, old);
        return dir;
    }

    [Fact]
    public void Sync_leaves_a_fresh_Assets_g_cs_untouched()
    {
        var dir = ScratchSample();
        var target = Path.Combine(dir, "Assets.g.cs");
        var stamp = File.GetLastWriteTimeUtc(target);
        var before = File.ReadAllBytes(target);
        var r = AssetBuilder.Sync(Path.Combine(dir, "assets.json"));
        Assert.Equal(AssetBuilder.SyncStatus.UpToDate, r.Status);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(target));
        Assert.Equal(before, File.ReadAllBytes(target));
    }

    [Fact]
    public void Sync_regenerates_a_missing_file_byte_identical_to_the_committed_one()
    {
        var dir = ScratchSample();
        var target = Path.Combine(dir, "Assets.g.cs");
        var before = File.ReadAllBytes(target);
        File.Delete(target);
        Assert.Equal(AssetBuilder.SyncStatus.Created, AssetBuilder.Sync(Path.Combine(dir, "assets.json")).Status);
        Assert.Equal(before, File.ReadAllBytes(target));
    }

    [Fact]
    public void Sync_regenerates_when_a_png_is_touched_and_rewrites_only_if_the_text_changed()
    {
        var dir = ScratchSample();
        var manifest = Path.Combine(dir, "assets.json");
        var target = Path.Combine(dir, "Assets.g.cs");
        var before = File.ReadAllBytes(target);

        // Newer PNG with the same pixels: stale, regenerated, but the text is the same -> only touched, and fresh afterwards.
        File.SetLastWriteTimeUtc(Path.Combine(dir, "card.png"), DateTime.UtcNow);
        Assert.True(AssetBuilder.IsStale(manifest));
        Assert.Equal(AssetBuilder.SyncStatus.Touched, AssetBuilder.Sync(manifest).Status);
        Assert.Equal(before, File.ReadAllBytes(target));
        Assert.False(AssetBuilder.IsStale(manifest));
        Assert.Equal(AssetBuilder.SyncStatus.UpToDate, AssetBuilder.Sync(manifest).Status);

        // A PNG with different pixels: the file is rewritten.
        var png = Png.Read(File.ReadAllBytes(Path.Combine(dir, "card.png")));
        var rgba = (byte[])png.Rgba.Clone();
        rgba[0] ^= 0xFF; rgba[1] ^= 0xFF; rgba[2] ^= 0xFF; rgba[3] = 255;
        File.WriteAllBytes(Path.Combine(dir, "card.png"), Png.Write(new RgbaImage(png.Width, png.Height, rgba)));
        var r = AssetBuilder.Sync(manifest);
        Assert.Equal(AssetBuilder.SyncStatus.Updated, r.Status);
        Assert.NotEqual(before, File.ReadAllBytes(target));
    }

    [Fact]
    public void Sync_regenerates_when_the_manifest_is_newer()
    {
        var dir = ScratchSample();
        var manifest = Path.Combine(dir, "assets.json");
        File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow);
        Assert.True(AssetBuilder.IsStale(manifest));
    }
}
