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
