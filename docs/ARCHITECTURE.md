# Architecture

How E# is put together, for people who want to change it. For using it, see the [README](../README.md) and the
[guide](GUIDE.md); for what works and what is verified, [STATUS.md](STATUS.md); for the design decisions and roadmap,
[PROJECT_CONTEXT.md](PROJECT_CONTEXT.md).

## The idea

E# is not a new language and not .NET on the Amiga. Your program is ordinary C# source. Roslyn parses it, an analyzer
checks that it stays inside a subset, an emitter writes C (with `#line` directives pointing back at your `.cs` files), and
a C compiler for the target turns that into a native executable. Nothing of the CLR, a JIT or a garbage collector runs on the
Amiga. The same `.cs` files also build with plain `dotnet`, against managed fakes of the platform layer, so game logic can be
run, unit tested and debugged on the PC.

```
                          +--> E# analyzer (ES0001...) --> errors in the IDE and in the build
  your .cs  --> Roslyn ---+
  + Runtime/Platform      +--> lowering + CEmitter --> C (#line to .cs) --> gcc for the target:
    sources                                                                 m68k, os4, morphos, aros, aros-i386, sdl
  and, for tests:
  your .cs + Runtime/Platform + Target.Host (managed fakes, HOST defined) --> dotnet build/run  --> ok/FAIL lines
```

## The projects

| Project | What it is |
|---|---|
| `src/ESharp.Analyzers` | The subset rules (`ES0001`-`ES0014`, `ES0020`..., `ES0098`-`ES0102`, in `Descriptors.cs`, `SubsetAnalyzer.cs`, `ClosureRules.cs`; `LibraryVersions.cs` for ES0101). One analyzer, used by the IDE (it stays on `netstandard2.0` so IDEs can load it) and by the compiler. A program class may derive only from its own classes or from a library class marked `[Extensible]`; the rule has to hold where the library is source (the compiler) and where it is metadata (the IDE). |
| `src/ESharp.Compiler` | `EsCompiler.Compile`: parse with Roslyn, report C# errors in user code, run the analyzer, check the entry point and the chip RAM budget, then `CEmitter` (split over `CEmitter*.cs`: closures, LINQ, patterns, inline asm, C names) writes C. `Monomorphizer.cs` instantiates generics. `Targets.cs` is the CPU x OS target model. `CRuntime/es_std.h` and `es_sdl.h` are C runtime pieces pasted into the C of the `sdl` target when a `[Native]` extern named `es_std_*` / `es_sdl_*` is called. |
| `src/ESharp.Runtime` | Attributes (`[EntryPoint]`, `[AmigaLib]`, `[ChipRam]`, ...) and `Fix`. Parsed together with user code. |
| `src/ESharp.Platform` | The platform layer: `Video`, `Input`, `Sound`, `Dos`, `Mem`, `Heap`, collections, `Gui`, BOOPSI, MUI, IFF, fonts, and the `[AmigaLib]` bindings. Parsed together with user code, with the symbols below choosing the implementation. `Layout.g.cs` holds struct offsets measured per target. |
| `src/ESharp.Target.Host` | Managed fakes for tests (`HostConsole`, `HostMem`, a fake chip RAM budget). Built with `HOST`. |
| `src/ESharp.Assets` | The PNG and IFF ILBM readers, PNG writer, colour reduction and dithering, planar packing, WAV and 8SVX reading, the `assets.json` to `Assets.g.cs` generator with an incremental `Sync` and the Workbench icon writers. |
| `src/ESharp.Cli` | The `esharp` command: `new`, `build`, `run`, `test`, `assets`, `addr`, `os4vm`. The FS-UAE, SDL, host and AmigaOS 4.1 VM harnesses live here, plus the project template (`ProjectTemplate.cs`). Packs the dotnet tool **AmigaESharp.Tool**. |
| `src/ESharp.Sdk` | Packaging only: **AmigaESharp.Sdk** (Runtime, Platform and Target.Host in `lib/net10.0`, the analyzer in `analyzers/dotnet/cs`). |
| `tests/ESharp.Compiler.Tests` | The unit tests (xUnit). |
| `samples/` | Programs that double as feature demos and tests (`ok NAME` / `FAIL NAME` lines). |
| `tools/` | BindGen, CardArt, AssetSamples, PackageIcon, the hardware pack, hosted AROS and OS4 VM scripts, the MUI fetch script. |
| `docker/` | The pinned toolchain images and their smoke tests: `Dockerfile` (m68k, os4, morphos, aros), `sdl`, `aros-hosted`, `aros-i386`. |

The Runtime and Platform sources are embedded in the compiler as source (`ESharp.Compiler.csproj`, `LibrarySources.cs`), so
the Amiga build needs no separate library. For the host, the same files are built by dotnet with `HOST` defined and shipped
as assemblies (`host/` next to `esharp.dll`, and in the Sdk package).

## The compile pipeline

`EsCompiler.Compile(sources, options)` does, in order:

1. **Parse** the user files and the embedded Runtime and Platform sources with Roslyn, with the target's preprocessor symbols.
2. **C# errors** in user code are reported as they are (a compilation that does not compile is not translated).
3. **Subset rules** run through `SubsetAnalyzer`: errors such as exceptions, LINQ beyond what is turned into loops, floating
   point without `--allow-float`, async, dynamic, reflection, structs holding references, 64-bit maths on a 68020, a library
   call newer than the target's libraries, and Amiga-only APIs on `sdl` (ES0102).
4. **Entry point and chip budget** checks (`[EntryPoint]`, `ChipBudget.cs`).
5. **Lowering and emission** (`CEmitter`): generics are monomorphised, classes become C structs with vtables, lambdas that
   capture become stack or reference-counted heap closures, LINQ over arrays becomes fused loops, patterns become tests, and
   `[AmigaLib]` calls become library calls per target (`jsr -N(a6)` on m68k, `proto/*.h` interface calls on the PowerPC and AROS
   targets). `#line` directives keep errors and crash addresses mappable to C# lines (`esharp addr`).
6. **C compiler**, normally inside a pinned Docker image (`--docker`), with the target's flags from `Targets.cs`.

**Emitter gaps are reported as `ES0099`, never miscompiled.** If the emitter cannot translate something it says so; prefer adding
the feature over silencing the diagnostic.

## One source, several builds

The same `.cs` files are parsed with different symbols:

| Build | Symbols | Platform implementation |
|---|---|---|
| Amiga targets | `AMIGA`, plus `M68K` or `PPC` or `X86_64` or `I386`, plus `AMIGAOS3`, `AMIGAOS4`, `MORPHOS` or `AROS` | `#if AMIGA` branches: library calls |
| `sdl` | `SDL` (not `AMIGA`) | `#elif SDL` branches: SDL 3 through the C runtime header |
| dotnet (tests, IDE) | `HOST` | the `#else` branches and `ESharp.Target.Host`: managed fakes |

Platform code is written as `#if AMIGA` / `#elif SDL` / `#else`. Amiga-only classes (Gui, Asl, Iff, Fonts, Joystick, BOOPSI, MUI and the raw
library bindings) are left out of the `sdl` build; using them is error ES0102. Nothing in the front end or in the platform
contracts may assume a single target: CPU and OS are separate axes in `Targets.cs`.

## Where to add things

**An analyzer rule.** Add a descriptor in `Descriptors.cs` and the check in `SubsetAnalyzer.cs` (or `ClosureRules.cs`), then a
test in `tests/ESharp.Compiler.Tests/SubsetAnalyzerTests.cs`. Remember the analyzer also runs in the IDE, where the platform
library is metadata, and on the library sources in the compiler.

**An emitter feature.** Add the lowering in the matching `CEmitter*.cs` file. A new feature needs a test that compiles the
generated C and runs it (see `CEmitterTests.cs`, `CEmitterPatternTests.cs`, `HeapClosureTests.cs`), not only a check of the C
text; a sample in `samples/` that prints `ok` lines is the usual way to also run it on the emulator. If part of a feature is
not done, the emitter must report `ES0099` for it.

**A platform API.** Add the class to `src/ESharp.Platform` with the Amiga, SDL and host branches (or leave the SDL and host
branches out and make it Amiga-only), keep the signatures identical across targets, and add a sample. Update
[STATUS.md](STATUS.md) and say what you ran it on.

### Adding a platform binding

Amiga library bindings are `static extern` methods tagged `[AmigaLib("lib", vector)]` with `[Since(version)]`. The vector numbers,
argument registers and library versions come from the library's `.fd` file and prototypes. `tools/BindGen` reads them from files
you provide (the official NDK is restrictively licensed: download it yourself and point the tool at your copy; the AROS
include directory is open and also works; never commit SDK text):

```bash
dotnet run --project tools/BindGen -- gen <fd dir> exec dos graphics --include <include dir> --out <dir>   # C# bindings (add --docs, --namespace N)
dotnet run --project tools/BindGen -- gen <fd dir> exec dos graphics --include <include dir> --check src/ESharp.Platform   # hand-written vs generated
dotnet run --project tools/BindGen -- check <sdk include dir> src/ESharp.Platform                          # vectors and argument counts against the headers
tools/BindGen/layouts.sh                                                                                    # re-measure struct offsets into Layout.g.cs (docker)
```

The generated classes are not committed; BindGen is used to write new declarations and to check the hand-written ones for
mistakes (a result type that is wrong, a `Since` that is off). Open a library that is newer than version 39 only as an
optional base (and with `[RequiresLib]`). The background and the licensing questions are in
[BINDINGS_SPIKE.md](BINDINGS_SPIKE.md); STATUS.md, "Binding generator", has the current state.

**A target.** Add it to `Targets.cs` (symbols, compiler, flags), the image in `docker/`, the `[AmigaLib]` call shape in the
emitter if the calling convention differs, and the run harness in `src/ESharp.Cli`. Do the PowerPC and AROS notes in STATUS.md
first: they list what differs.

**An asset format or icon.** `src/ESharp.Assets` has the readers and writers; `AssetFormatTests.cs`, `IconTests.cs` and
`NewIconsTests.cs` show how they are tested.

## How tests compare host and target runs

There are two levels.

**Unit tests** (`dotnet test`, `tests/ESharp.Compiler.Tests`). They compile E# source to C and check the diagnostics; many
then build the generated C with the host `gcc` (or `ESHARP_TEST_CC`) against a small shim of the Amiga libraries and run it, so
the emitted behaviour is tested and not only its text. Others run the samples under plain dotnet (`HostTargetTests`).
Tests that need Docker, the OS4 VM or an SDK skip when it is missing (two skip by default).

**`esharp test`** (the behaviour of one program on several machines). A program prints `ok NAME` and `FAIL NAME` lines (and
`skip NAME` for a check that cannot run on this machine). `esharp test <path>` always runs the program on the host with
`HOST` defined. With `--target m68k --docker` it also builds for m68k and runs it headless in FS-UAE, with `--target os4` it
boots the AmigaOS 4.1 VM, and with `--target sdl` it runs the SDL build headless. `TestReport` parses the `ok`/`FAIL` lines of both
runs and compares them by name: a check that is ok on one side and FAIL or missing on the other is a difference, a skipped one
is not, and the exit code is non-zero on any `FAIL`, crash, build error or difference. The same `--args` line goes to both
runs. Hosted AROS runs go through `tools/aros-hosted/aros-hosted.sh`, which prints the program's lines (`esharp test` does
not support `aros` or `morphos` yet).

This is why samples are written as lists of checks: the host run is the reference, and the target run has to agree with it.
CI runs the unit tests on Linux and Windows, a package round trip, the toolchain image smoke test and the `sdl` target
(`.github/workflows/ci.yml`); emulator and VM runs are done by hand and recorded in STATUS.md.

## Packaging

`dotnet pack ESharp.sln` makes **AmigaESharp.Tool** (the CLI, with the embedded sources, `host/`, `os4vm/` and `mui/` folders)
and **AmigaESharp.Sdk** at one version (`Directory.Build.props`). `esharp new` writes a csproj that references the Sdk at the
tool's version. Anything the CLI needs at run time must ship in the tool package, not be read from the repository layout.
`.github/workflows/release.yml` publishes on pushes to main; nobody publishes by hand (STATUS.md, "NuGet packages").
