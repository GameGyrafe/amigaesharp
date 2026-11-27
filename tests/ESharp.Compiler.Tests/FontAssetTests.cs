using System.Collections.Generic;
using System.Diagnostics;
using ESharp.Assets;
using ESharp.Cli;
using ESharp.Compiler;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Bitmap fonts as assets (issue #155): the generator (font image in, glyph bits out; errors for bad sizes), the sample compiled for
/// every target, and, with the esharp-sdl image, the sample built and run on sdl with its screenshot checked against the glyph data.
/// </summary>
public class FontAssetTests : IDisposable
{
    private readonly List<string> _scratch = new();

    public void Dispose()
    {
        foreach (var d in _scratch)
            try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Scratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-fontasset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _scratch.Add(dir);
        return dir;
    }

    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");

    /// <summary>A white image with black ink where <paramref name="ink"/> says so (alpha 255 everywhere).</summary>
    private static RgbaImage Sheet(int w, int h, Func<int, int, bool> ink, bool transparentBackground = false)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                var on = ink(x, y);
                rgba[o] = rgba[o + 1] = rgba[o + 2] = (byte)(on ? 0 : 255);
                rgba[o + 3] = (byte)(on || !transparentBackground ? 255 : 0);
            }
        return new RgbaImage(w, h, rgba);
    }

    [Fact]
    public void Glyph_bits_come_from_the_cells_in_reading_order_most_significant_bit_first()
    {
        // 3 columns x 2 rows of 4x2 cells, 5 glyphs from 'A': glyph g has its top-left pixel set and, for g == 4, the whole bottom row.
        var image = Sheet(12, 4, (x, y) =>
        {
            var g = y / 2 * 3 + x / 4;
            var cx = x % 4;
            var cy = y % 2;
            return g < 5 && ((cx == 1 && cy == 0) || (g == 4 && cy == 1));
        });
        var glyphs = FontImage.Convert(image, 4, 2, 'A', 5);
        Assert.Equal(3, glyphs.Columns);
        Assert.Equal(2, glyphs.Rows);
        // One byte per row; 4-pixel cells use the high nibble.
        Assert.Equal(new byte[] { 0x40, 0x00, 0x40, 0x00, 0x40, 0x00, 0x40, 0x00, 0x40, 0xF0 }, glyphs.Data);
        Assert.Equal(1 + 1 + 1 + 1 + 1 + 4, glyphs.InkPixels);
    }

    [Fact]
    public void Wide_cells_take_two_bytes_a_row_and_a_strip_with_fewer_columns_is_read_as_declared()
    {
        // 11-pixel cells: 2 bytes a row. Six glyphs in a strip of 3 columns (33 wide) and 2 rows.
        var image = Sheet(33, 6, (x, y) => (x % 11 == 10 && y == 0) || (x % 11 == 8 && y == 2));
        var glyphs = FontImage.Convert(image, 11, 3, 0, 6, columns: 3);
        Assert.Equal(6 * 3 * 2, glyphs.Data.Length);
        // Glyph 0, row 0: pixel 10 is bit 2 of the second byte.
        Assert.Equal(0x00, glyphs.Data[0]);
        Assert.Equal(0x20, glyphs.Data[1]);
        // Row 2: pixel 8 is the top bit of the second byte.
        Assert.Equal(0x80, glyphs.Data[5]);
    }

    [Fact]
    public void The_background_is_the_top_left_pixel_or_transparency()
    {
        // Orange on blue: the top-left blue pixel is the background, orange is ink.
        var blue = new RgbaImage(8, 1, new byte[] { 0, 0, 255, 255, 255, 128, 0, 255, 255, 128, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 255, 128, 0, 255, 0, 0, 255, 255 });
        Assert.Equal(new byte[] { 0x62 }, FontImage.Convert(blue, 8, 1, 65, 1).Data);
        // Transparent background: opaque pixels are ink whatever their colour, translucent ones (alpha < 128) are not.
        var rgba = new byte[8 * 4];
        for (var x = 0; x < 8; x++) { rgba[x * 4] = rgba[x * 4 + 1] = rgba[x * 4 + 2] = 255; rgba[x * 4 + 3] = (byte)(x == 1 ? 255 : x == 2 ? 127 : x == 7 ? 128 : 0); }
        Assert.Equal(new byte[] { 0x41 }, FontImage.Convert(new RgbaImage(8, 1, rgba), 8, 1, 65, 1).Data);
    }

    [Theory]
    [InlineData(10, 4, 8, 4, 32, 1, 0, "not a multiple of the cell width")]
    [InlineData(16, 3, 8, 4, 32, 4, 0, "need 8")]                      // 4 glyphs in 2 columns of cells 4 high: 8 rows, image is 3
    [InlineData(16, 4, 8, 4, 32, 0, 0, "count 0")]
    [InlineData(16, 4, 8, 4, 250, 10, 0, "does not fit the character codes")]
    [InlineData(16, 4, 0, 4, 32, 1, 0, "cell 0x4")]
    [InlineData(16, 4, 65, 4, 32, 1, 0, "cell 65x4")]
    [InlineData(16, 4, 8, 4, 256, 1, 0, "first character 256")]
    [InlineData(16, 4, 8, 4, 32, 1, 3, "columns 3 do not fit")]
    public void Bad_sizes_are_errors_that_say_what_is_wrong(int imageWidth, int imageHeight, int cellWidth, int cellHeight, int first, int count, int columns, string message)
    {
        var image = Sheet(imageWidth, imageHeight, (x, y) => x == 0);
        var e = Assert.Throws<InvalidDataException>(() => FontImage.Convert(image, cellWidth, cellHeight, first, count, columns));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void The_manifest_writes_a_nested_class_with_the_constants_data_and_drawing()
    {
        var dir = Scratch();
        File.WriteAllBytes(Path.Combine(dir, "f.png"), Png.Write(Sheet(16, 4, (x, y) => x % 8 == 1 && y % 2 == 0)));
        var manifest = Path.Combine(dir, "assets.json");
        File.WriteAllText(manifest, @"{ ""namespace"": ""T"", ""class"": ""A"", ""fonts"": [ { ""name"": ""Tiny"", ""file"": ""f.png"", ""cellWidth"": 8, ""cellHeight"": 2, ""first"": 48, ""count"": 4, ""columns"": 2 } ] }");
        var output = AssetBuilder.Build(manifest);
        Assert.Contains("using ESharp.Platform;", output.Source);
        Assert.Contains("public static class Tiny", output.Source);
        Assert.Contains("public const int CellWidth = 8;", output.Source);
        Assert.Contains("public const int CellHeight = 2;", output.Source);
        Assert.Contains("public const int First = 48;", output.Source);
        Assert.Contains("public const int Count = 4;", output.Source);
        Assert.Contains("public static byte[] Glyphs =", output.Source);
        Assert.Contains("0x40, 0x00, 0x40, 0x00, 0x40, 0x00, 0x40, 0x00", output.Source);
        Assert.Contains("BitmapFont.Draw(Glyphs, CellWidth, CellHeight, First, Count, text, x, y, color, scale);", output.Source);
        Assert.Contains("// input sha256:", output.Source);
        var report = Assert.Single(output.Reports);
        Assert.True(report.IsFont);
        Assert.Empty(output.Warnings!);
        // A font changes the sync inputs like an image does.
        Assert.Contains(Path.GetFullPath(Path.Combine(dir, "f.png")), AssetBuilder.Inputs(manifest));
        // No fonts: no using of the platform.
        File.WriteAllText(manifest, @"{ ""images"": [] }");
        Assert.DoesNotContain("ESharp.Platform", AssetBuilder.Build(manifest).Source);
    }

    [Fact]
    public void A_font_without_cell_size_or_count_and_an_image_with_no_ink_are_reported()
    {
        var dir = Scratch();
        File.WriteAllBytes(Path.Combine(dir, "f.png"), Png.Write(Sheet(8, 8, (x, y) => false)));
        var manifest = Path.Combine(dir, "assets.json");
        File.WriteAllText(manifest, @"{ ""fonts"": [ { ""name"": ""F"", ""file"": ""f.png"", ""count"": 1 } ] }");
        Assert.Contains("cellWidth", Assert.Throws<InvalidDataException>(() => AssetBuilder.Build(manifest)).Message);
        File.WriteAllText(manifest, @"{ ""fonts"": [ { ""name"": ""F"", ""file"": ""f.png"", ""cellWidth"": 8, ""cellHeight"": 8 } ] }");
        Assert.Contains("count", Assert.Throws<InvalidDataException>(() => AssetBuilder.Build(manifest)).Message);
        File.WriteAllText(manifest, @"{ ""fonts"": [ { ""name"": ""1F"", ""file"": ""f.png"", ""cellWidth"": 8, ""cellHeight"": 8, ""count"": 1 } ] }");
        Assert.Contains("identifier", Assert.Throws<InvalidDataException>(() => AssetBuilder.Build(manifest)).Message);
        File.WriteAllText(manifest, @"{ ""fonts"": [ { ""name"": ""F"", ""file"": ""f.png"", ""cellWidth"": 8, ""cellHeight"": 8, ""count"": 1 } ] }");
        Assert.Contains("no ink", Assert.Single(AssetBuilder.Build(manifest).Warnings!));
    }

    [Fact]
    public void The_sample_assets_are_what_the_generator_makes_of_slant_png()
    {
        var dir = Path.Combine(Samples, "fontasset");
        var made = AssetBuilder.Build(Path.Combine(dir, "assets.json")).Source.Replace("\r\n", "\n");
        Assert.Equal(made, File.ReadAllText(Path.Combine(dir, "Assets.g.cs")).Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("m68k")]
    [InlineData("os4")]
    [InlineData("morphos")]
    [InlineData("aros")]
    [InlineData("aros-i386")]
    [InlineData("sdl")]
    public void The_font_sample_compiles_for_every_target(string target)
    {
        var dir = Path.Combine(Samples, "fontasset");
        var sources = new[] { "FontAsset.cs", "Assets.g.cs" }.Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(dir, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        if (target == "sdl") Assert.Contains("es_sdl_fill(", r.C!);
        else
        {
            // Amiga: one BltTemplate per piece of a line, the strip in chip RAM.
            Assert.Contains("BltTemplate(", r.C!);
            if (target == "m68k") Assert.Contains("__chip", r.C!);
        }
    }

    private static bool HasSdlImage()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "image", "inspect", "esharp-sdl" }, RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>The sample built and run on sdl in the esharp-sdl image (headless), then its screenshot compared with the glyph data pixel for pixel.</summary>
    [Fact]
    public void The_font_sample_runs_on_sdl_in_the_image_and_its_screenshot_matches_the_glyph_data()
    {
        if (!HasSdlImage()) return;
        var samples = Path.Combine(Samples, "fontasset");
        var sources = new[] { "FontAsset.cs", "Assets.g.cs" }.Select(f => new SourceFile(f, File.ReadAllText(Path.Combine(samples, f)))).ToArray();
        var r = EsCompiler.Compile(sources, Targets.Sdl.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var dir = Scratch();
        File.WriteAllText(Path.Combine(dir, "prog.c"), r.C!);
        var build = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "run", "--rm", "-v", $"{dir}:/work", "-w", "/work", "esharp-sdl", "sh", "-c",
                     "gcc -O2 -fwrapv -Wall -Werror -o prog prog.c $(pkg-config --static --cflags --libs sdl3)" })
            build.ArgumentList.Add(a);
        using (var b = Process.Start(build)!)
        {
            var err = b.StandardError.ReadToEndAsync();
            b.StandardOutput.ReadToEnd();
            b.WaitForExit();
            Assert.True(b.ExitCode == 0, err.Result);
        }
        var png = Path.Combine(dir, "shot.png");
        var (exit, output) = SdlRun.Run(Path.Combine(dir, "prog"), true, TimeSpan.FromSeconds(60), "esharp-sdl", png, "", echo: false);
        Assert.Equal(0, exit);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(21, output.Split('\n').Count(l => l.StartsWith("ok ")));

        // The two lines of every character at (8, 8) and (8, 17): ink (15,15,8) = 255,255,136 on back (0,0,5) = 0,0,85.
        var img = Png.Read(File.ReadAllBytes(png));
        Assert.Equal(640, img.Width);
        Assert.Equal(256, img.Height);
        const string all = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        var wrong = 0;
        for (var k = 0; k < all.Length; k++)
            for (var y = 0; y < 9; y++)
                for (var x = 0; x < 9; x++)
                {
                    var px = 8 + k % 48 * 9 + x;
                    var py = 8 + k / 48 * 9 + y;
                    var rowBytes = (FontAssetSample.Assets.Slant.CellWidth + 7) / 8;
                    var on = (FontAssetSample.Assets.Slant.Glyphs[((all[k] - 32) * 9 + y) * rowBytes + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                    var o = (py * img.Width + px) * 4;
                    var got = (img.Rgba[o], img.Rgba[o + 1], img.Rgba[o + 2]);
                    if (got != (on ? (255, 255, 136) : (0, 0, 85))) wrong++;
                }
        Assert.Equal(0, wrong);
    }
}
