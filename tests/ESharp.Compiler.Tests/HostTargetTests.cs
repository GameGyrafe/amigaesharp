using ESharp.Target.Host;

namespace ESharp.Compiler.Tests;

/// <summary>Runs the hello sample under plain dotnet: the same source the Amiga build compiles.</summary>
public class HostTargetTests
{
    [Fact]
    public void Hello_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        Hello.Program.Main();
        Assert.Equal("Hello from E#!\n", console.Output);
    }

    [Fact]
    public void Phase2_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        Phase2.Program.Main();
        Assert.Equal(Phase2Expected.Output, console.Output);
    }

    [Fact]
    public void Generics_sample_runs_on_the_host_without_leaks()
    {
        using var mem = HostMem.Capture();
        using var console = HostConsole.Capture();
        GenericsSample.Program.Main();
        Assert.Equal(GenericsExpected.Output, console.Output);
        Assert.Equal(0, mem.LiveBlocks);
    }

    [Fact]
    public void Properties_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        PropertiesSample.Program.Main();
        Assert.Equal(PropertiesExpected.Output, console.Output);
    }

    [Fact]
    public void Language_sample_runs_on_the_host()
    {
        using var mem = HostMem.Capture();
        using var console = HostConsole.Capture();
        LanguageSample.Program.Main();
        Assert.Equal(LanguageExpected.Output, console.Output);
        Assert.Equal(0, mem.LiveBlocks);
    }

    [Fact]
    public void Sound_sample_runs_on_the_host_without_leaks()
    {
        using var mem = HostMem.Capture();
        using var console = HostConsole.Capture();
        ESharp.Platform.Sound.HostReset();
        SoundSample.Program.Main();
        Assert.Equal(SoundExpected.Output, console.Output);
        Assert.Equal(0, mem.LiveBlocks);
        Assert.Equal(4, ESharp.Platform.Sound.HostPlays);
        Assert.Equal(300, ESharp.Platform.Sound.HostLastPeriod);
        Assert.Equal(64, ESharp.Platform.Sound.HostLastVolume);
        ESharp.Platform.Sound.HostReset();
    }

    [Fact]
    public void Collections_sample_runs_on_the_host_without_leaks()
    {
        using var mem = HostMem.Capture();
        using var console = HostConsole.Capture();
        CollectionsSample.Program.Main();
        Assert.Equal(CollectionsExpected.Output, console.Output);
        Assert.Equal(0, mem.LiveBlocks);
    }

    [Fact]
    public void Aga_sample_runs_on_the_host_with_a_24_bit_palette()
    {
        using var console = HostConsole.Capture();
        ESharp.Platform.Video.HostAga = true;
        AgaSample.Program.Main();
        Assert.Equal(AgaExpected.Output, console.Output);
        Assert.Equal(0xF0F000u, ESharp.Platform.Video.HostPalette24[255]);   // restored to the image colour after the pen test
        Assert.Equal(256, ESharp.Platform.Video.HostPalette24.Length);
    }

    [Fact]
    public void Aga_sample_falls_back_on_a_machine_without_aga()
    {
        using var console = HostConsole.Capture();
        ESharp.Platform.Video.HostAga = false;
        try { AgaSample.Program.Main(); }
        finally { ESharp.Platform.Video.HostAga = true; }
        Assert.Equal("no AGA chipset\nok an 8-bitplane screen is refused without AGA\nok the 32-colour fallback opens\nok SetColor24 falls back to the 4-bit palette\n", console.Output);
    }

    [Fact]
    public void Inherit_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        InheritSample.Program.Main();
        Assert.Equal(InheritExpected.Output, console.Output);
    }

    [Fact]
    public void Lambdas_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        LambdasSample.Program.Main();
        Assert.Equal(LambdasExpected.Output, console.Output);
    }

    [Fact]
    public void Refs_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        RefsSample.Program.Main();
        Assert.Equal(RefsExpected.Output, console.Output);
    }

    [Fact]
    public void Linq_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        LinqSample.Program.Main();
        Assert.Equal(LinqExpected.Output, console.Output);
    }

    [Fact]
    public void Controls_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        ESharp.Platform.Joystick.HostSet(1, ESharp.Platform.Joystick.KindNone, 0);
        ControlsSample.Program.Main();
        Assert.Equal("ok the clock reads 0 before Open\nok timer.device opens\nclock 1000000 ticks per second\nok the clock has a frequency\n"
            + "ok ticks convert to time\nok the clock moves\nok Delay(10) measures about 200 ms\nok the clock closes\n"
            + "lowlevel.library present\nport 1 kind 0 bits 0\nok port 1 set to a CD32 pad reads as a pad\n"
            + "ok port 1 can be set to a joystick\nok port 1 then reads as a joystick\nok a direction is -1, 0 or 1\nok lowlevel closes\n", console.Output);
    }

    [Fact]
    public void Dates_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        ESharp.Platform.Date.HostSetNow(0);
        DatesSample.Program.Main();
        Assert.DoesNotContain("FAIL", console.Output);
        Assert.Contains("2000-02-29 12:34:56 is second 699366896\n", console.Output);
        Assert.Contains("now 1978-1-1 0:0:0\n", console.Output);
        Assert.Equal(19, console.Output.Split('\n').Count(l => l.StartsWith("ok ")));
    }

    [Fact]
    public void Fonts_sample_runs_on_the_host_with_and_without_diskfont()
    {
        ESharp.Platform.Fonts.HostAdd("ruby", 12, 8, 10);
        try
        {
            using var console = HostConsole.Capture();
            FontsSample.Program.Main();
            Assert.DoesNotContain("FAIL", console.Output);
            Assert.Contains("ruby 12: height 12, width 8, baseline 10\n", console.Output);
            Assert.Contains("ok text was drawn in the disk font\n", console.Output);
            Assert.Equal(0, ESharp.Platform.Fonts.HostOpenCount);
            ESharp.Platform.Fonts.HostDiskFonts = false;
            using var console2 = HostConsole.Capture();
            FontsSample.Program.Main();
            Assert.DoesNotContain("FAIL", console2.Output);
            Assert.Contains("diskfont.library is not installed: disk fonts skipped\n", console2.Output);
            Assert.DoesNotContain("ruby 12", console2.Output);
            Assert.Contains("ok topaz 8 opens\n", console2.Output);
            Assert.Equal(0, ESharp.Platform.Fonts.HostOpenCount);
        }
        finally { ESharp.Platform.Fonts.HostDiskFonts = true; }
    }

    [Fact]
    public void Fonts_open_takes_the_exact_size_or_with_diskfont_the_closest()
    {
        try
        {
            ESharp.Platform.Fonts.HostDiskFonts = true;
            var f = ESharp.Platform.Fonts.Open("topaz", 11);       // not exact: with diskfont the closest size
            Assert.True(f.IsOpen); Assert.Equal(9, f.Height);
            ESharp.Platform.Fonts.Close(ref f);
            ESharp.Platform.Fonts.HostAdd("topaz", 11, 8, 9);      // now FONTS:topaz/11 exists and wins over the in-memory sizes
            f = ESharp.Platform.Fonts.Open("topaz", 11);
            Assert.Equal(11, f.Height);
            ESharp.Platform.Fonts.Close(ref f);
            ESharp.Platform.Fonts.HostDiskFonts = false;
            f = ESharp.Platform.Fonts.Open("topaz", 10);            // no diskfont: exact sizes only
            Assert.False(f.IsOpen);
            f = ESharp.Platform.Fonts.Open("topaz", 9);
            Assert.True(f.IsOpen); Assert.Equal(9, f.Height);
            ESharp.Platform.Fonts.Close(ref f);
        }
        finally { ESharp.Platform.Fonts.HostDiskFonts = true; }
    }

    [Fact]
    public void Iff_sample_runs_on_the_host_and_writes_a_valid_iff_file()
    {
        using var console = HostConsole.Capture();
        IffSample.Program.Main();
        Assert.DoesNotContain("FAIL", console.Output);
        Assert.Equal(10, console.Output.Split('\n').Count(l => l.StartsWith("ok ")));
    }

    [Fact]
    public void Iff_file_has_the_standard_layout_and_a_missing_library_gives_a_closed_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "esharp_iff_layout_" + Environment.ProcessId + ".iff");
        try
        {
            var w = ESharp.Platform.Iff.Create(path, ESharp.Platform.Iff.Id("ILBM"));
            unsafe
            {
                byte* three = stackalloc byte[3];
                three[0] = 7; three[1] = 8; three[2] = 9;
                Assert.True(w.WriteChunk(ESharp.Platform.Iff.Id("ABCD"), three, 3));
            }
            Assert.True(w.Finish());
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("FORM", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(new byte[] { 0, 0, 0, 16 }, bytes[4..8]);                 // ILBM + chunk header (8) + 3 bytes + pad
            Assert.Equal("ILBMABCD", System.Text.Encoding.ASCII.GetString(bytes, 8, 8));
            Assert.Equal(new byte[] { 0, 0, 0, 3, 7, 8, 9, 0 }, bytes[16..24]);
            ESharp.Platform.Iff.HostAvailable = false;
            Assert.False(ESharp.Platform.Iff.Open());
            Assert.False(ESharp.Platform.Iff.Create(path, 0).IsOpen);
            Assert.False(ESharp.Platform.Iff.OpenRead(path).IsOpen);
            using var console = HostConsole.Capture();
            IffSample.Program.Main();
            Assert.Equal("iffparse.library is not installed: nothing to do\n", console.Output);
        }
        finally { ESharp.Platform.Iff.HostAvailable = true; File.Delete(path); }
    }

    [Fact]
    public void Asl_sample_runs_on_the_host_and_reports_the_pick_or_a_missing_library()
    {
        using var console = HostConsole.Capture();
        AslSample.Program.Main();
        Assert.DoesNotContain("FAIL", console.Output);
        Assert.Contains("result: picked RAM:picked.txt\n", console.Output);
        Assert.Equal("E# file requester", ESharp.Platform.Asl.HostLastTitle);
        Assert.Equal("RAM:", ESharp.Platform.Asl.HostLastDrawer);
        ESharp.Platform.Asl.HostAvailable = false;
        try
        {
            using var console2 = HostConsole.Capture();
            AslSample.Program.Main();
            Assert.Equal("asl.library is not installed: nothing to do\n", console2.Output);
            Assert.Null(ESharp.Platform.Asl.PickFile("x", "RAM:"));
        }
        finally { ESharp.Platform.Asl.HostAvailable = true; }
    }

    [Fact]
    public void Names_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        NamesSample.Program.Main();
        Assert.DoesNotContain("FAIL", console.Output);
        Assert.Equal(12, console.Output.Split('\n').Count(l => l.StartsWith("ok ")));
    }

    [Fact]
    public void Keys_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        KeysSample.Program.Main();
        Assert.Equal("ok keymap.library opens\nkey 0x20 types 97, with shift 65\nok a letter key types a letter, upper case with shift\n"
            + "ok Caps Lock types upper case\nok space, return, escape and backspace\nok cursor up types no single character\n"
            + "ok a key release types nothing\nok Find goes back from the letter to its key\nok Find gives the upper case letter with shift\n"
            + "keymap: qwerty\nSHOT\nno key pressed\n", console.Output);
    }

    [Fact]
    public void A_pressed_key_gives_its_character_and_modifiers_until_the_next_poll()
    {
        ESharp.Platform.Input.HostKey(0x20, ESharp.Platform.Keys.LeftShift);
        ESharp.Platform.Input.Poll();
        Assert.Equal(0x20, ESharp.Platform.Input.LastKey);
        Assert.Equal('A', ESharp.Platform.Input.LastChar);
        Assert.Equal(ESharp.Platform.Keys.LeftShift, ESharp.Platform.Input.LastQualifier);
        ESharp.Platform.Input.HostKey(0x01);
        ESharp.Platform.Input.Poll();
        Assert.Equal('1', ESharp.Platform.Input.LastChar);
        Assert.Equal(0, ESharp.Platform.Input.LastQualifier);
        ESharp.Platform.Input.Poll();
        Assert.Equal(0, ESharp.Platform.Input.LastChar);
        Assert.Equal((0x10, 0), ESharp.Platform.Keys.Find('q'));
        Assert.Equal((0x01, ESharp.Platform.Keys.LeftShift), ESharp.Platform.Keys.Find('!'));
        Assert.Equal(17, ESharp.Platform.Keys.Char(0x10, ESharp.Platform.Keys.Control));   // Ctrl-Q
    }

    [Fact]
    public void Joystick_reads_directions_and_buttons_only_from_sticks_and_pads()
    {
        ESharp.Platform.Joystick.Open();
        try
        {
            ESharp.Platform.Joystick.HostSet(1, ESharp.Platform.Joystick.KindGamepad, ESharp.Platform.Joystick.Up | ESharp.Platform.Joystick.Left | ESharp.Platform.Joystick.Fire);
            Assert.Equal((-1, -1), ESharp.Platform.Joystick.Direction(1));
            Assert.NotEqual(0u, ESharp.Platform.Joystick.Read(1) & ESharp.Platform.Joystick.Fire);
            ESharp.Platform.Joystick.HostSet(1, ESharp.Platform.Joystick.KindJoystick, ESharp.Platform.Joystick.Down | ESharp.Platform.Joystick.Right);
            Assert.Equal((1, 1), ESharp.Platform.Joystick.Direction(1));
            ESharp.Platform.Joystick.HostSet(0, ESharp.Platform.Joystick.KindMouse, 0x1234);
            Assert.Equal(0u, ESharp.Platform.Joystick.Read(0));                    // a mouse has counters, not directions
        }
        finally
        {
            ESharp.Platform.Joystick.HostSet(0, ESharp.Platform.Joystick.KindNone, 0);
            ESharp.Platform.Joystick.HostSet(1, ESharp.Platform.Joystick.KindNone, 0);
            ESharp.Platform.Joystick.Close();
        }
    }

    [Fact]
    public void Rtg_sample_opens_an_rtg_screen_on_the_host_and_falls_back_without_a_card()
    {
        using (var console = HostConsole.Capture())
        {
            ESharp.Platform.Video.HostRtg = true;
            RtgSample.Program.Main();
            Assert.Equal("RTG present\nok an RTG screen opens\nok the screen is reported as RTG\nok pen 200 draws on the card\nok closed screen is not RTG\n"
                + "ok the native screen opens\nok the native screen is not RTG\nok native drawing works\n", console.Output);
        }
        using (var console = HostConsole.Capture())
        {
            ESharp.Platform.Video.HostRtg = false;
            try { RtgSample.Program.Main(); }
            finally { ESharp.Platform.Video.HostRtg = true; }
            Assert.Equal("no RTG\nok an RTG screen is refused without a card\nok no screen is not RTG\nok the native screen opens\n"
                + "ok the native screen is not RTG\nok native drawing works\n", console.Output);
        }
    }

    [Fact]
    public void Operators_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        OperatorsSample.Program.Main();
        Assert.Equal(OperatorsExpected.Output, console.Output);
    }

    [Fact]
    public void Screen_sample_runs_on_the_host_framebuffer()
    {
        using var console = HostConsole.Capture();
        ScreenSample.Program.Main();
        Assert.Equal(ScreenExpected.Output, console.Output);
        // The card was drawn from the planar data: its centre pixel is not the background.
        Assert.False(ESharp.Platform.Video.IsOpen());
        Assert.Equal(320 * 200, ESharp.Platform.Video.HostPixels.Length);
        Assert.Equal(11, ESharp.Platform.Video.HostPixels[2 * 320 + 2]);
    }

    [Fact]
    public void A_scaled_masked_blit_is_the_normal_blit_with_every_pixel_drawn_as_a_square()
    {
        // A 16 x 2 two-plane image: plane 0 = 0xF0F0 / 0x0FF0, plane 1 = 0xFF00 / 0xFF00; the mask hides the last pixel of row 0.
        byte[] planar = { 0xF0, 0xF0, 0x0F, 0xF0, 0xFF, 0x00, 0xFF, 0x00 };
        byte[] mask = { 0xFF, 0xFE, 0xFF, 0xFF };
        Assert.True(ESharp.Platform.Video.Open(64, 16, 2));
        try
        {
            ESharp.Platform.Video.FillRect(0, 0, 64, 16, 3);
            ESharp.Platform.Video.BlitMaskedScaled(planar, mask, 16, 2, 2, 0, 0, 16, 2, 0, 0, 1);
            var plain = (byte[])ESharp.Platform.Video.HostPixels.Clone();
            ESharp.Platform.Video.FillRect(0, 0, 64, 16, 3);
            ESharp.Platform.Video.BlitMaskedScaled(planar, mask, 16, 2, 2, 0, 0, 16, 2, 4, 3, 3);
            var scaled = ESharp.Platform.Video.HostPixels;
            int wrong = 0;
            for (int y = 0; y < 2 * 3; y++)
                for (int x = 0; x < 16 * 3; x++)
                    if (scaled[(3 + y) * 64 + 4 + x] != plain[(y / 3) * 64 + x / 3]) wrong++;
            Assert.Equal(0, wrong);
            Assert.Equal(3, plain[15]);                 // the masked pixel shows the background
            Assert.Equal(3, scaled[3 * 64 + 4 + 45]);   // ... and so does its 3 x 3 block
        }
        finally { ESharp.Platform.Video.Close(); }
    }

    [Fact]
    public void Cards_sample_runs_on_the_host_and_draws_masked_sprites()
    {
        using var console = HostConsole.Capture();
        CardsDemo.Program.Main();
        Assert.Equal(CardsExpected.Output, console.Output);
        Assert.False(ESharp.Platform.Video.IsOpen());
        Assert.Equal(8, ESharp.Platform.Video.HostPixels[0]);   // the table colour at the top left corner
    }

    [Fact]
    public void Rummy_sample_runs_on_the_host()
    {
        using var console = HostConsole.Capture();
        RummyDemo.Program.Main();
        Assert.Equal(RummyExpected.Output, console.Output);
        Assert.False(ESharp.Platform.Video.IsOpen());
    }

    [Fact]
    public void Memory_sample_runs_on_the_host_without_leaks()
    {
        using var mem = HostMem.Capture();
        using var console = HostConsole.Capture();
        MemorySample.Program.Main();
        Assert.Equal(MemoryExpected.Output, console.Output);
        Assert.Equal(0, mem.LiveBlocks);
        Assert.Equal(0u, mem.ChipUsed);
    }

    [Fact]
    public void Fix_multiply_matches_a_64_bit_reference()
    {
        var rng = new Random(1234);
        var edges = new[] { 0, 1, -1, 0xFFFF, 0x10000, -0x10000, 0x7FFF0000, int.MinValue, int.MaxValue, 0x8000, -0x8000 };
        var values = edges.Concat(Enumerable.Range(0, 400).Select(_ => rng.Next(int.MinValue, int.MaxValue))).ToArray();
        foreach (var a in values)
        foreach (var b in values.Take(120))
        {
            var expected = unchecked((int)(((long)a * b) >> 16));
            var actual = (ESharp.Fix.FromRaw(a) * ESharp.Fix.FromRaw(b)).Raw;
            Assert.True(expected == actual, $"{a} * {b}: expected {expected}, got {actual}");
        }
    }
}

/// <summary>What samples/phase2 prints: one "ok" line per check. Shared by the host, C and Amiga runs.</summary>
internal static class Phase2Expected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "static array length", "array converts to ReadOnlySpan", "fill and foreach over a static array",
        "span to ReadOnlySpan", "Slice(start, length)", "Slice(start)", "writes through a slice", "sieve",
        "fixed buffer in a struct", "3 * 0.5 = 1.5", "integer and fraction parts", "3 / 0.5 = 6",
        "-2 * 1.5 = -3", "negation", "ToInt rounds towards negative infinity", "comparisons",
        "compound assignment", "multiply by int", "divide by int", "2.5 * 2.5 = 6.25",
        "smallest step keeps its sign", "-1.5 * -2.5 = 3.75", "1.5 * -2.5 = -3.75", "100.25 * -200.5",
        "constructor sets fields", "instance method calling another", "method mutates its receiver",
        "method on an array element", "constructor with early return", "constructor overloads",
        "method on a by-value parameter", "inline array in an instance method",
    };
}

/// <summary>What samples/aga prints on a machine with AGA.</summary>
internal static class AgaExpected
{
    public static string Output => "AGA chipset\n" + string.Concat(Names.Select(n => "ok " + n + "\n")) + "SHOT\nok screen closes\n";

    private static readonly string[] Names =
    {
        "an 8-bitplane screen opens", "the asset has 256 colours", "the blitted pixels have the 24-bit colours of the image",
        "pen 255 draws", "the chart is drawn",
    };
}

/// <summary>What samples/inherit prints.</summary>
internal static class InheritExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "derived fields and inherited fields",
        "base constructor runs first, then the derived body",
        "this(...) chains to a constructor that calls base(...)",
        "two levels of inheritance",
        "constructors run base to derived",
        "virtual call through the base type",
        "another override",
        "a base method calls virtual methods of the derived object",
        "virtual property",
        "virtual with an argument, inherited and overridden",
        "base.Method() calls",
        "is: tests along the chain",
        "is on null",
        "as",
        "is with a declaration",
        "is not",
        "explicit downcast",
        "interface on a derived class",
        "interface implemented by a base class, used on a derived one",
        "implicit derived constructor runs the base constructor",
        "a base object still uses its own method",
        "a class outside any hierarchy",
        "a base reference shares the object",
        "Heap.Free through a base variable",
    };
}

/// <summary>What samples/collections prints.</summary>
internal static class CollectionsExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "empty dictionary", "adds grow the table", "lookups after growth", "Add refuses a duplicate", "indexer replaces",
        "remove", "removed slots are reused", "enumeration visits every entry once", "clear", "destroy", "enum keys",
        "struct keys", "hash set", "hash set remove and enumerate", "string map counts words", "string map lookup",
        "string map remove", "string map keys", "string set", "all memory is given back",
    };
}

/// <summary>What samples/sound prints.</summary>
internal static class SoundExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "WAV 8 bit mono", "WAV 16 bit stereo", "8SVX", "8SVX Fibonacci delta", "other data is refused",
        "files are written and loaded back", "an unknown or missing file gives an empty sample", "the file is deleted", "period for a sample rate",
        "period for a frequency", "samples are allocated", "sample contents", "audio.device opens and gives a channel",
        "a playing voice is busy", "the voice is free when the sample has finished", "a looping sample keeps playing",
        "two voices play together", "Stop silences every voice", "a voice can be reused after Stop",
        "Close releases the channels", "samples are freed",
    };
}

/// <summary>What samples/memory prints.</summary>
internal static class MemoryExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "chip allocation succeeds", "chip memory is zeroed", "chip memory keeps what is written",
        "allocation reduces free chip RAM", "chip block is in the chip address range", "free returns the chip RAM",
        "fast allocation works and is zeroed", "zero-size allocation returns null", "arena reports its size",
        "arena aligns allocations", "arena tracks what is left", "arena returns null when full",
        "typed allocation with sizeof", "typed arena memory is usable and zeroed", "span over arena memory",
        "reset reuses the arena", "destroy releases the arena", "an impossible arena fails cleanly", "chip arena",
    };
}

/// <summary>What samples/generics prints.</summary>
internal static class GenericsExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "generic struct with int",
        "generic struct method",
        "generic struct with Fix",
        "generic struct with a user struct",
        "nested instantiation",
        "generic method, inferred and explicit",
        "generic method with Fix",
        "generic method with only an explicit argument",
        "generic struct instance method mutates",
        "generic method over Span",
        "pool created",
        "pool hands out distinct items",
        "pool items are writable",
        "pool free",
        "pool ignores a double free",
        "pool reuses a freed slot, zeroed",
        "pool returns null when full",
        "pool is full",
        "pool rejects a foreign pointer",
        "pool rejects a misaligned pointer",
        "pool reset",
        "pool destroy returns the memory",
        "pool of int",
        "impossible pool fails cleanly",
    };
}

/// <summary>What samples/properties prints.</summary>
internal static class PropertiesExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "auto properties, get-only set in the constructor",
        "property getter with a body, expression-bodied getter",
        "setter with a body clamps",
        "setter clamps low, Alive follows",
        "-- on a property inside a method",
        "compound assignment on properties",
        "++ and -= on an auto-property",
        "++ on a property with a setter",
        "computed property",
        "this.Property and unqualified property in a method",
        "property of a by-value parameter",
        "properties through a pointer",
        "properties on array elements",
        "properties in a generic struct",
        "compound assignment with a user-defined operator",
        "static properties",
        "static property with accessors, compound",
        "static property of a struct type",
    };
}

/// <summary>What samples/operators prints.</summary>
internal static class OperatorsExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "implicit conversion in an initializer",
        "implicit conversion in an assignment",
        "implicit conversion of an argument",
        "implicit conversion of a return value",
        "implicit conversion of an operand",
        "explicit conversion still works",
        "Fix from int in an initializer",
        "Fix plus int",
        "Fix compared with int",
        "Fix argument from int",
        "Fix times int is still the int overload",
        "indexer get and set",
        "indexer setter runs its body",
        "compound assignment and ++ on an indexer",
        "indexer with an expression index",
        "this[...] inside a method",
        "two-index indexer, expression-bodied",
        "indexer on an array element",
        "indexer in a generic struct",
        "generic indexer with an implicit conversion",
    };
}

/// <summary>What samples/screen prints.</summary>
internal static class ScreenExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n")) + "SHOT\nok screen closes\n";

    private static readonly string[] Names =
    {
        "screen opens",
        "background pixel",
        "red rectangle",
        "green rectangle on top of red",
        "yellow rectangle",
        "rectangle edges are exact",
        "outside the screen",
        "asset constants",
        "blitted card matches the planar data pixel for pixel",
        "card centre is not background",
        "blit does not spill outside the card",
        "font: top row of A",
        "font: crossbar of A",
        "font: scale 2 doubles pixels",
        "font: strings, newline and width",
        "font: numbers",
    };
}

/// <summary>What samples/cards prints on the host (the Amiga run has no scripted input checks).</summary>
internal static class CardsExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n")) + "SHOT\nok screen closes\n";

    private static readonly string[] Names =
    {
        "deal: 52 distinct cards",
        "rank and suit",
        "same seed gives the same shuffle",
        "other seed shuffles again",
        "deck with eight suits",
        "streak matches an independent count",
        "game ends on a miss",
        "no guesses after the end",
        "screen opens",
        "buttons are drawn",
        "button edge is exact",
        "masked card matches the sheet pixel for pixel",
        "masked corner shows the table",
        "card face is drawn",
        "click outside the buttons does nothing",
        "click on Higher guesses higher",
        "click on Lower guesses lower",
        "Esc quits",
        "nothing queued: nothing happens",
    };
}

/// <summary>What samples/rummy prints on the host (the Amiga run has no scripted input checks).</summary>
internal static class RummyExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n")) + "SHOT\nok screen closes\n";

    private static readonly string[] Names =
    {
        "card points", "deadwood leaves only the king", "melds are numbered per group", "run of three",
        "two ranks of a run plus a stray card are no meld", "ace is low only", "a set needs different suits",
        "best discard is the king", "no melds: every point counts", "counting with lambdas",
        "deal: 7 + 7 + pile + stock", "deal: 52 distinct cards", "player moves first", "same seed deals the same hand",
        "cannot discard or knock before drawing", "draw from the stock", "no second draw",
        "discard hands the move to the bot", "the bot draws and discards", "draw from the pile",
        "bot-against-bot games all end cleanly", "both sides win some games", "games are not endless",
        "screen opens", "table is drawn", "stock and pile are drawn", "the hand is drawn", "no eighth card yet",
        "click on the table does nothing", "click on the stock draws", "eighth card and knock button appear",
        "click on a card discards it", "bot replies", "Esc quits", "nothing queued: nothing happens",
    };
}

/// <summary>What samples/language prints.</summary>
internal static class LanguageExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "out declaration", "ref swap", "in parameter", "named tuple elements", "deconstruction", "tuple swap",
        "switch expression", "property patterns", "is patterns", "local function", "static constructor",
        "this initializer", "list add insert remove", "list as span", "lambda as a predicate", "list destroy",
        "heap objects linked by reference", "an object is shared, not copied", "Heap.Free clears the variable",
        "interface constraint with a struct and a class", "interface value calls the class", "the interface value shares the object",
        "null interface value", "Heap.Free on an interface value",
        "interpolated string", "concatenation and escaped braces",
        "lambdas with two parameters", "method group and delegate variable",
    };
}

/// <summary>What samples/lambdas prints.</summary>
internal static class LambdasExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "lambda captures a local",
        "a lambda writes to a captured local",
        "a captured local is shared, not copied",
        "two captured locals",
        "a user delegate type with a capture",
        "a static method as a user delegate",
        "capture as a span filter",
        "same code, new captured value",
        "a lambda passed through another method",
        "a method group",
        "a lambda inside a lambda captures through both",
        "closures created inside a loop",
        "null delegate",
        "assigned delegate",
        "a lambda that uses this",
        "capture of a captured variable",
    };
}

/// <summary>What samples/refs prints.</summary>
internal static class RefsExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "ref local writes through",
        "ref local pointed at another variable",
        "ref to an array element",
        "assignment to a ref return (array element)",
        "ref to the largest element",
        "ref return into a struct field",
        "ref return of a static struct",
        "ref local to a struct in an array",
        "ref return passed on as a ref argument",
        "ref readonly local sees later writes",
        "foreach by ref over a span",
        "foreach over a Vec",
        "continue and break in foreach over a Vec",
        "user struct enumerator",
        "enumerable returned by a call",
        "class handing out a Vec enumerator",
        "nested foreach with break in the inner loop",
        "foreach over an empty Vec",
    };
}

/// <summary>What samples/linq prints.</summary>
internal static class LinqExpected
{
    public static string Output => string.Concat(Names.Select(n => "ok " + n + "\n"));

    private static readonly string[] Names =
    {
        "Count",
        "Sum",
        "Min and Max",
        "Any",
        "All",
        "First and Last",
        "FirstOrDefault, no match",
        "Contains",
        "Where then Sum",
        "method groups",
        "Select then Where",
        "three steps",
        "Take and Skip",
        "Where with an index",
        "Select with an index",
        "Aggregate",
        "captures nothing: the lambda sees the locals",
        "the lambda reads the current value",
        "Enumerable.Range",
        "Range with steps",
        "foreach over a chain",
        "foreach over Range with continue and Take",
        "structs",
        "struct projection",
        "query: where and select",
        "query: let",
        "foreach over a query",
        "query over structs",
        "a chain inside a lambda",
        "OrderBy",
        "OrderByDescending",
        "a sort then First, Skip and Take",
        "OrderBy then ThenByDescending on structs",
        "the sort is stable",
        "OrderBy over Range with two keys",
        "query: orderby",
        "Where, OrderBy and Select",
    };
}
