using ESharp.Compiler;
using ESharp.Target.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// Requester.Show (issue #155): the Amiga build of the platform code run with host gcc against a fake EasyRequestArgs (the struct it
/// fills, the '%' escaping, the window it passes and the mapping of EasyRequest's 1, 2, ..., 0 to button indexes), the sdl build, and
/// the host fake. The emulator runs (Kickstart 3.1, hosted AROS, the OS4 VM) are recorded in docs/STATUS.md.
/// </summary>
public class RequesterTests
{
    private static CompileOptions Defines(params string[] d) => new() { Defines = d };

    private static readonly CompileOptions HostC = Defines("AMIGA", "X86_64", "AROS", "ESHARP_HOSTC");

    /// <summary>EasyRequestArgs prints the EasyStruct it gets (read at the X86_64 offsets) and answers with the next scripted result.</summary>
    private const string IntuitionShim = @"#pragma once
#include <stdio.h>
#include <stdint.h>
#include <string.h>
static int shim_answers[8] = { 1, 2, 0, 0, 5, 1 };
static int shim_calls;
static inline __attribute__((unused)) int32_t EasyRequestArgs(void *w, void *es, void *idcmp, void *args)
{
    unsigned char *e = es;
    printf(""[size %u flags %u window %s idcmp %s args %s title '%s' text '%s' gadgets '%s']\n"", *(uint32_t *)e, *(uint32_t *)(e + 4),
        w == NULL ? ""null"" : ""set"", idcmp == NULL ? ""null"" : ""set"", args == NULL ? ""null"" : ""set"",
        *(const char **)(e + 8), *(const char **)(e + 16), *(const char **)(e + 24));
    return shim_answers[shim_calls++];
}
";

    private static readonly System.Collections.Generic.Dictionary<string, string> Shims = new() { ["proto/intuition.h"] = IntuitionShim };

    [Fact]
    public void Amiga_build_fills_an_EasyStruct_escapes_percent_and_maps_the_answer_to_a_button_index()
    {
        var c = CompileOk(Program("", @"
Dos.PutStr($""{Requester.Show(""T"", ""Quit?\n100%"", ""Yes|Maybe|No"")}\n"");
Dos.PutStr($""{Requester.Show(""T"", ""a"", ""Yes|Maybe|No"")}\n"");
Dos.PutStr($""{Requester.Show(""T"", ""b"", ""Yes|Maybe|No"")}\n"");
Dos.PutStr($""{Requester.Show(""%s"", ""c"", """")}\n"");
Dos.PutStr($""{Requester.Show(""T"", ""d"", ""A|B"")}\n"");
Dos.PutStr($""{Requester.Show(""T"", ""e"", ""Go 50%|Stop"")}\n"");"), HostC);
        Assert.Contains("EasyRequestArgs(", c);
        if (!HasGcc) return;
        var (code, output) = RunWithHostGcc(c, Shims);
        Assert.True(code == 0, output);
        Assert.Equal(
            "[size 32 flags 0 window null idcmp null args null title 'T' text 'Quit?\n100%%' gadgets 'Yes|Maybe|No']\n0\n" +   // 1: the first button
            "[size 32 flags 0 window null idcmp null args null title 'T' text 'a' gadgets 'Yes|Maybe|No']\n1\n" +            // 2: the second
            "[size 32 flags 0 window null idcmp null args null title 'T' text 'b' gadgets 'Yes|Maybe|No']\n2\n" +            // 0: the rightmost
            "[size 32 flags 0 window null idcmp null args null title '%s' text 'c' gadgets 'OK']\n0\n" +                     // no buttons: one OK; the title is not a format
            "[size 32 flags 0 window null idcmp null args null title 'T' text 'd' gadgets 'A|B']\n1\n" +                     // 5: not a button, the last one
            "[size 32 flags 0 window null idcmp null args null title 'T' text 'e' gadgets 'Go 50%%|Stop']\n0\n",
            output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Sdl_build_calls_SDL_ShowMessageBox_through_the_C_runtime()
    {
        var c = CompileOk(Program("", "int a = Requester.Show(\"T\", \"Quit?\", \"Yes|No\");"), Targets.Sdl.ToOptions());
        Assert.Contains("es_sdl_message_box(", c);
        Assert.Contains("SDL_ShowMessageBox", c);
        Assert.Contains("SDL_MESSAGEBOX_BUTTONS_LEFT_TO_RIGHT", c);
    }

    [Fact]
    public void Requester_is_on_every_Amiga_target()
    {
        foreach (var t in Targets.All.Where(t => t.IsAmiga))
            Assert.Contains("EasyRequestArgs(", CompileOk(Program("", "int a = Requester.Show(\"T\", \"Quit?\", \"Yes|No\");"), t.ToOptions()));
    }

    [Fact]
    public void Host_fake_answers_the_preset_button_or_the_last_one()
    {
        int before = ESharp.Platform.Requester.HostShown;
        try
        {
            ESharp.Platform.Requester.HostSet(-1);
            Assert.Equal(2, ESharp.Platform.Requester.Show("T", "Quit?", "Yes|Maybe|No"));
            ESharp.Platform.Requester.HostSet(1);
            Assert.Equal(1, ESharp.Platform.Requester.Show("T", "Quit?", "Yes|Maybe|No"));
            Assert.Equal(0, ESharp.Platform.Requester.Show("T", "Quit?", ""));            // past the only button: the last one
            Assert.Equal(1, ESharp.Platform.Requester.Count(""));
            Assert.Equal(2, ESharp.Platform.Requester.Count("A|B"));
            Assert.Equal(8, ESharp.Platform.Requester.Count("1|2|3|4|5|6|7|8|9"));
            Assert.True(ESharp.Platform.Requester.HostShown >= before + 3);
        }
        finally { ESharp.Platform.Requester.HostSet(-1); }
    }

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "requester", "Requester.cs");

    [Fact]
    public void Requester_sample_builds_for_m68k_and_sdl()
    {
        var src = File.ReadAllText(SamplePath);
        foreach (var t in new[] { Targets.M68k, Targets.Sdl })
        {
            var r = EsCompiler.Compile(new[] { new SourceFile(SamplePath, src) }, t.ToOptions());
            Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        }
    }

    [Fact]
    public void Requester_sample_runs_on_dotnet_with_the_preset_answer()
    {
        var compilation = CSharpCompilation.Create("requester", new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(SamplePath)) }, PlatformSubclassTests.HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        ESharp.Platform.Args.HostSet("ANSWER=1");
        try
        {
            using var console = HostConsole.Capture();
            asm.GetType("RequesterSample.Program")!.GetMethod("Main")!.Invoke(null, null);
            Assert.Equal("ok button counts\nSHOT\nanswer 1\nok the chosen button\n", console.Output);
        }
        finally { ESharp.Platform.Args.HostReset(); ESharp.Platform.Requester.HostSet(-1); }
    }
}
