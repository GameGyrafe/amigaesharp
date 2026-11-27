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
| `src/ESharp.Analyzers` | The subset rules (`ES0001`-`ES0015`, `ES0020`..., `ES0098`-`ES0103`, ES0117 where a package is metadata, in `Descriptors.cs`, `SubsetAnalyzer.cs`, `ClosureRules.cs`; `LibraryVersions.cs` for ES0101). One analyzer, used by the IDE (it stays on `netstandard2.0` so IDEs can load it) and by the compiler. A program class may derive only from its own classes or from a library or package class marked `[Extensible]`; the rule has to hold where the library or package is source (the compiler) and where it is metadata (the IDE). |
| `src/ESharp.Compiler` | `EsCompiler.Compile`: parse with Roslyn, report C# errors in user code, run the analyzer, check the entry point and the chip RAM budget, then `CEmitter` (split over `CEmitter*.cs`: closures, LINQ, patterns, inline asm, C names) writes C. `Monomorphizer.cs` instantiates generics. `Targets.cs` is the CPU x OS target model. `CRuntime/es_std.h` and `es_sdl.h` are C runtime pieces pasted into the C of the `sdl` target when a `[Native]` extern named `es_std_*` / `es_sdl_*` is called. |
| `src/ESharp.Runtime` | Attributes (`[EntryPoint]`, `[AmigaLib]`, `[ChipRam]`, ...) and `Fix`. Parsed together with user code. |
| `src/ESharp.Platform` | The platform layer: `Video`, `Input`, `Sound`, `Dos`, `Mem`, `Heap`, collections, `Gui`, BOOPSI, MUI, IFF, fonts, and the `[AmigaLib]` bindings. Parsed together with user code, with the symbols below choosing the implementation. `Layout.g.cs` holds struct offsets measured per target. |
| `src/ESharp.Target.Host` | Managed fakes for tests (`HostConsole`, `HostMem`, a fake chip RAM budget). Built with `HOST`. |
| `src/ESharp.Assets` | The PNG and IFF ILBM readers, PNG writer, colour reduction and dithering, planar packing, WAV and 8SVX reading, the `assets.json` to `Assets.g.cs` generator with an incremental `Sync` and the Workbench icon writers. |
| `src/ESharp.Cli` | The `esharp` command: `new`, `build` (also of an Amiga shared library, `AmigaLibraryStep.cs`), `run`, `test`, `assets`, `addr`, `os4vm`, `doctor` (`Doctor.cs`), `precompile` and `verify-package` (`Precompile.cs`). Where a local compiler comes from (`--toolchain`, `ESHARP_TOOLCHAIN_*`, PATH; version check) is `Toolchain.cs`; Docker builds pass the same command line. The FS-UAE, SDL, host and AmigaOS 4.1 VM harnesses live here, plus the project template (`ProjectTemplate.cs`). Packs the dotnet tool **AmigaESharp.Tool**. |
| `src/ESharp.Sdk` | Packaging only: **AmigaESharp.Sdk** (Runtime, Platform and Target.Host in `lib/net10.0`, the analyzer in `analyzers/dotnet/cs`, and `buildTransitive/AmigaESharp.Sdk.targets`, the step that makes a class library with `EsLibrary=true` an E# library). |
| `tests/ESharp.Compiler.Tests` | The unit tests (xUnit). |
| `samples/` | Programs that double as feature demos and tests (`ok NAME` / `FAIL NAME` lines). |
| `tools/` | BindGen, CardArt, AssetSamples, PackageIcon, the hardware pack, hosted AROS and OS4 VM scripts, the MUI fetch script. |
| `docker/` | The pinned toolchain images and their smoke tests: `Dockerfile` (m68k, os4, morphos, aros), `sdl`, `aros-hosted`, `aros-i386`. |

The Runtime and Platform sources are embedded in the compiler as source (`ESharp.Compiler.csproj`, `LibrarySources.cs`), so
the Amiga build needs no separate library. For the host, the same files are built by dotnet with `HOST` defined and shipped
as assemblies (`host/` next to `esharp.dll`, and in the Sdk package).

## The compile pipeline

`EsCompiler.Compile(sources, options)` does, in order:

1. **Parse** the user files, the sources of the E# library packages (`CompileOptions.Packages`, under `<package Id Version>/`
   paths) and the embedded Runtime and Platform sources with Roslyn, with the target's preprocessor symbols. Package files
   count as program files from here on: the same C# errors, subset rules and emission (all of their code is emitted, or declared
   only when the package's precompiled binary for the target is linked: "Precompiled binaries" below).
2. **C# errors** in user code are reported as they are (a compilation that does not compile is not translated).
3. **Subset rules** run through `SubsetAnalyzer`: errors such as exceptions, LINQ beyond what is turned into loops, floating
   point without `--allow-float`, async, dynamic, reflection, structs holding references, 64-bit maths on a 68020, a library
   call newer than the target's libraries, and Amiga-only APIs on `sdl` (ES0102).
4. **Entry point, package and chip budget** checks (`[EntryPoint]`, `PackageChecks.cs`, `ChipBudget.cs`).
5. **Lowering and emission** (`CEmitter`): generics are monomorphised, classes become C structs with vtables, lambdas that
   capture become stack or reference-counted heap closures, LINQ over arrays becomes fused loops, patterns become tests, and
   `[AmigaLib]` calls become calls by name with `proto/<lib>.h` included, on every target (the m68k register call comes from
   the toolchain's inline headers). `#line` directives keep errors and crash addresses mappable to C# lines (`esharp addr`).
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

Platform code is written as `#if AMIGA` / `#elif SDL` / `#else`. Amiga-only classes (Gui, Asl, Fonts, BOOPSI, MUI and the raw
library bindings such as LowLevel) are left out of the `sdl` build; using them is error ES0102. Nothing in the front end or in the platform
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
boots the AmigaOS 4.1 VM, with `--target aros` or `aros-i386` it boots hosted AROS in the `esharp-aros-hosted` image
(`ArosHosted.cs`, which carries its own run script so the installed tool needs no checkout), and with `--target sdl` it runs the
SDL build headless. `TestReport` parses the `ok`/`FAIL` lines of both runs and compares them by name: a check that is ok on one
side and FAIL or missing on the other is a difference, a skipped one is not. `TestReport.Judge` adds the target program's return
code (each runner records the shell's `RC` after the program: an `RC` file on m68k and AROS, a marker line on the os4 debug
port) and, on AROS, the `Trap` lines of the debug log. The exit code is non-zero on any `FAIL`, crash, trap, build error,
difference, or a non-zero target return code when the host returned 0. The same `--args` line goes to both runs. morphos has
no runner; `tools/aros-hosted/aros-hosted.sh` remains for AROS screenshots, input and Wanderer runs.

This is why samples are written as lists of checks: the host run is the reference, and the target run has to agree with it.
CI runs the unit tests on Linux and Windows, a package round trip, the toolchain image smoke test and the `sdl` target
(`.github/workflows/ci.yml`); emulator and VM runs are done by hand and recorded in STATUS.md.

## Library packages

An E# library (issue #31) is a .NET class library whose DLL carries its C# sources (`esharp/src/<path>`) and a manifest
(`esharp/library.json`: format, id, version, targets, minSdk, features, defines, licence, and `assets` and `native` when it has them) as embedded resources, plus its C files and headers (`esharp/native/<common|target>/<name>`), the same idea as the
compiler embedding Runtime and Platform (`LibrarySources.cs`). The pieces:

- **Embedding**: `src/ESharp.Sdk/buildTransitive/AmigaESharp.Sdk.targets`, imported into every project that references
  AmigaESharp.Sdk. With `EsLibrary=true` it defines `HOST`, checks `EsTargets`/`EsFeatures`/`ImplicitUsings`/`<Using>` items and refuses
  misplaced C files, prebuilt code and E# target symbols in `DefineConstants` (codes ESL001-ESL015; ESL010 and ESL011 are warnings), writes the manifest (with the
  library's own symbols as `defines`) and adds the resources before the build. A library with an `assets.json` ships the
  `Assets.g.cs` that `esharp assets` wrote: an inline task reads the namespace, class and input files of `assets.json`
  (missing file ESL005, no namespace ESL012, an input whose content differs from the SHA-256 that `Assets.g.cs` records
  for it ESL011; a file without those lines, from an older `esharp assets`, by file time), and the manifest's `assets` names the file and
  the namespace and class that `Assets.g.cs` declares. A second inline task (`EsLibraryNativeFiles`) takes the project's `None`/`Content` items:
  `native/common/*.c|h` and `native/<target>/*.c|h` are embedded and listed in the manifest's `native` with the prefix
  (`EsNativePrefix`, default from the id); anything else in `native/`, a `.c`/`.h` elsewhere and `.o`/`.obj`/`.a`/`.lib`/`.so`/`.dylib`
  anywhere are ESL006; NativeC and the files must go together (ESL015); a bad prefix is ESL014. The converted data is shipped rather than the inputs because the conversion is the same for every target (one
  `Assets.g.cs` serves all of them for a program too) and the package then carries exactly what its author built and tested;
  the game's build parses it as any package source, so `[ChipRam]`, the chip report and the C names (`Es_<Namespace>_<Class>_<Field>`)
  work as for program assets. `EsCompiler` parses each package's files with
  the target's symbols plus its `defines`; the generic expansion (`Monomorphizer`) re-parses a rewritten tree with that tree's own options.
- **Loading**: `src/ESharp.Compiler/Packages.cs`. `PackageLoader.FromAssetsFile` walks `obj/project.assets.json` (the first
  target framework, every package of type `package`), `PackageReader` reads the resources of each compile DLL through
  `System.Reflection.Metadata` without loading it, and skips DLLs with no manifest. Problems become `EsPackage.Problems`
  (ES0118; also an `assets` entry it cannot read, or one whose source the package does not carry: `PackageAssets`). `Licenses` evaluates SPDX expressions (copyleft when every OR choice has a GPL-family id), `SdkVersions` the
  minimum version. The CLI side is `src/ESharp.Cli/PackageStep.cs` (finds the csproj and the restore, reads `EsAllowCopyleft`,
  prints the packages); `HostRun` references the package DLLs for the host run.
- **Checks**: `src/ESharp.Compiler/PackageChecks.cs`, ES0110-ES0123 (descriptors in `Descriptors.cs`; compiler-only, the IDE
  sees a package as metadata, except ES0117 below). ES0115 names an `assets.json` when the clashing type is a generated asset class. The subset rules need no
  change for package code: package trees are in the user set.
- **Deriving from a package class**: a package class marked `[Extensible]` can be the base of a class of the program or of
  another package. In `esharp build` nothing is special about it: package trees are user trees, so the emitter lays out the
  hierarchy (class tables, `es_base`, `X_new`/`X_new_init` chaining, `is`/`as`) as within one program; `PackageChecks` gives
  ES0117 only for a base without the attribute whose package differs from the deriving class's. Where the package is a referenced
  assembly (IDE, `dotnet build` of the game) `SubsetAnalyzer.CheckClass` does the same from metadata: a base type from an
  assembly that is an E# package (`EsPackageId`: the `[assembly: ESharp.EsLibrary(id)]` that the Sdk targets add through an
  `AssemblyAttribute` item, or, without it, an assembly that references ESharp.Runtime/ESharp.Platform, named by its assembly
  name) is accepted when it has `[Extensible]` and is ES0117 (same descriptor, same message) when not; any other metadata base
  stays ES0007. ES0117 is in `Descriptors.SubsetRules` for that. The same `EsPackageId` keeps a package's generic methods from
  being ES0008 in the IDE (in `esharp build` they are source and monomorphised).
- **Package C**: `src/ESharp.Compiler/PackageNatives.cs`. `Select` picks a target's files (common, a target file in place of a
  common one of the same name); `CheckTargetHasC` gives ES0119 (the package has prefixed `[Native]` externs in the C# compiled for the target, from `Prototypes`, and no `.c` for it), `CheckForeignExterns` ES0123 (a `[Native]` extern outside the package with its prefix), `PathProblem(path, all)` refuses file names that differ only in case (also in the SDK's `EsLibraryNativeFiles` task), `Check` gives ES0120 (an exported name without the prefix:
  `ExportedSymbols` is a light reading of the C source, comments, strings and preprocessor lines left out, file-level definitions
  that are not `static`, K&R definitions and `extern` with an initializer included, the name taken from inside a parenthesised
  declarator such as `(*hook)`) and ES0122 (overlapping prefixes); `CFlags` (`-fno-common`) goes on the compiler line of a build with package C, so
  uninitialised variables of the same name in two translation units are a link error, not merged; `Prototypes` makes the C prototype of each `[Native]` extern whose
  name has the package's prefix from its C# signature (numbers, bool, string, pointers to those or void; else ES0121). The
  emitter puts them after the includes (`CEmitter.PackagePrototypes`), so the program's C never includes a package header, and
  `Outputs` gives `CompileResult.Native`: `esharp-native/<id>/` with the chosen files, each `.c` starting with
  `#include "esharp_natives.h"` (the same prototypes: a definition that does not match is a compile error) and a `#line` to
  `<package Id Version>/native/<folder>/<name>`. `Program.Build` writes them under `obj/<target>/` (cleared first) and adds the
  `.c` files to the compiler command after the program's C (Docker or local toolchain alike; not to the `-c` object for
  `esharp addr`). No include path is added, so `#include "x.h"` reaches only the package's own folder.

- **Precompiled binaries** (issue #154). A split build: the package's code in one object, the program's in another, linked.
  `CEmitter.Split.cs` holds the plan. *Library mode* (`CompileOptions.LibraryBuild`, run by `esharp precompile` for each target, which
  the Sdk's `EsPackBinaries` target calls at `dotnet pack` through `EsCommand`): no program sources and no entry point; the emitter
  defines what is declared in the package's trees (functions, static fields with external linkage, class tables `X_vt`, field
  initializers `X_finit`, the generic copies the package makes) and declares the rest, and `main` becomes `es_pkginit_<id>()` (its static
  initializers and constructors). `CompileResult.Library` records the generic copies' C names (`Monomorphizer.Result.MadeTypes` /
  `MadeMethods` tell a copy from other code, by qualified name), the runtime pieces the code uses and its library bases. `Precompile.BuildBinary` compiles
  that C and the package's C files (`-c`, fixed names, in `/work` with Docker) and archives them with `ar rcsD`; `PackageBinaries`
  writes `binary.json` (stamp: tool version + the compiler assembly's MVID). *Program mode* (`EsCompiler.ChooseBinaries`: a binary for
  `PackageBinaries.BinaryTarget(target)` with this stamp and `Config`, else ES0124): `IsExternal` is true for a symbol of a linked
  package's tree unless it belongs to a generic copy the binary does not list; those are declared only, the rest is emitted as before,
  and `main` calls the init functions after the platform's static initializers. Both modes compute the same reachable set for the
  package (its whole code is a root, as program code is; in a split build every slot of a package class's table is reached), so the
  program defines every platform function and runtime variable a binary refers to; the runtime's state (`RuntimeState` at each
  definition) gets external linkage in the program and is `extern` in a binary, and lambdas are `static`. Whatever is compared by
  address has one definition in the link, by its owner: class tables `X_vt` (type ids), interface tables `I_vt_X` (`is` and switch on
  an interface value) and method-group wrappers `F__dg` (an event's `-=`; `DelegateWrapper`: the binary lists the wrappers it defines
  and the platform's it only declares, which the program defines). A binary does not know the program's classes, some of which derive
  from its `[Extensible]` classes: in a split build every interface table starts with `es_poly` (its object starts with a class table
  pointer), and in library mode `IfaceIs` adds, for a class with type ids, a test of the object's own type id (`es_type_of`, `es_is`)
  after the tables it knows; an `[Extensible]` class always has a class table in a split build, so a program subclass of one without
  virtual members has the layout the binary was built with. A package with E# dependencies is not precompiled (ES0125;
  `esharp precompile` skips it), so a binary holds one package and its init order cannot run before a dependency's. `PackageBinaries.ReadFolder` checks the binaries
  beside a restored DLL (listed files, SHA-256, archive members: GNU/BSD ar and the Amiga hunk library that m68k-amigaos-ar writes, the
  package's id, version and sources) and `StrayPrebuilt` refuses any other prebuilt file (ES0118). `Program.Build` copies the archives
  to `obj/<target>/esharp-bin/<id>/` and puts them after the program's C. `esharp verify-package` rebuilds each binary with
  `BuildBinary` and compares bytes.

Tests: `PackageTests.cs` (in-memory packages, generated C built and run with the host gcc, every check; a package whose
`Assets.g.cs` comes from `AssetBuilder`, its chip data checked in the C and the chip report and read back by the program) and
`PackageRoundTripTests.cs` (the samples/packages projects: dotnet pack, restore from a local feed, build, host run, m68k
build with the image; the library's asset checks ESL005, ESL011, ESL012 and the outside-file warning ESL010; the C library
`Checksum`: ESL006, ESL014, ESL015, the embedded C and the package C in the m68k build; the `[EsLibrary]` and `[Extensible]` metadata
of the packed DLL, `dotnet build` of the game with its class derived from `Tween.Mover` and ES0117 from both `dotnet build` and
`esharp build` for a class derived from a class without the attribute); `PackageTests.cs` also builds a package's
C with the program's C under the host gcc and runs it, and checks a mismatched C definition fails to compile; `tools/package-smoke.sh` repeats it with the packed AmigaESharp.Sdk (and, with an m68k image, packs a library with binaries, links it and runs verify-package).
`PrecompiledTests.cs` builds a package's library-mode C with the host gcc into an archive, links the program-mode C with it and runs it
(the same output as the source build; generic copies over a program type; a binary from another E# or configuration falls back with
ES0124; the binary folder checks); `PrecompiledRoundTripTests.cs` packs Tween and Checksum with binaries through the Sdk step (sdl with
the host gcc, m68k when its image is present), links them into TweenDemo, and runs verify-package (pass, and fail on a tampered binary)
and two precompiles in different folders (same bytes).

## Amiga shared libraries

A project with `EsOutputType` `AmigaLibrary` (issue #154; m68k AmigaOS 3.x, AROS x86-64 and AmigaOS 4) is compiled as a program without an entry point
(`CompileOptions.AmigaLibrary`). The pieces:

- **Rules, registers, `.fd`, binding:** `src/ESharp.Compiler/AmigaLibrary.cs`. `AmigaLibraries.Plan` finds the `[LibraryFunction]`
  methods of the program's trees (by file path, then position: that is the vector order), checks them (ES0130-ES0135, descriptors in
  `Descriptors.cs`) and gives each its registers (`AssignRegisters`: numbers d0-d7, pointers a0-a3, overflow into the other kind's
  free registers, never a4/a5). `EsCompiler` runs it before the subset's later steps for the diagnostics, and again on the final
  compilation (generics expanded, C keywords renamed) for the emitter. `Fd` writes the `.fd` file; `Binding` writes the C# binding (a
  struct per opener with `#if AMIGA` vector calls through `[LibraryCall]` externs and, elsewhere, direct calls of the library's methods
  between `SharedLibrary.Enter` and `Leave`; the boundary structs copied, fields only, for the Amiga build of a program that does not
  compile the library). `CompileResult.AmigaLibrary` carries them.
- **The library's C:** `src/ESharp.Compiler/CEmitter.AmigaLibrary.cs` (`LibMode`). The E# data of one opener is one block: the
  runtime's variables (`RuntimeState` and the `[LibraryBase]` bases go into `struct es_rt`, written where the includes end, at
  `LibRtMarker`) and the program's static fields, properties and events (`EmitStaticField`, `EmitStaticProperty` and the events call
  `LibStatic` instead of defining a global; `struct es_user` follows the types, at `ES_RT_SIZE` in the block). Each name is a macro for
  its member through `es_d`, so every other part of the emitter is unchanged. On m68k `es_d` is a global register variable in a4 (the
  rest of the file never uses a4); each vector's entry (`es_lib_entry_<F>`, register parameters with `__asm("d0")`, the base in a6) calls
  `es_lib_call_<F>`, which saves the caller's a4, points it at the opener's block and restores it. A constant initial value is a
  `static` template copied in (`_libInit`); the rest runs in `es_lib_data_init` (`EmitDeferredStatics` and the static constructors),
  and `es_lib_data_fini` runs the `[AtExit]` methods and frees the opener's pool. `es_new` and `free` become `es_lib_alloc` and
  `es_lib_free` (`FreeName`): an exec pool per opener on m68k, a list of calloc blocks elsewhere (the host tests); `es_fatal` ends in
  `es_lib_fatal` (an alert). Under `#if defined(__mc68000__)` follow the library proper: `SysBase` and the shared ROM library bases
  (`SharedBases`: dos, graphics, intuition, from the headers the code includes), the base layout `struct es_lib_head` (exec's
  `struct Library`, the master base, the segment list, an opener's allocation size), init (from InitResident's RTF_AUTOINIT table),
  open (copies the jump table and head into a new block with the data after it, `CacheClearU`, `es_lib_data_init`), close, expunge, the
  reserved vector, the vector table and the init table. `LibraryRomTag` puts the start of the file in top-level asm (gcc writes it
  before the functions, and the file is the link's first object): `moveq #-1,d0; rts` and the RomTag right after it, because ramlib on
  Kickstart 3.1 did not find one further into the hunk. `CheckLibraryFeatures` reports what a library cannot use yet (ES0134).
  `LibOs` picks the library proper by target: `EmitM68kLibrary` (above, text unchanged so m68k builds are byte for byte the same),
  `EmitArosLibrary` (under `#if defined(__AROS__) && defined(__x86_64__)`: a C `struct Resident`, an RTF_AUTOINIT table of function
  addresses, init with `AROS_UFH3`, open, close, expunge and the entries with the `AROS_LH` macros, LVO 5 for vector -30, `es_d` in
  r15, the same copy-the-base-per-opener scheme) and `EmitOs4Library` (under `#if defined(__amigaos4__)`: a Resident with RTF_NATIVE |
  RTF_AUTOINIT and CreateLibrary tags, the manager interface `__library` and the main interface `main` marked IFLF_PRIVATE, whose
  Clone makes each opener's interface with the opener's data in its MIT_DataSize area (the clone flags set only after the data is set
  up, as DeleteInterface calls Expunge) and whose Expunge frees it; entries take the
  interface as Self, `es_d` in r14; `_start` returns -1; no libauto, so the shared ROM libraries' interfaces are got at init).
  `AmigaLibraries.TargetProblem` turns a library build for MorphOS, AROS i386 or sdl into ES0134.
- **The program side:** a `[LibraryCall(offset, registers)]` extern (`EmitLibraryCall`) is a C function that loads the arguments into
  their registers and a6 from its first parameter, and `jsr offset(a6)` with d0, d1, a0 and a1 as clobbered outputs (ES0136 for a bad
  declaration). On AROS and AmigaOS 4 (`EmitLibraryCallThroughTable`) it is a call through the opener's table, every value as a
  `uintptr_t`: AROS `AROS_LC<n>` with the LVO, AmigaOS 4 the interface's function table after `struct InterfaceData` (slot 4 for
  vector -30) with the interface as Self; ES0099 on MorphOS and AROS i386. `src/ESharp.Platform/SharedLibrary.cs` opens and closes
  (exec on the Amiga; on AmigaOS 4 `es_lib_open_main`/`es_lib_close_main`: OpenLibrary plus GetInterface `main`, DropInterface plus
  CloseLibrary; on the host and sdl a handle), counts (`es_lib_open_count`: FindName in exec's list under Forbid; on AROS the list is
  found from exec's own node, not through `struct ExecBase`, whose SDK layout is not the running nightly's) and, on the host, keeps
  each opener's statics apart by reflection (start values deep-copied per opener, swapped in and out by `Enter`/`Leave`).
- **The CLI:** `src/ESharp.Cli/AmigaLibraryStep.cs` reads the csproj properties and finds the AmigaLibrary folders in a program's
  `esharp.include`. `Program.Build` builds a library project (`-nostartfiles -nostdlib ... -lgcc`, output `<name>.library`, plus the
  `.fd`, the binding, the map and the `-g` object), and for a program that uses one: drops the library's files from an Amiga build,
  writes and adds the binding, and after the link builds the library with the same toolchain options and copies it next to the program.
  `esharp test` adds the binding to the host run. `FsUae.Run` copies every `.library` beside the program into `DH0:Libs`, `ArosHosted.Run`
  into the fresh tree's `Libs`, and `Os4Vm.Run` onto the CD's `Libs` drawer, which its `esrun` script adds to `LIBS:`
  (`AmigaLibraryStep.BuildsFor`: m68k, aros, os4).

Tests: `AmigaLibraryTests.cs` (registers, the rules, the `.fd` read back by BindGen's parser, the binding compiled into a program for
m68k and sdl, `[LibraryCall]` checks for m68k, AROS and AmigaOS 4, and the library's C built with the host gcc, with AddressSanitizer
and LeakSanitizer when they are there: two openers called through `es_lib_call_<F>`, each with its own data, then torn down with
nothing left; the AROS library proper run on an x86-64 host with gcc against a stand-in of the AROS headers it includes, called
through its jump table as InitResident, OpenLibrary and CloseLibrary would), the host side of `SharedLibrary` (`SharedLibraryHostTests`)
and `AmigaLibraryCliTests` (`esharp build` of samples/amigalib and samples/amigalibdemo for m68k, sdl, aros and os4, refusals for
aros-i386 and morphos, `esharp test` of the demo on the host, and with the pinned m68k image the RomTag at the start of the built file).

## Packaging

`dotnet pack ESharp.sln` makes **AmigaESharp.Tool** (the CLI, with the embedded sources, `host/`, `os4vm/` and `mui/` folders)
and **AmigaESharp.Sdk** at one version (`Directory.Build.props`). `esharp new` writes a csproj that references the Sdk at the
tool's version. Anything the CLI needs at run time must ship in the tool package, not be read from the repository layout.
`.github/workflows/release.yml` publishes on pushes to main; nobody publishes by hand (STATUS.md, "NuGet packages").
