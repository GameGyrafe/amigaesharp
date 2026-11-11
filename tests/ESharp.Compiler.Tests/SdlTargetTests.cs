using System.Diagnostics;
using ESharp.Analyzers;
using ESharp.Cli;
using ESharp.Compiler;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// The sdl target: its symbols, the standard C part of its runtime (built and run with the host gcc, no SDL needed), the
/// Amiga-only API error, the CLI helpers for --input and --screenshot, and (when the esharp-sdl image is present) a program
/// that draws, reads its input and writes its screenshot, built and run headless in the image.
/// </summary>
public class SdlTargetTests
{
    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");

    [Fact]
    public void Sdl_defines_SDL_and_not_AMIGA_and_links_SDL_with_pkg_config()
    {
        var t = Targets.Find("sdl")!;
        Assert.Same(Targets.Sdl, t);
        Assert.Contains("SDL", t.Defines);
        Assert.DoesNotContain("AMIGA", t.Defines);
        Assert.DoesNotContain("M68K", t.Defines);
        Assert.False(t.IsAmiga);
        Assert.False(t.IsM68k);
        Assert.Equal("sdl3", t.PkgConfig);
        Assert.Equal("gcc", t.Compiler);
        Assert.Contains("-fwrapv", t.CFlags);
        Assert.All(Targets.All.Where(x => x != Targets.Sdl), x => Assert.True(x.IsAmiga));
    }

    [Fact]
    public void Source_can_branch_on_AMIGA_and_SDL()
    {
        const string src = @"
public static class P
{
    [EntryPoint]
    public static int Main()
    {
#if AMIGA
        return 1;
#elif SDL
        return 2;
#else
        return 3;
#endif
    }
}";
        Assert.Contains("return 2;", CompileOk(Prelude + src, Targets.Sdl.ToOptions()));
        Assert.Contains("return 1;", CompileOk(Prelude + src, Targets.M68k.ToOptions()));
    }

    [Fact]
    public void Printing_needs_only_standard_C_no_amiga_headers_and_no_SDL()
    {
        var c = CompileOk(Program("", "Dos.PutStr(\"hi\\n\"); Dos.FlushOutput();"), Targets.Sdl.ToOptions());
        Assert.DoesNotContain("proto/", c);
        Assert.DoesNotContain("SDL3", c);
        Assert.Contains("es_std_putstr", c);
    }

    [Fact]
    public void Video_pulls_in_the_SDL_runtime()
    {
        var c = CompileOk(Program("", "if (Video.Open(320, 200, 4)) { Video.FillRect(0, 0, 10, 10, 1); Video.Close(); }"), Targets.Sdl.ToOptions());
        Assert.Contains("#include <SDL3/SDL.h>", c);
        Assert.Contains("es_sdl_video_open(", c);
        Assert.DoesNotContain("proto/", c);
    }

    [Fact]
    public void Amiga_only_APIs_are_a_clear_compile_error_on_sdl()
    {
        var r = Compile(Program("", "var w = new GtWindow(); Joystick.Open();"), Targets.Sdl.ToOptions());
        Assert.False(r.Success);
        var ids = ErrorIds(r);
        Assert.Contains("ES0102", ids);
        var messages = r.Diagnostics.Where(d => d.Id == "ES0102").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.Contains("'GtWindow'") && m.Contains("'sdl'"));
        Assert.Contains(messages, m => m.Contains("'Joystick'"));
        Assert.Equal(DiagnosticSeverityError, Descriptors.AmigaOnly.DefaultSeverity);
    }

    private const Microsoft.CodeAnalysis.DiagnosticSeverity DiagnosticSeverityError = Microsoft.CodeAnalysis.DiagnosticSeverity.Error;

    [Fact]
    public void Standard_C_runtime_runs_natively_memory_files_dates_keys()
    {
        if (!HasGcc) return;
        const string body = @"
uint before = Mem.AvailChip();
byte* p = Mem.AllocChip(1000);
Check(p != null && p[999] == 0 && Mem.AvailChip() == before - 1000, ""alloc is zeroed and counted"");
Mem.Free(p);
Check(Mem.AvailChip() == before && Mem.AllocFast(0x7FFFFFF0) == null, ""free returns it, too much fails"");
byte[] data = { 1, 2, 3, 250 };
Check(Files.WriteAll(""es_sdl_test.bin"", data), ""write"");
byte* back = Files.ReadAll(""es_sdl_test.bin"", out int size, false);
Check(back != null && size == 4 && back[3] == 250, ""read back"");
Mem.Free(back);
Check(Files.Delete(""es_sdl_test.bin"") && Files.ReadAll(""es_sdl_test.bin"", out int none, false) == null, ""delete"");
DateParts d = Date.Split(0);
Check(d.Year == 1978 && d.Month == 1 && d.Day == 1 && d.Weekday == 0, ""epoch is Sunday 1 January 1978"");
DateParts leap = new DateParts { Year = 2024, Month = 2, Day = 29, Hour = 23, Minute = 59, Second = 58 };
Check(Date.TryJoin(leap, out uint s) && Date.Split(s).Day == 29 && Date.Split(s).Month == 2 && Date.Split(s).Second == 58, ""leap day round trip"");
leap.Year = 2023;
Check(!Date.TryJoin(leap, out uint t), ""no 29 February 2023"");
Check(Date.Now() > 1400000000u, ""clock is after 2022"");
Check(Keys.Char(0x20, 0) == 'a' && Keys.Char(0x20, Keys.LeftShift) == 'A' && Keys.Char(0x45, 0) == 27, ""US keys"");
Check(Keys.Find('Q').Code == 0x10 && Keys.Find('Q').Qualifier == Keys.LeftShift, ""find a key"");
";
        const string members = @"static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }";
        var c = CompileOk(Prelude + "public static unsafe class P { [EntryPoint] public static void Main() {" + body + "} " + members + " }", Targets.Sdl.ToOptions());
        Assert.DoesNotContain("SDL3", c);
        var (code, output) = RunWithHostGcc(c);
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Equal(11, output.Split('\n').Count(l => l.StartsWith("ok ")));
    }

    [Theory]
    [InlineData("collections", "Collections.cs")]
    [InlineData("inherit", "Inherit.cs")]
    public void Pure_samples_on_sdl_run_natively_with_the_same_output_as_dotnet(string dir, string file)
    {
        if (!HasGcc) return;
        var path = Path.Combine(Samples, dir, file);
        var r = EsCompiler.Compile(new[] { new SourceFile(path, File.ReadAllText(path)) }, Targets.Sdl.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.DoesNotContain("proto/", r.C!);
        var (code, output) = RunWithHostGcc(r.C!);
        Assert.Equal(0, code);
        Assert.Equal(dir == "collections" ? CollectionsExpected.Output : InheritExpected.Output, output);
    }

    [Fact]
    public void Input_script_keeps_pointer_steps_and_turns_X11_keys_into_Amiga_raw_keys()
    {
        Assert.Equal("click 400,300;rel -80,50;press;key 69", SdlRun.TranslateInput("click 400,300; rel -80,50 ;press;key 9"));
        Assert.Equal("key 96+32", SdlRun.TranslateInput("key 50+38"));        // Shift+A
        Assert.Equal("key 16;key 49;key 64;key 68", SdlRun.TranslateInput("key 24;key 52;key 65;key 36"));   // q z space return
        Assert.Throws<FormatException>(() => SdlRun.TranslateInput("wiggle 3"));
        Assert.Throws<FormatException>(() => SdlRun.TranslateInput("key 255"));
        Assert.Throws<FormatException>(() => SdlRun.TranslateInput("click 1"));
    }

    [Fact]
    public void Screenshot_ppm_is_read_into_an_image()
    {
        var header = System.Text.Encoding.ASCII.GetBytes("P6\n2 1\n255\n");
        var ppm = header.Concat(new byte[] { 255, 0, 0, 0, 128, 255 }).ToArray();
        var img = SdlRun.ReadPpm(ppm);
        Assert.Equal(2, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 128, 255, 255 }, img.Rgba);
        Assert.Throws<InvalidDataException>(() => SdlRun.ReadPpm(System.Text.Encoding.ASCII.GetBytes("P5\n1 1\n255\n\0")));
    }

    [Fact]
    public void Test_report_names_the_other_side()
    {
        var host = TestReport.Parse("ok a\nok b\n");
        var sdl = TestReport.Parse("ok a\nFAIL b\n");
        Assert.Equal(new[] { "b: host ok, SDL FAIL" }, TestReport.Compare(host, sdl, "SDL"));
    }

    // ---- in the esharp-sdl image (skipped when it is not built locally) ----

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

    /// <summary>
    /// A program that opens a screen, draws, checks its pixels, reads a scripted click and key (ESHARP_INPUT, replayed after SHOT),
    /// and writes its screenshot, built with static SDL 3 and run headless (offscreen video, dummy audio) in the esharp-sdl image.
    /// </summary>
    [Fact]
    public void Sdl_program_draws_reads_scripted_input_and_writes_a_screenshot_in_the_image()
    {
        if (!HasSdlImage()) return;
        const string src = @"
public static class P
{
    static void Check(bool ok, string name) { Dos.PutStr(ok ? ""ok "" : ""FAIL ""); Dos.PutStr(name); Dos.PutStr(""\n""); }

    [EntryPoint]
    public static void Main()
    {
        Check(Video.Open(64, 32, 3), ""open"");
        Video.SetColor(1, 15, 0, 0);
        Video.SetColor24(2, 0, 255, 0);
        Video.FillRect(0, 0, 64, 32, 1);
        Video.FillRect(10, 5, 4, 3, 2);
        Check(Video.GetPixel(11, 6) == 2 && Video.GetPixel(0, 0) == 1 && Video.GetPixel(64, 0) == -1, ""pixels"");
        Check(Sound.Open() && Sound.VoiceCount == 4, ""sound opens"");
        Dos.PutStr(""SHOT\n"");
        int clicks = 0, key = 0, x = -1, y = -1;
        for (int i = 0; i < 200 && key == 0; i++)
        {
            Input.Poll();
            if (Input.LeftPressed) { clicks++; x = Input.MouseX; y = Input.MouseY; }
            if (Input.LastKey != 0) key = Input.LastKey;
            Frame.WaitVBlank();
        }
        Check(clicks == 1 && x == 12 && y == 7, ""scripted click"");
        Check(key == 0x45, ""scripted Esc"");
        Sound.Close();
        Video.Close();
    }
}";
        var dir = Path.Combine(Path.GetTempPath(), "esharp-sdl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "prog.c"), CompileOk(Prelude + src, Targets.Sdl.ToOptions()));
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
            var (exit, output) = SdlRun.Run(Path.Combine(dir, "prog"), true, TimeSpan.FromSeconds(60), "esharp-sdl", png, "click 12,7;key 9", echo: false);
            Assert.Equal(0, exit);
            Assert.DoesNotContain("FAIL", output);
            Assert.Equal(5, output.Split('\n').Count(l => l.StartsWith("ok ")));
            var img = ESharp.Assets.Png.Read(File.ReadAllBytes(png));
            Assert.Equal(64, img.Width);
            Assert.Equal(32, img.Height);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Rgba[..4]);                      // colour 1: 4-bit red
            var at = (6 * 64 + 11) * 4;
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, img.Rgba[at..(at + 4)]);             // colour 2: 24-bit green, rounded to 4 bits (3 planes)
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
