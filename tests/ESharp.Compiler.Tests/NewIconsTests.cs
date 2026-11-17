using System.Collections.Generic;
using System.Linq;
using System.Text;
using ESharp.Assets;
using ESharp.Cli;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>The NewIcons image in E# icons (issue #101): encoding, line limits, the csproj switch, and Args skipping the lines on a Workbench start.</summary>
public class NewIconsTests
{
    private static int Be16(byte[] d, int o) => d[o] << 8 | d[o + 1];
    private static int Be32(byte[] d, int o) => Be16(d, o) << 16 | Be16(d, o + 2);

    // The tool types straight from the file bytes (no DiskObject.Read): header, classic image, then the table.
    private static List<string> RawToolTypes(byte[] d)
    {
        var pos = 78;
        Assert.NotEqual(0, Be32(d, 22));                      // an image follows
        Assert.NotEqual(0, Be32(d, 54));                      // tool types follow
        int w = Be16(d, pos + 4), h = Be16(d, pos + 6), depth = Be16(d, pos + 8);
        pos += 20 + (w + 15) / 16 * 2 * h * depth;
        var count = Be32(d, pos) / 4 - 1; pos += 4;
        var list = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var n = Be32(d, pos); pos += 4;
            Assert.Equal(0, d[pos + n - 1]);
            list.Add(Encoding.Latin1.GetString(d, pos, n - 1)); pos += n;
        }
        return list;
    }

    /// <summary>
    /// A NewIcons reader written for the test, apart from the one in ESharp.Assets: it walks the characters the way icon.library readers do
    /// (a bit buffer that keeps going, its count reset at each line, values taken while enough bits are there), so a line whose padding
    /// would make an extra value is caught.
    /// </summary>
    private static (int W, int H, bool Transparent, uint[] Palette, byte[] Pixels) ReadNi(IReadOnlyList<string> tt, int which)
    {
        var start = tt.ToList().IndexOf(NewIcons.Marker);
        Assert.True(start >= 0, "no marker");
        var tag = "IM" + (char)('0' + which) + "=";
        var lines = new Queue<string>(tt.Skip(start + 1).Where(t => t.StartsWith(tag)).Select(t => t[4..]));
        var head = lines.Dequeue();
        int w = head[1] - 33, h = head[2] - 33, n = (head[3] - 33) * 64 + (head[4] - 33);
        var src = head[5..];
        long bitbuf = 0;
        byte[] Take(int entries, int bits, string first)
        {
            var o = new byte[entries]; int got = 0, numbits = 0, run = 0, at = 0; var s = first;
            while (got < entries)
            {
                int v;
                if (run > 0) { v = 0; run--; }
                else
                {
                    if (at >= s.Length) { s = lines.Dequeue(); at = 0; numbits = 0; }
                    int c = s[at++];
                    Assert.True(c >= 0x20 && c < 0x80 || c >= 0xA1 && c <= 0xFF, $"character {c:X2}");
                    if (c < 0xA0) v = c - 0x20; else if (c < 0xD1) v = c - 0x51; else { run = c - 0xD1; v = 0; }
                }
                bitbuf = (bitbuf << 7) + v; numbits += 7;
                while (numbits >= bits && got < entries) { o[got++] = (byte)(bitbuf >> (numbits - bits) & ((1 << bits) - 1)); numbits -= bits; }
            }
            return o;
        }
        var pal = Take(n * 3, 8, src);
        var bits = 1; while ((1 << bits) < n) bits++;
        var px = Take(w * h, bits, "");
        Assert.Empty(lines);                                  // nothing left over: the encoder wrote exactly what was needed
        var palette = Enumerable.Range(0, n).Select(i => (uint)(pal[i * 3] << 16 | pal[i * 3 + 1] << 8 | pal[i * 3 + 2])).ToArray();
        return (w, h, head[0] == 'B', palette, px);
    }

    private static void AssertSamePicture(IconPicture want, (int W, int H, bool Transparent, uint[] Palette, byte[] Pixels) got)
    {
        Assert.Equal(want.Width, got.W); Assert.Equal(want.Height, got.H);
        Assert.Equal(want.TransparentIndex >= 0, got.Transparent);
        // Compare colours per pixel (the encoder may swap the transparent entry into place 0).
        for (var i = 0; i < want.Indices.Length; i++)
        {
            var wt = want.Indices[i] == want.TransparentIndex;
            Assert.Equal(wt, got.Transparent && got.Pixels[i] == 0);
            if (!wt) Assert.Equal(want.Palette[want.Indices[i]], got.Palette[got.Pixels[i]]);
        }
        Assert.Equal(want.Palette.Length, got.Palette.Length);
        Assert.Equal(want.Palette.OrderBy(c => c), got.Palette.OrderBy(c => c));
    }

    private static IconPicture Random(int w, int h, int colours, int transparent, int seed)
    {
        var r = new System.Random(seed);
        var pal = Enumerable.Range(0, colours).Select(_ => (uint)r.Next(0x1000000)).ToArray();
        // Runs of the same index, with some long runs of 0 so the zero-run characters are used.
        var px = new byte[w * h];
        for (var i = 0; i < px.Length;)
        {
            var v = (byte)(r.Next(4) == 0 ? 0 : r.Next(colours)); var len = r.Next(1, v == 0 ? 200 : 6);
            for (var k = 0; k < len && i < px.Length; k++) px[i++] = v;
        }
        return new IconPicture(w, h, px, pal, transparent);
    }

    [Fact]
    public void Default_icon_round_trips_through_an_independent_reader()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp-ni-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(path, "G.csproj"), "<Project><PropertyGroup><EsToolTypes>SEED=42;AUTO</EsToolTypes></PropertyGroup></Project>");
            var d = System.IO.File.ReadAllBytes(IconStep.Write(path, path, "G", amiga: true)!);
            var tt = RawToolTypes(d);
            Assert.Equal(new[] { "SEED=42", "AUTO", NewIcons.Marker }, tt.Take(3));   // the user's tool types first
            var (_, colour) = IconArt.Default();
            AssertSamePicture(colour, ReadNi(tt, 1));
            AssertSamePicture(NewIcons.Selected(colour), ReadNi(tt, 2));
            // The colour FORM is still there, and Read gives the user's tool types only.
            Assert.Contains("FORM", Encoding.Latin1.GetString(d));
            var back = DiskObject.Read(d);
            Assert.Equal(new[] { "SEED=42", "AUTO" }, back.ToolTypes);
            Assert.Equal(colour.Indices, back.NewIcons!.Indices);
            Assert.Equal(colour.Palette, back.NewIcons.Palette);
            Assert.NotNull(back.Colour);
        }
        finally { System.IO.Directory.Delete(path, true); }
    }

    [Theory]
    [InlineData(1, 1, 2, -1, 1)]
    [InlineData(5, 3, 2, 0, 2)]
    [InlineData(64, 64, 32, 0, 3)]
    [InlineData(93, 93, 32, 3, 4)]
    [InlineData(17, 40, 5, -1, 5)]
    [InlineData(64, 64, 16, 0, 6)]
    [InlineData(33, 9, 3, 2, 7)]
    public void Pictures_round_trip_and_every_line_is_within_the_limits(int w, int h, int colours, int transparent, int seed)
    {
        var pic = Random(w, h, colours, transparent, seed);
        var o = new DiskObject { Classic = new IconPicture(w, h, new byte[w * h], IconArt.ClassicPalette), NewIcons = pic };
        o.ToolTypes.Add("X=1");
        var tt = RawToolTypes(o.Write());
        AssertSamePicture(pic, ReadNi(tt, 1));
        AssertSamePicture(NewIcons.Selected(pic), ReadNi(tt, 2));
        foreach (var t in tt.Skip(2))
        {
            Assert.True(t.Length <= NewIcons.MaxLine, $"{t.Length} characters");
            Assert.True(t.StartsWith("IM1=") || t.StartsWith("IM2="));
            Assert.DoesNotContain('\0', t);
        }
        var back = DiskObject.Read(o.Write());
        Assert.Equal(new[] { "X=1" }, back.ToolTypes);
        AssertSamePicture(pic, ReadNi(NewIcons.Encode(back.NewIcons!, back.NewIconsSelected), 1));
    }

    [Fact]
    public void Limits_are_refused()
    {
        Assert.ThrowsAny<System.ArgumentException>(() => NewIcons.Encode(Random(94, 2, 2, -1, 1)));
        Assert.ThrowsAny<System.ArgumentException>(() => NewIcons.Encode(Random(8, 8, 33, -1, 1)));
    }

    [Fact]
    public void A_PNG_with_many_colours_is_reduced_for_NewIcons()
    {
        var rgba = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var o = (y * 64 + x) * 4;
                rgba[o] = (byte)(x * 4); rgba[o + 1] = (byte)(y * 4); rgba[o + 2] = (byte)((x ^ y) * 4);
                rgba[o + 3] = (byte)(x < 4 ? 0 : 255);
            }
        var png = Png.Write(new RgbaImage(64, 64, rgba));
        var (_, colour) = IconArt.FromPng(png);
        Assert.True(colour.Palette.Length > NewIcons.MaxColours);
        var ni = IconArt.NewIconsFromPng(png)!;
        Assert.InRange(ni.Palette.Length, 2, NewIcons.MaxColours);
        Assert.Equal(0, ni.TransparentIndex);
        Assert.Equal(0, ni.Indices[0]);
        Assert.NotEqual(0, ni.Indices[10]);
        AssertSamePicture(ni, ReadNi(NewIcons.Encode(ni), 1));
        Assert.Null(IconArt.NewIconsFromPng(Png.Write(new RgbaImage(100, 10, new byte[100 * 10 * 4]))));
    }

    [Fact]
    public void EsNewIcons_false_leaves_the_lines_out()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp-ni-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "G.csproj"), "<Project><PropertyGroup><EsNewIcons>false</EsNewIcons><EsToolTypes>A</EsToolTypes></PropertyGroup></Project>");
            Assert.False(IconStep.ReadSettings(dir).NewIcons);
            var d = System.IO.File.ReadAllBytes(IconStep.Write(dir, dir, "G", amiga: true)!);
            Assert.Equal(new[] { "A" }, RawToolTypes(d));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "G.csproj"), "<Project><PropertyGroup><EsNewIcons>maybe</EsNewIcons></PropertyGroup></Project>");
            Assert.Throws<System.IO.InvalidDataException>(() => IconStep.ReadSettings(dir));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    private const string ShowArgs = @"
public static class P
{
    [EntryPoint]
    public static void Main()
    {
        Dos.PutStr(""n="" + Args.Count + ""\n"");
        for (int i = 0; i < Args.Count; i++) { Dos.PutStr(Args.Get(i)); Dos.PutStr(""\n""); }
    }
}";

    private static string CString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
            if (ch < 0x20 || ch >= 0x7F || ch == '"' || ch == '\\' || ch == '?') sb.Append('\\').Append(System.Convert.ToString(ch, 8).PadLeft(3, '0'));
            else sb.Append(ch);
        return sb.Append('"').ToString();
    }

    [Fact]
    public void A_Workbench_start_skips_the_NewIcons_lines()
    {
        var c = CompileOk(Prelude + ShowArgs);
        if (!HasGcc) return;
        // The real lines of the default icon after the user's tool types, and an IM1= before the marker (a program's own word, kept).
        var (_, colour) = IconArt.Default();
        var tt = new List<string> { "SEED=42", "IM1=mine", "AUTO" };
        tt.AddRange(NewIcons.Encode(colour));
        Assert.True(tt.Count > 10);
        var init = "{ static char *tt[] = { " + string.Join(", ", tt.Select(CString)) + ", NULL }; es_args_from_tooltypes(\"prog\", tt); }";
        var test = c.Replace("es_argc = argc; es_argv = argv;", init);
        Assert.NotEqual(c, test);
        Assert.Equal((0, "n=3\nSEED=42\nIM1=mine\nAUTO\n"), RunWithHostGcc(test));
        // AROS's icon.library empties the marker and takes the IM lines out when it reads the picture: an empty word, skipped as before.
        var aros = c.Replace("es_argc = argc; es_argv = argv;", "{ static char *tt[] = { \"SEED=42\", \"\", NULL }; es_args_from_tooltypes(\"prog\", tt); }");
        Assert.Equal((0, "n=1\nSEED=42\n"), RunWithHostGcc(aros));
    }

    [Fact]
    public void The_floppy_pack_strip_script_leaves_the_icon_as_EsNewIcons_false_writes_it()
    {
        // tools/hwpack/stripni.py takes the NewIcons lines out of the icons on hwtest.adf (no room for them on one floppy).
        var script = System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "hwpack", "stripni.py");
        var (classic, colour) = IconArt.Default();
        DiskObject Icon(bool ni)
        {
            var o = new DiskObject { StackSize = 32768, Classic = classic, Colour = colour, NewIcons = ni ? colour : null };
            o.ToolTypes.Add("SEED=42"); o.ToolTypes.Add("AUTO");
            return o;
        }
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "esharp-ni-" + System.Guid.NewGuid().ToString("N") + ".info");
        try
        {
            System.IO.File.WriteAllBytes(file, Icon(true).Write());
            System.Diagnostics.Process? p;
            try { p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("python3", new[] { "-I", script, file }) { RedirectStandardOutput = true }); }
            catch (System.ComponentModel.Win32Exception) { return; }   // no python3 here
            p!.WaitForExit();
            Assert.Equal(0, p.ExitCode);
            Assert.Equal(Icon(false).Write(), System.IO.File.ReadAllBytes(file));
            var none = new DiskObject { Classic = classic, NewIcons = colour };
            System.IO.File.WriteAllBytes(file, none.Write());
            p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("python3", new[] { "-I", script, file }) { RedirectStandardOutput = true });
            p!.WaitForExit();
            var back = DiskObject.Read(System.IO.File.ReadAllBytes(file));
            Assert.Empty(back.ToolTypes); Assert.Null(back.NewIcons);
            Assert.Equal(new DiskObject { Classic = classic }.Write(), System.IO.File.ReadAllBytes(file));
        }
        finally { System.IO.File.Delete(file); }
    }
}
