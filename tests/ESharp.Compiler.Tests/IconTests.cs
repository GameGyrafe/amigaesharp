using System.Collections.Generic;
using System.Text;
using ESharp.Assets;
using ESharp.Cli;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>Workbench icons (issue #84): the .info writer, the csproj settings, and Args reading tool types on a Workbench start.</summary>
public class IconTests
{
    private static int Be16(byte[] d, int o) => d[o] << 8 | d[o + 1];
    private static int Be32(byte[] d, int o) => Be16(d, o) << 16 | Be16(d, o + 2);

    private static DiskObject Sample()
    {
        var (classic, colour) = IconArt.Default();
        var o = new DiskObject { StackSize = 40000, Classic = classic, Colour = colour, DefaultTool = null };
        o.ToolTypes.Add("SEED=42"); o.ToolTypes.Add("AUTO"); o.ToolTypes.Add("(OFF)");
        return o;
    }

    [Fact]
    public void Header_fields_are_big_endian_at_the_documented_offsets()
    {
        var d = Sample().Write();
        Assert.Equal(0xE310, Be16(d, 0));
        Assert.Equal(1, Be16(d, 2));
        Assert.Equal(48, Be16(d, 12));              // gadget width = image width
        Assert.Equal(32, Be16(d, 14));              // gadget height
        Assert.Equal(3, d[48]);                      // do_Type: WBTOOL
        Assert.Equal(40000, Be32(d, 74));            // do_StackSize
        Assert.Equal(unchecked((int)0x80000000), Be32(d, 58));   // no position
        Assert.Equal(unchecked((int)0x80000000), Be32(d, 62));
        Assert.Equal(0, Be32(d, 66));                // no drawer data
    }

    [Fact]
    public void Write_then_read_gives_the_same_fields_and_pictures()
    {
        var o = Sample();
        var r = DiskObject.Read(o.Write());
        Assert.Equal(IconType.Tool, r.Type);
        Assert.Equal(40000, r.StackSize);
        Assert.Equal(new[] { "SEED=42", "AUTO", "(OFF)" }, r.ToolTypes);
        Assert.Null(r.DefaultTool);
        Assert.Equal(o.Classic!.Indices, r.Classic!.Indices);
        Assert.Equal(48, r.Classic.Width); Assert.Equal(32, r.Classic.Height);
        Assert.Equal(o.Colour!.Indices, r.Colour!.Indices);
        Assert.Equal(o.Colour.Palette, r.Colour.Palette);
        Assert.Equal(0, r.Colour.TransparentIndex);
    }

    [Fact]
    public void Default_tool_and_tool_window_and_no_colour_part()
    {
        var (classic, _) = IconArt.Default();
        var o = new DiskObject { Type = IconType.Project, DefaultTool = "SYS:Utilities/MultiView", ToolWindow = "CON:0/0/200/100/x", Classic = classic };
        var d = o.Write();
        var r = DiskObject.Read(d);
        Assert.Equal(IconType.Project, r.Type);
        Assert.Equal("SYS:Utilities/MultiView", r.DefaultTool);
        Assert.Equal("CON:0/0/200/100/x", r.ToolWindow);
        Assert.Empty(r.ToolTypes);
        Assert.Null(r.Colour);
        Assert.DoesNotContain("FORM", Encoding.ASCII.GetString(d));
    }

    [Fact]
    public void Sizes_add_up_and_the_classic_image_is_word_padded_planes()
    {
        var d = Sample().Write();
        // header 78 + image record 20 + 2 planes of 32 rows x 6 bytes, then the tool type strings, then the FORM.
        var imageEnd = 78 + 20 + 2 * 32 * 6;
        Assert.Equal(2, Be16(d, 78 + 8));            // depth 2
        Assert.Equal(48, Be16(d, 78 + 4));
        Assert.Equal((3 + 1) * 4, Be32(d, imageEnd));   // the table size is (count + 1) * 4
        Assert.Equal(8, Be32(d, imageEnd + 4));      // "SEED=42" and its zero byte
        var form = Encoding.ASCII.GetString(d).IndexOf("FORM", StringComparison.Ordinal);
        Assert.True(form > imageEnd);
        Assert.Equal(d.Length - form - 8, Be32(d, form + 4));   // the FORM size covers everything after its own header
        Assert.Equal("ICON", Encoding.ASCII.GetString(d, form + 8, 4));
        Assert.Equal("FACE", Encoding.ASCII.GetString(d, form + 12, 4));
        Assert.Equal(6, Be32(d, form + 16));
        Assert.Equal(47, d[form + 20]); Assert.Equal(31, d[form + 21]);    // width - 1, height - 1
        Assert.Equal("IMAG", Encoding.ASCII.GetString(d, form + 26, 4));
        Assert.Equal(7, d[form + 26 + 8 + 1] + 1);                       // colours in the palette
        Assert.Equal(8, d[form + 26 + 8 + 5]);                           // depth field: one byte per pixel (what AmigaOS 4.1 accepts)
        Assert.Equal(48 * 32 - 1, Be16(d, form + 26 + 8 + 6));           // image bytes - 1
        Assert.Equal(7 * 3 - 1, Be16(d, form + 20 + 4));                 // FACE: largest palette in bytes - 1
        Assert.True(d.Length < 4096);
    }

    [Fact]
    public void Odd_width_and_drawers()
    {
        var pic = new IconPicture(5, 3, new byte[15], IconArt.ClassicPalette);
        var d = new DiskObject { Classic = pic }.Write();
        Assert.Equal(78 + 20 + 2 * 3 * 2, d.Length);   // 5 pixels pad to one word per row
        Assert.Throws<NotSupportedException>(() => new DiskObject { Type = IconType.Drawer }.Write());
        Assert.Throws<InvalidDataException>(() => DiskObject.Read(new byte[100]));
    }

    [Fact]
    public void PNG_becomes_a_four_colour_classic_and_a_colour_image()
    {
        var rgba = new byte[8 * 8 * 4];
        for (var i = 0; i < 64; i++)
        {
            var left = i % 8 < 4;
            rgba[i * 4] = (byte)(left ? 250 : 10); rgba[i * 4 + 1] = (byte)(left ? 250 : 20); rgba[i * 4 + 2] = (byte)(left ? 250 : 200);
            rgba[i * 4 + 3] = (byte)(i < 8 ? 0 : 255);
        }
        var (classic, colour) = IconArt.FromPng(Png.Write(new RgbaImage(8, 8, rgba)));
        Assert.Equal(0, classic.Indices[0]);          // transparent: background
        Assert.Equal(2, classic.Indices[8]);          // white
        Assert.Equal(3, classic.Indices[8 + 7]);      // blue
        Assert.Equal(0, colour.TransparentIndex);
        Assert.Equal(8, colour.Width);
        Assert.Throws<InvalidDataException>(() => IconArt.FromPng(Png.Write(new RgbaImage(300, 2, new byte[300 * 2 * 4]))));
    }

    [Fact]
    public void Settings_come_from_the_csproj_and_default_otherwise()
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var none = IconStep.ReadSettings(dir);
            Assert.Equal(IconStep.DefaultStackSize, none.StackSize);
            Assert.Empty(none.ToolTypes);
            File.WriteAllText(Path.Combine(dir, "G.csproj"), "<Project><PropertyGroup><EsStackSize>32768</EsStackSize><EsToolTypes>SEED=42; AUTO;</EsToolTypes><EsIcon>art/i.png</EsIcon></PropertyGroup></Project>");
            var s = IconStep.ReadSettings(dir);
            Assert.Equal(32768, s.StackSize);
            Assert.Equal(new[] { "SEED=42", "AUTO" }, s.ToolTypes);
            Assert.EndsWith(Path.Combine("art", "i.png"), s.IconPng);
            File.WriteAllText(Path.Combine(dir, "G.csproj"), "<Project><PropertyGroup><EsStackSize>12</EsStackSize></PropertyGroup></Project>");
            Assert.Throws<InvalidDataException>(() => IconStep.ReadSettings(dir));
            File.WriteAllText(Path.Combine(dir, "G.csproj"), "<Project><PropertyGroup><EsIcon>none</EsIcon></PropertyGroup></Project>");
            Assert.Null(IconStep.Write(dir, dir, "G", amiga: true));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_icon_is_written_for_Amiga_targets_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(IconStep.Write(dir, dir, "G", amiga: false));
            Assert.False(File.Exists(Path.Combine(dir, "G.info")));
            var path = IconStep.Write(dir, dir, "G", amiga: true);
            Assert.Equal(Path.Combine(dir, "G.info"), path);
            Assert.Equal(IconStep.DefaultStackSize, DiskObject.Read(File.ReadAllBytes(path!)).StackSize);
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string ArgsProgram = @"
public static class P
{
    [EntryPoint]
    public static void Main()
    {
        Dos.PutStr(""n="" + Args.Count + "" seed="" + Args.Int(""SEED"", -1) + "" auto="" + (Args.Has(""auto"") ? ""yes"" : ""no"") + "" off="" + (Args.Has(""(OFF)"") || Args.Has(""OFF"") ? ""yes"" : ""no"") + ""\n"");
    }
}";

    [Fact]
    public void Tool_types_stand_in_for_the_command_line_and_disabled_ones_are_skipped()
    {
        var c = CompileOk(Prelude + ArgsProgram);
        Assert.Contains("es_args_from_tooltypes", c);
        if (!HasGcc) return;
        // Replace the host path of main with the tool type conversion (the Amiga path calls it from the Workbench start).
        var test = c.Replace("es_argc = argc; es_argv = argv;", "{ static char *tt[] = { \"SEED=42\", \"(OFF)\", \"AUTO\", \"\", NULL }; es_args_from_tooltypes(\"prog\", tt); }");
        Assert.NotEqual(c, test);
        Assert.Equal((0, "n=2 seed=42 auto=yes off=no\n"), RunWithHostGcc(test));
        var none = c.Replace("es_argc = argc; es_argv = argv;", "es_args_from_tooltypes(\"prog\", NULL);");
        Assert.Equal((0, "n=0 seed=-1 auto=no off=no\n"), RunWithHostGcc(none));
    }

    private const string ShowArgs = @"
public static class P
{
    [EntryPoint]
    public static void Main()
    {
        for (int i = 0; i < Args.Count; i++) { Dos.PutStr(Args.Get(i)); Dos.PutStr(""\n""); }
    }
}";

    // A stand-in for icon.library and the Workbench startup structures: counts open libraries and loaded DiskObjects.
    private const string FakeIcon = @"
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
typedef long BPTR; typedef const unsigned char *CONST_STRPTR;
struct Library { int x; };
struct DiskObject { char **do_ToolTypes; };
struct WBArg { BPTR wa_Lock; char *wa_Name; };
struct WBStartup { long sm_NumArgs; struct WBArg *sm_ArgList; };
static int es_t_open, es_t_dobs;
static struct Library es_t_lib;
static inline struct Library *OpenLibrary(CONST_STRPTR n, unsigned v) { (void)n; (void)v; es_t_open++; return &es_t_lib; }
static inline void CloseLibrary(struct Library *l) { (void)l; es_t_open--; }
static inline BPTR CurrentDir(BPTR b) { return b; }
static char *es_t_src[] = { ""SEED=42"", ""(OFF)"", ""AUTO"", """", ""TOOLPRI=5"", ""NAME=\""two words\"""", ""donotwait"", ""WINDOW=small"", NULL };
static inline struct DiskObject *GetDiskObject(CONST_STRPTR n)
{
    (void)n; es_t_dobs++;
    struct DiskObject *d = malloc(sizeof *d);
    d->do_ToolTypes = malloc(sizeof es_t_src);
    for (int i = 0; i < 9; i++) d->do_ToolTypes[i] = es_t_src[i] ? strdup(es_t_src[i]) : NULL;
    return d;
}
static inline void FreeDiskObject(struct DiskObject *d)
{
    es_t_dobs--;
    for (int i = 0; d->do_ToolTypes[i]; i++) free(d->do_ToolTypes[i]);
    free(d->do_ToolTypes); free(d);
}
static struct WBArg es_t_arg = { 0, ""prog"" };
static struct WBStartup es_t_wb = { 1, &es_t_arg };
static void __attribute__((destructor)) es_t_report(void) { printf(""open %d diskobjects %d\n"", es_t_open, es_t_dobs); }
";

    [Fact]
    public void A_Workbench_start_reads_the_icon_skips_workbench_tool_types_and_releases_everything()
    {
        var c = CompileOk(Prelude + ShowArgs);
        if (!HasGcc) return;
        var headers = new Dictionary<string, string>
        {
            ["proto/icon.h"] = FakeIcon, ["workbench/startup.h"] = "/* fake */\n", ["workbench/workbench.h"] = "/* fake */\n",
        };
        var test = "#define __AMIGA__ 1\n" + c.Replace("argc == 0", "1").Replace("(struct WBStartup *)argv", "&es_t_wb");
        Assert.Contains("&es_t_wb", test);
        // Quotes around a value are removed, TOOLPRI / donotwait / (OFF) / empty are skipped, and the library and the DiskObject are released.
        Assert.Equal((0, "SEED=42\nAUTO\nNAME=two words\nWINDOW=small\nopen 0 diskobjects 0\n"), RunWithHostGcc(test, headers));
    }

    [Fact]
    public void An_opaque_black_pixel_next_to_a_transparent_one_stays_opaque()
    {
        var rgba = new byte[4 * 4]
        {
            0, 0, 0, 0,        255, 255, 255, 255,
            0, 0, 0, 255,      255, 0, 0, 255,
        };
        var (_, colour) = IconArt.FromPng(Png.Write(new RgbaImage(2, 2, rgba)));
        Assert.Equal(0, colour.TransparentIndex);
        Assert.Equal(0, colour.Indices[0]);                       // the transparent pixel
        Assert.NotEqual(0, colour.Indices[2]);                    // the opaque black one
        Assert.Equal(0x000000u, colour.Palette[colour.Indices[2]]);
        Assert.NotEqual(colour.Indices[2], colour.Indices[1]);
    }

    [Fact]
    public void An_msbuild_property_in_a_setting_is_refused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "esharp-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "G.csproj"), "<Project><PropertyGroup><EsIcon>$(Foo).png</EsIcon></PropertyGroup></Project>");
            var e = Assert.Throws<InvalidDataException>(() => IconStep.ReadSettings(dir));
            Assert.Contains("literal", e.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_Workbench_start_code_is_in_the_Amiga_C_and_not_in_sdl()
    {
        var src = Prelude + ArgsProgram;
        foreach (var t in new[] { Targets.M68k, Targets.Os4, Targets.MorphOs, Targets.Aros, Targets.ArosI386 })
        {
            var r = EsCompiler.Compile(new[] { new SourceFile("P.cs", src) }, t.ToOptions());
            Assert.True(r.Success, string.Join("\n", r.Diagnostics));
            Assert.Contains("es_args_from_workbench((struct WBStartup *)argv)", r.C);
            Assert.Contains("GetDiskObject", r.C);
        }
    }
}
