# E# guide

The reference material behind the [README](../README.md): installing, the language subset, the platform APIs, assets,
icons, MUI, running in emulators and VMs, and the `esharp` commands. [STATUS.md](STATUS.md) is the source of truth for what
is verified (it says "not verified" where something was not run); this guide says how to use things.

Contents: [Install](#install-and-the-packages) - [Projects and VS Code](#projects-and-vs-code) - [Libraries](#libraries-e-packages) - [The language subset](#the-language-subset) -
[Platform APIs](#platform-apis) - [Assets](#assets) - [Workbench icons](#workbench-icons) - [Building](#building) -
[Running and testing](#running-and-testing) - [Emulators and VMs](#emulators-and-vms) - [MUI](#mui) - [Debugging](#debugging) -
[Commands](#esharp-commands)

## Install and the packages

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). That is enough to write, run and test E# code on your PC.
E# is on nuget.org as a .NET tool ([AmigaESharp.Tool](https://www.nuget.org/packages/AmigaESharp.Tool)), with the
[AmigaESharp.Sdk](https://www.nuget.org/packages/AmigaESharp.Sdk) package for projects; every change merged into main
(other than Markdown or workflow-only changes) publishes a new `0.1.x` release.

```bash
dotnet tool install -g AmigaESharp.Tool      # the esharp command
esharp new mygame && cd mygame
dotnet run                                   # on the PC: Hello from E#!
esharp test                                  # the ok/FAIL checks on the PC
esharp build --emit-only                     # writes obj/m68k/mygame.c, no C compiler needed
```

`esharp new` writes a csproj that references **AmigaESharp.Sdk** (the runtime, the platform contracts with their host
fakes, and the subset analyzer) at the tool's own version. The installed tool needs no checkout for `new`, `build`,
`run`, `test`, `assets`, `addr` and `os4vm`. A checkout is still needed to build the `esharp-amiga-toolchain`,
`esharp-sdl`, `esharp-aros-hosted` and `esharp-aros-i386` images (the pinned m68k image works without one), and for the
scripts in `tools/` other than the os4 VM helper and the MUI fetch script, which ship in the tool. To try a change before
it is released, pack it and install from a local folder feed:

```bash
dotnet pack ESharp.sln -c Release -o artifacts/packages -p:Version=0.1.0-local.1
tools/package-smoke.sh artifacts/packages 0.1.0-local.1 <m68k image>   # installs into a temporary tool path and tests a new project
```

Never publish these packages by hand: the release workflow does it (docs/STATUS.md, "NuGet packages").

## Projects and VS Code

`esharp new <name>` (or a path such as `games/demo`; the last segment is the project name and must be a valid C#
identifier) writes a project you can open in VS Code (with the C# extension): `Program.cs`, a `<name>.csproj` that builds
the same source under .NET against the E# runtime and loads the subset analyzer, a `.gitignore`, and a `.vscode/` folder
with `tasks.json` (build for m68k and run in FS-UAE, by Docker or by a [local toolchain](#a-local-toolchain-instead-of-docker),
run on the host, check the subset), `launch.json` (debug the host run
with the coreclr debugger, so logic can be stepped through; the Amiga build cannot be debugged at source level yet),
`extensions.json` and `settings.json` (the `esharp.toolchain` setting the local-toolchain tasks read, empty). The ES0001-ES0010 subset errors show as you type and in `dotnet build`. ES0101 (a library call newer than
the target's libraries; opt in with `[RequiresLib("lib", version)]`) is reported only by `esharp build`/`run`/`test` for the
m68k, aros and aros-i386 targets, not in the IDE or the host build. ES0103 (a program uses an `internal` or `private protected` member of E#'s library, or a member of an internal library type) is reported by `esharp build`/`run`/`test`; the IDE and the host build already reject the same code with C# error CS0117/CS0122, because there the library is a referenced assembly.

With the installed tool the csproj references the AmigaESharp.Sdk package and the tasks call `esharp`. When the CLI runs
from a checkout (`dotnet run --project src/ESharp.Cli -- new ...`), or with `esharp new <name> --source [dir]`, the csproj
and the tasks point at that checkout instead (found from the CLI's location, or `ESHARP_ROOT`; edit `ESharpRoot` in the
csproj if you move it); `--package [version]` forces the package reference.

A program prints lines that start with `ok NAME` or `FAIL NAME` (and `skip NAME` for a check it cannot run on this
machine); `esharp test` runs it on the host and, optionally, on a target, and compares those lines
(see [ARCHITECTURE.md](ARCHITECTURE.md#how-tests-compare-host-and-target-runs)).

Editing in VS Code is verified on macOS (2026-11-10); the editor on Windows and Linux is not verified (docs/STATUS.md,
"VS Code support").

## Libraries (E# packages)

An E# library is an ordinary .NET class library on NuGet whose DLL also carries its C# sources and a small manifest, both as
embedded resources (issue #31, first wave). The IDE, `dotnet build`, `dotnet run` and the host side of `esharp test` use the
compiled DLL (the library built with `HOST`). `esharp build`, `run` and `test --target` read the sources back out of the
restored package and compile them with your program for the chosen target, so every `#if AMIGA` / `#elif SDL` branch is
there. `samples/packages` has a library (`Tween`: easing curves and a generic tween struct) and a game that uses it
(`TweenDemo`), with the steps in its README.

**Writing a library.** Reference AmigaESharp.Sdk and set `EsLibrary`; the Sdk's MSBuild step (in the package's
`buildTransitive/`) embeds every C# file of the project and writes the manifest when the library is built:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>disable</ImplicitUsings>       <!-- required: the Amiga build sees only the embedded files -->
  <EsLibrary>true</EsLibrary>
  <EsTargets>m68k;os4;morphos;aros;aros-i386;sdl</EsTargets>   <!-- required: the targets it supports -->
  <EsFeatures></EsFeatures>                      <!-- AmigaLib, Native, InlineAsm: platform-level features it uses -->
  <PackageId>MyName.Easing</PackageId>
  <Version>1.0.0</Version>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="AmigaESharp.Sdk" Version="<the esharp version>" />
</ItemGroup>
```

then `dotnet pack -o <feed>`. The manifest (`esharp/library.json` in the DLL) holds the package id and version, the targets,
`minSdk` (`EsMinSdkVersion`, by default the AmigaESharp.Sdk version the library was built against), the features and the
licence expression. Rules for the code: the same subset as a program (the analyzer from the Sdk runs on the library in the
IDE and in `dotnet build`); no `[EntryPoint]`; types in a namespace of your own; `public` for what programs may use
(`internal` stays inside the library, as it would between two assemblies). Declaring `[AmigaLib]` bindings, `[Native]`
externs or `[InlineAsm]` bodies needs `AmigaLib`, `Native` or `InlineAsm` in `EsFeatures`; using the platform API (which may
itself be bindings, such as `Dos.PutStr`) does not. C# only for now: an `assets.json` or C files in the project stop the
library build, and `esharp build` refuses a package that carries either (both are planned). Community packages use any id
plus the `esharp` tag (the step adds it); `AmigaESharp.*` ids are meant for the official packages.

**Using a library.** Add a `PackageReference` to the game's csproj, run `dotnet restore` (or `dotnet build`), then build as
usual. `esharp build` reads `obj/project.assets.json`, takes every package of the restore (direct and transitive) whose DLL
has an E# manifest, and prints each one (`Package MyName.Easing 1.0.0 (MIT): 2 C# file(s)`, plus the platform-level features
it declares). Without a restore it stops and says to run `dotnet restore`; when the csproj is newer than the restore it
warns. The checks on package code:

| Check | Diagnostic |
|---|---|
| The subset rules, ES0101 against the target's library versions, ES0099 and C# errors, as for program code; reported at `<package Id Version>/File.cs(line,col)` (also in the `#line` directives, so `esharp addr` names the package file) | ES0001..., ES0101, ES0099, CS... |
| The package does not list the chosen target | ES0110 |
| A GPL, LGPL or AGPL licence (every choice of the SPDX expression; `MIT OR GPL-3.0-only` is fine), in any E# package of the restore; allowed with `<EsAllowCopyleft>true</EsAllowCopyleft>` in the game's csproj (a literal value), and then printed as a warning | ES0111 |
| The package needs a newer esharp (`minSdk`; not checked by a `-dev` build from a checkout) | ES0112 |
| `[AmigaLib]`, `[Native]` or `[InlineAsm]` in the package without the matching `EsFeatures` entry | ES0113 |
| An `[EntryPoint]` in a package | ES0114 |
| The same type (namespace, name, arity; also a `partial` one) in a package and the program, or in two packages | ES0115 |
| An `internal` member of a package used from the program or another package | ES0116 |
| A program class derived from a package class (not supported yet) | ES0117 |
| A package that cannot be used: an unknown manifest format, assets, C files, a bad path | ES0118 |

Types with the same name in different namespaces (the program's `Game.Ease` and the package's `Tween.Ease`) are fine: their C
names carry the namespace, as for any two namespaces. Package code is emitted in full, like program code (unused package
functions are not pruned yet). `esharp.include` stays the way to share source folders between local projects without NuGet.

## The language subset

E# accepts a defined subset of C# and rejects the rest with diagnostics (`ES0001`...). Extra meaning comes from
attributes (`[EntryPoint]`, `[AmigaLib]`, `[ChipRam]`, ...) and a small intrinsics library. This is a summary;
[STATUS.md](STATUS.md) has the exact state, the limits and what is verified, and each feature has a sample (see the
[feature list in the README](../README.md#features)).

**Supported:**
- Static classes and methods, structs (methods, constructors, properties, indexers), enums, small integer types,
  fixed-point types (`Fix`), fixed-size arrays and span views, `unsafe` pointers, `ref`/`out`/`in`, value tuples, patterns and
  switch expressions (positional, property and list patterns), local functions, static constructors, string interpolation
  and concatenation (formatted into a small ring of static buffers).
- Monomorphised generics (structs, static methods, `Pool<T>`, `Vec<T>`, `Dictionary`/`HashSet`), heap classes (`new`, no garbage
  collector: `Heap.Free`), single inheritance with `virtual`/`abstract`/`override`, `is`/`as` to classes (also from interface
  values), nested classes, fixed-size array fields and field initializers in classes, interfaces (generic constraints and dynamic
  dispatch), delegates, lambdas, field-like events on classes, and LINQ over arrays as fused loops.
- Lambdas that capture live on the stack while they are only passed down; stored or returned ones and event handlers are
  reference-counted heap closures that you free with `Heap.Free` (`Heap.LiveClosures` is the leak check). A delegate given to an
  `[Owned]` parameter or a property setter is the callee's to free; if it is a field, or a local you use again or free, the callee
  takes a reference of its own, so you still free yours.
- User classes that derive from a platform class marked `[Extensible]` (so far `GtWindow`, see `samples/guiclass`).
- Slow or opt-in: `long`/`ulong` (software 64-bit, rule ES0100 on the 68020), `float`/`double` (off by default, ES0003; `--allow-float`).

**Not supported** (the analyzer or the emitter says so; the emitter reports `ES0099` and never miscompiles): exceptions, `async`,
reflection, `dynamic`, a garbage collector, generic classes (ES0008; generic structs and methods work), records, `is`/`as` to an interface or struct type, inheritance from generic classes or
from library classes that are not `[Extensible]`, `ToArray`/`ToList`, `foreach` over anything but arrays, spans and struct/class
`GetEnumerator` types, delegates in struct fields. The exact list of emitter gaps is "Known limits of the emitter" in
[STATUS.md](STATUS.md).

**Attributes:** `[EntryPoint]`, `[AmigaLib("dos", offset)]` (a library vector), `[ChipRam]`, `[Interrupt]` (an exec interrupt
server; `&M` is its address for `Interrupts.AddVBlank`; m68k and AROS, `ES0099` on PowerPC and sdl), `[InlineAsm("68020 code",
Ppc = "...")]` (GCC inline asm with named register operands; the C# body is the fallback on other targets), `[HookEntry]`
(a BOOPSI hook entry per target), `[RequiresLib("lib", version)]`, `[Native]`, `[Extensible]`.

**Memory:** no garbage collector and no hidden allocation. Static and stack first; allocate big, rarely, up front (`Arena`,
`Pool<T>`, `Vec<T>`, `Mem.AllocChip` for chip RAM); a failed allocation is a null return, not an exception.
See [MEMORY.md](MEMORY.md) (step 1, `Mem` and `Arena`, is implemented; the later steps are proposals).

**Names:** the C names of E# types are prefixed where two types would meet (same-named classes in two namespaces, or
classes called `Window` or `List`); see `samples/names`.

## Platform APIs

`src/ESharp.Platform` is parsed together with your code. The same public signatures exist on every target; the
implementation is chosen with `#if AMIGA` / `#elif SDL` / `#else` (the dotnet host fake). Amiga-only classes (Gui, Asl, Iff,
Fonts, Joystick, BOOPSI, MUI and the raw library bindings) do not exist on `sdl`: using them is error `ES0102`.

| Area | Classes | Sample |
|---|---|---|
| Program | `Dos` (output, delays), `Args` (arguments and Workbench tool types), `Trace`, `Files` | `hello`, `mainargs`, `trace` |
| Memory | `Mem`, `Arena`, `Pool<T>`, `Vec<T>`, `Heap`, `Dictionary`, `HashSet`, `StringMap` | `memory`, `collections` |
| Graphics | `Video` (screens, blits, palettes, double buffer, AGA 24-bit palettes, RTG), `Font`, `Frame`/`FrameTimer` | `screen`, `aga`, `dbuffer`, `rtg`, `timing` |
| Input and time | `Input` (mouse, keyboard), `Keys` (keymap), `Joystick` (lowlevel), `Clock` (timer) | `cards`, `keys`, `controls` |
| Sound | `Sample`, `Sound` (audio.device) | `sound`, `assets` |
| Utility | `Date`, `Strings` (case-insensitive text, utility.library), `Rng`, `Deck` | `dates`, `cards` |
| Files and formats | `Iff` (iffparse), `Fonts` (diskfont), `Asl` (file requester) | `iff`, `fonts`, `asl` |
| GUI | `GtWindow` and gadgets (GadTools), `BoopsiObject` and typed attributes, hooks, `Mui*` wrappers | `gui`, `guiclass`, `boopsi`, `mui` |
| System | `Exec`, `Graphics`, `Intuition`, `CyberGfx` and other `[AmigaLib]` bindings, `Interrupts`, `TagList` | `interrupts`, `hwinfo` |

The Amiga library bindings are hand-written `static extern` methods tagged `[AmigaLib]`, with struct offsets measured per
target into `Layout.g.cs` (`tools/BindGen/layouts.sh`). `tools/BindGen` can also generate bindings from `.fd` files and
headers you provide, and compare them with the hand-written ones (`check`, `gen ... --check`, see
[ARCHITECTURE.md](ARCHITECTURE.md#adding-a-platform-binding)). Libraries are opened at version 39 or higher (keymap.library at
37, lowlevel.library v40 optional, muimaster.library 19 optional).

Hardware facts: 2 MB of chip RAM is the baseline; `[ChipRam]` is a no-op on PowerPC (chip memory is allocated at run time
there); both PowerPC targets are big-endian, so asset formats need no change.

## Assets

`assets.json` lists images, tile sheets, palettes and sounds; `esharp assets samples/screen` turns them into
`Assets.g.cs` (planar chip data, palettes, 8-bit signed samples) next to the manifest. `esharp build`, `run` and `test` do
this automatically when an input is newer than the output (`--force` rebuilds); commit the `Assets.g.cs` it writes.
Details in docs/STATUS.md ("Asset dithering, ILBM and sound assets"):

- Images are PNGs, or IFF ILBM files (`.iff`, `.ilbm`, `.lbm`; 1 to 8 planes, uncompressed or ByteRun1, masks; HAM and
  Extra-Half-Brite are rejected).
- Colour reduction is median cut with 12-bit OCS rounding, or AGA 24-bit palettes; `"dither": "fs"` (Floyd-Steinberg) or
  `"ordered"` (4x4 Bayer) is optional per image.
- `"samples"` take WAV (PCM 8/16 bit, mono/stereo) or 8SVX and write 8-bit signed mono `[ChipRam]` data with its length and
  rate; play it with `Sound.Play(in s, Sound.PeriodForRate(rate), vol, n)`.
- `--chip-budget <n>` fails a build when constant chip RAM reservations exceed `n` bytes (68k; only constant sizes are counted).

`samples/screen`, `samples/aga` and `samples/assets` show each of these. The inputs of `samples/assets` are made by
`tools/AssetSamples`, the card sprite sheet by `tools/CardArt`.

## Workbench icons

An Amiga build (`m68k`, `os4`, `morphos`, `aros`, `aros-i386`; not `sdl`, not `--emit-only`) also writes `<name>.info` next
to the program: a Tool icon (classic image for every Amiga OS, the colour image for OS 3.5 and later, OS 4, MorphOS and AROS,
and a NewIcons image in the tool types for Workbench 3.0/3.1 with NewIcons installed) so the program shows in a Workbench
window and starts by double-click. Properties in the project's csproj set it:

| Property | Meaning |
|---|---|
| `EsStackSize` | stack in bytes (default 65536) |
| `EsToolTypes` | tool types such as `SEED=42;AUTO`; `Args` reads them on a Workbench start (without the NewIcons lines) |
| `EsIcon` | a PNG instead of the default E# picture, icon-sized (32 to 64 pixels; up to 256 x 256 is accepted but huge on Workbench 3.1); `none` writes no icon |
| `EsNewIcons` | `false` leaves the NewIcons image out; a PNG gets it only up to 93 x 93 and at most 32 colours |

Only literal values are read (no `$(...)`, `Condition` or `Directory.Build.props`). Copy the `.info` together with the
program. The code is in `src/ESharp.Assets/Icon*.cs` and `src/ESharp.Cli/IconStep.cs`; `samples/wbstart` is a project that
sets these properties.

## Building

Targets (the table with what is verified is in the [README](../README.md#targets) and in STATUS.md): `m68k` (default), `os4`,
`morphos`, `aros`, `aros-i386`, `sdl`. There are two ways to get the C compiler: **Docker** (the default, used in all the
examples: the compilers live in pinned images, `--docker <image>`) or a **local toolchain** installed on your machine
(`--toolchain <dir>`, see [below](#a-local-toolchain-instead-of-docker)). `esharp doctor` says which of the two each target can
use here. `--cc` and `--cflags` override the compiler and its flags either way.

| Target | Image | Symbols |
|---|---|---|
| `m68k` | `amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859` (1.1 GB, m68k only) or `esharp-amiga-toolchain` | `AMIGA M68K AMIGAOS3` |
| `os4`, `morphos`, `aros` | `esharp-amiga-toolchain` (about 5.7 GB, all four Amiga compilers; `docker build -t esharp-amiga-toolchain docker`) | `AMIGA PPC AMIGAOS4`, `AMIGA PPC MORPHOS`, `AMIGA X86_64 AROS` |
| `aros-i386` | `esharp-aros-i386` (`docker build -t esharp-aros-i386 docker/aros-i386`, about 14 minutes, 1.3 GB; [docker/aros-i386](../docker/aros-i386/README.md)) | `AMIGA I386 AROS` |
| `sdl` | `esharp-sdl` (`docker build -t esharp-sdl docker/sdl`; gcc and a static SDL 3) | `SDL` |

```bash
esharp build samples/hello --target m68k --docker <image>      # obj/m68k/hello, obj/m68k/hello.info
esharp build samples/hello --target os4 --docker esharp-amiga-toolchain
esharp build samples/hello --emit-only                          # just the C file; no compiler needed
```

Build options: `-o <dir>`, `--emit-only`, `--docker [image]`, `--toolchain <dir>`, `--sysroot <dir>`, `--any-cc-version`, `--cc`, `--cflags`, `--allow-float`, `--chip-budget <n>`,
`--debug` (bounds-checked arrays and spans, poisoned arena `Reset`; also defines `TRACE`) and `--trace` (keep `Trace` calls).
Symbols: `AMIGA` is defined for every Amiga-family target and `M68K`/`PPC`/`X86_64`/`I386` and
`AMIGAOS3`/`AMIGAOS4`/`MORPHOS`/`AROS` narrow it; `sdl` defines `SDL` only; the dotnet host defines `HOST`.

### A local toolchain instead of Docker

Without `--docker`, `esharp build` (and `run`, `test`, `addr`) runs the target's compiler on this machine. It looks in this
order:

1. `--toolchain <dir>`: the toolchain's install prefix, the folder that holds `bin/<compiler>` (for m68k the folder the
   pinned image has at `/opt/m68k-amigaos`; `bin/<compiler>.exe` on Windows). An empty `<dir>` counts as not given.
2. `ESHARP_TOOLCHAIN_<TARGET>`: `ESHARP_TOOLCHAIN_M68K`, `_OS4`, `_MORPHOS`, `_AROS`, `_AROS_I386`, `_SDL`.
3. `ESHARP_TOOLCHAIN` (Amiga targets): handy when you only use one.
4. The compiler on PATH (`m68k-amigaos-gcc`, `ppc-amigaos-gcc`, `ppc-morphos-gcc`, `x86_64-aros-gcc`, `i386-aros-gcc`, `gcc`).

`--docker` and `--toolchain` together are an error; `--docker` ignores the variables. The compiler must report the same
version as the pinned image's (`-dumpversion`: m68k 6.5.0b, os4 11.5.0, morphos 15.2.0, aros 9.1.0, aros-i386 6.5.0), or the
build stops with a message saying which version it found; `--any-cc-version` builds anyway (not tested). A missing folder or
compiler gives an error that names the folder and the setting it came from. AROS also needs the SDK sysroot: by default the
`--toolchain` folder itself (aros) or its `sdk` folder (aros-i386), as in the images; `--sysroot <dir>` or
`ESHARP_SYSROOT_AROS` / `ESHARP_SYSROOT_AROS_I386` give another. The compiler runs in the output folder with the same command
line as in Docker, so the same toolchain gives the same bytes. That folder is also the compiler's current directory, as it is
in Docker (`/work`): a relative path in `--cflags` (`-I include`, `-L lib`) is taken from the output folder (`obj/<target>`
by default), not from the folder you ran `esharp` in. Give such paths as absolute ones.

Setting one up, per platform ([design/TOOLCHAINS.md](design/TOOLCHAINS.md) has the options, versions, licences and what is
verified):

- **Linux, m68k (verified):** copy the toolchain out of the pinned image into any folder. Docker is needed once; the result
  is the image's own compiler, so builds are byte-identical to Docker builds.

  ```bash
  img=amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859
  mkdir -p ~/amiga && c=$(docker create "$img") && docker cp "$c:/opt/m68k-amigaos" ~/amiga/m68k-amigaos && docker rm "$c"
  export ESHARP_TOOLCHAIN_M68K=~/amiga/m68k-amigaos      # or pass --toolchain ~/amiga/m68k-amigaos
  esharp build samples/hello                               # Compiler .../bin/m68k-amigaos-gcc (from ESHARP_TOOLCHAIN_M68K)
  esharp run samples/hello --headless --kickstart <rom>
  ```

  The copied binaries need glibc 2.38 or later (Ubuntu 24.04 and newer). Building the toolchain yourself
  (AmigaPorts m68k-amigaos-gcc) works the same way if its `-dumpversion` is 6.5.0b (not verified).
- **macOS and Windows, m68k (not verified):** build AmigaPorts m68k-amigaos-gcc (Homebrew on macOS, MSYS2 on Windows; or the
  Linux route inside WSL2) and pass its install prefix. The Linux copies above do not run there.
- **os4, morphos, aros, aros-i386 (not verified):** use Docker. Copies of these toolchains out of the images only work at
  the image's own path (`/opt/ppc-amigaos` and so on): their include and linker paths are built in.

`esharp doctor` (offline, read-only, exit code 0) prints, per target, whether its Docker image is present and which local
compiler it would use, with the version and where the setting came from, then FS-UAE, Xvfb, a Kickstart, the OS4 VM, the
hosted AROS images and the MUI cache. `esharp doctor --target m68k --toolchain <dir>` checks one folder.

In VS Code, the tasks "E#: build (m68k, local toolchain)" and "E#: run in FS-UAE (local toolchain)" from `esharp new` pass
`--toolchain ${config:esharp.toolchain}`. VS Code stops such a task when the setting is defined nowhere, so `esharp new` writes
`.vscode/settings.json` with `"esharp.toolchain": ""`. Put the folder there (`"esharp.toolchain": "<dir>"`), or leave it
empty and use `ESHARP_TOOLCHAIN_M68K` / `ESHARP_TOOLCHAIN` (VS Code must be started with them in its environment) or PATH.
The workspace value wins over your user `settings.json`: to use a user-wide setting instead, delete the line from the
workspace file. The default build task stays the Docker one.

AROS needs a hosted AROS to run: `docker build -t esharp-aros-hosted docker/aros-hosted` (downloads a pinned AROS nightly and
checks its sha256, [docker/aros-hosted](../docker/aros-hosted/README.md)), and
`docker build --build-arg AROS_ARCH=i386 -t esharp-aros-hosted-i386 docker/aros-hosted` for i386.

## Running and testing

```bash
esharp run samples/hello --docker <m68k image> --headless --model A1200 --kickstart <rom>   # prints: Hello from E#!
esharp run samples/screen --docker <image> --headless --screenshot shot.png                 # the emulator screen after the program prints SHOT
esharp run samples/cards --docker <image> --headless --input "click 400,300;key 9"           # scripted mouse and keys
esharp test samples/language                                  # on the host: ok/FAIL lines, non-zero exit on FAIL
esharp test samples/objects --target m68k --docker <image> --kickstart <rom>   # host and FS-UAE, compared
esharp test samples/linq --target os4 --docker esharp-amiga-toolchain          # host and the AmigaOS 4.1 VM (ESHARP_OS4_VM)
esharp test samples/language --target sdl --docker esharp-sdl                  # host and SDL (headless in the image)
esharp test samples/language --target aros --docker esharp-amiga-toolchain     # host and hosted AROS x86-64 (esharp-aros-hosted image)
esharp test samples/language --target aros-i386 --docker esharp-aros-i386      # host and hosted AROS i386 (esharp-aros-hosted-i386 image)
esharp test samples/retcode --target m68k --docker <image> --args TARGETRC=5   # fails on purpose: the target returns 5, the host 0
esharp run samples/cards --target sdl --docker esharp-sdl                      # a window on this desktop (no --headless)
tools/aros-hosted/aros-hosted.sh cards --docker esharp-aros-hosted --shot shot.png --input "click 400,300"   # hosted AROS (needs a --target aros build)
```

- `--args "SEED=42 auto"` gives the same argument line to the program (`Args.Count`, `Args.Get`, `Args.Value`); `esharp test`
  passes it to both the host and the target run.
- `esharp test` also shows the target program's return code (m68k, os4 and AROS: the shell's `RC` after the program; sdl: the
  exit code) and fails when it is non-zero while the host returned 0. On hosted AROS a `Trap` line in AROS's debug output is a
  failure too. `--target aros` and `aros-i386` boot hosted AROS headless in the `esharp-aros-hosted` / `esharp-aros-hosted-i386`
  image (built from `docker/aros-hosted` in the repository; `--aros-image <image>` names another); `--docker` is the build's
  toolchain image as usual. `--target morphos` says there is no runner yet.
- `--input` scripts: `click X,Y;key 9;key 50+38` with display pixels and X11 key codes (`+` holds keys; needs libXtst),
  `rel DX,DY` moves, `press` clicks where the pointer is, `down`/`up` hold and release (a drag). On `os4` the QEMU monitor sends
  the input and `key N` is an Amiga raw code; on `sdl` the program replays the script itself, with X,Y in its own screen pixels.
- `--serial <file>` with `--trace` collects the Amiga serial port (`Trace`, `Trace.Where`); `esharp addr` maps a crash address or a
  `Trace.Where` line back to C# (see [Debugging](#debugging)).
- Runs are unattended; `--timeout <sec>` (default 120) is the limit, and AROS needs about 20 s to boot.

## Emulators and VMs

**FS-UAE (m68k).** `esharp run` uses FS-UAE's built-in AROS ROM replacement unless given a real Kickstart (`--kickstart <rom>` or
`ESHARP_KICKSTART`; `--fsuae` or `ESHARP_FSUAE` for the executable; on macOS pass
`--fsuae /Applications/FSUAE.app/Contents/MacOS/fs-uae`). `--headless` needs `xvfb`, and `--screenshot` needs `xwd`.
The tested configuration is Kickstart 3.1 (40.069) on `--model A1200` (the default). `--model A500`, `A500+` and `A600` are
below the minimum system and not in the matrix; Kickstart 3.0 runs the samples but is not in the matrix either.
**Bring your own ROM.** No Kickstart ROM, Workbench disk or other copyrighted Amiga file is in this repository or the
packages, and none will be: use a ROM you own, keep it outside the repository and never commit it (`*.rom` is git-ignored).
FS-UAE cannot run the PowerPC builds.

**AmigaOS 4.1 (QEMU).** `--target os4` runs in an AmigaOS 4.1 pegasos2 VM that you provide (`ESHARP_OS4_VM`, helpers:
`esharp os4vm check|start|install-hook|remove-hook`, [tools/os4vm/README.md](../tools/os4vm/README.md)). Every run is a
fresh boot on a throwaway overlay (about 10 to 15 s); never boot `amiga.img` by hand while a run is going. The VM and its
AmigaOS files are yours and stay in their own directory.

**Hosted AROS.** `tools/aros-hosted/aros-hosted.sh <sample> --docker esharp-aros-hosted [--shot f] [--input ...] [--wait n]
[--log]` runs an `aros` (or, with `--arch i386`, `aros-i386`) build on hosted AROS under Xvfb, on a fresh copy of the image's
AROS tree per run. AROS is open source; this is the one place where no AmigaOS file is needed.

**SDL.** `--target sdl` builds for a Linux x86-64 desktop with SDL 3 linked statically (in the `esharp-sdl` image) and runs it
headless (SDL's offscreen video and dummy audio) or in a window; `ESHARP_SCALE=n` sets the window scale. macOS and Windows
SDL builds are not verified.

**Real hardware.** Nothing has run on a real Amiga yet. `tools/hwpack/build.sh <m68k image>` builds a test pack
(`obj/hwtest`, optionally floppy images; `tools/hwpack/README.txt`), and `tools/hwpack/run-emulated.sh` runs the whole pack in
FS-UAE. Reports from real machines are wanted: [issue #100](https://github.com/GameGyrafe/amigaesharp/issues/100).

Arrival-day checklist (a new Amiga 1200 or THEA1200; the full text is `README.txt` in the pack):

1. Build the pack at the version you will test: `tools/hwpack/build.sh <m68k image>`. Keep that checkout and its `samples/*/obj`
   (the crash-address map needs the exes of the same build). Copy `obj/hwtest` to the hard disk, or `obj/hwtest.adf` to a floppy
   (it leaves out `mui`, `muiclass`, `guiclass`, `wbstart`, `rummy` and `cards`: no room).
2. Shell, `CD` into the drawer, `Execute RunAll` (about 3 to 5 minutes). Every program gets a line in `SUMMARY.txt`; an arrow marks a
   FAIL line or a count that differs from the emulator's. `mui` and `muiclass` need MUI installed by you and are skipped without it.
3. By eye, by hand: `gui`, `guiclass`, `boopsi WAIT`, `keys`, the `asl` requester, `wbstart` by double-click (Arguments 2, SEED 42, AUTO yes,
   Stack 32768), `mui WAIT` and `muiclass WAIT` if MUI is installed; look at `screen`, `aga`, `assets`, `dbuffer` and listen to `sound`.
4. Send back `SUMMARY.txt`, the `results` drawer, the `BUILD` file, the machine details (README section 4d) and the by-eye answers, as a
   "Hardware report" issue.
5. A crash: note the guru number and `results/PROGRESS.txt`, reset, `Execute RunAll RESUME`. With SegTracker, Enforcer or MuForce
   installed you get "hunk N offset X"; then `esharp addr samples/<program> N X --docker <m68k image>` gives the C# file and line
   ([DEBUGGING.md](DEBUGGING.md)).

## MUI

`samples/mui` and `samples/muicheck`. MUI is third-party shareware: nothing of it is in the repository or the packages, and the
MUI facts in E# (class names, attribute numbers, message layouts, vectors) come from the AROS Zune headers (AROS Public
License) and fd file only. `Mui.Open()` opens muimaster.library version 19 or newer and returns false when MUI is missing
(the sample then prints `skip: MUI not installed`). The wrappers (`MuiApplication`, `MuiWindow`, `MuiGroup`, `MuiText`,
`MuiButton`, `MuiCheckMark`, `MuiString`, `MuiCycle`, `MuiSlider`, `MuiGauge`, `MuiRectangle`, `MuiListview`, ...) sit on the
BOOPSI core and send notifications to C# lambdas.

A MUI class of your own is a C# class derived from `MuiArea` (Area.mui) or `MuiCustomGroup` (Group.mui) that overrides what MUI calls
(`samples/muiclass`):

```csharp
sealed unsafe class Meter : MuiArea
{
    public static IntAttr Level => new IntAttr(0x8E5A0001);   // your own attribute: TAG_USER and a number of yours
    int _level;

    protected override void OnAskMinMax(ref MuiMinMax size) { size.MinWidth = 60; size.MinHeight = 12; size.DefWidth = 120; size.MaxWidth = MuiMinMax.Unlimited; }
    protected override void OnDraw(MuiRender r) { r.Fill(MuiPen.Fill, 0, 0, r.Width * _level / 100, r.Height); }
    protected override void OnSet(uint attr, nuint value) { if (attr == Level.Tag) { _level = (int)value; Redraw(); } }
    protected override bool OnGet(uint attr, out nuint value) { value = (nuint)_level; return attr == Level.Tag; }
}
...
slider.OnValueChanged(v => meter.Set(Meter.Level, v));
```

`OnAskMinMax` sizes are added to the superclass's (frame, spacing). `OnDraw` coordinates are relative to the contents box; `Fill` is
clipped to the object's box, `Line` moves both of its ends into the box (a diagonal line that leaves it comes out at another angle), and
`Text` is not clipped at all (keep it short) and leaves the object's font and JAM1 set in MUI's rast port. `OnSetup`/`OnCleanup` and
`OnMethod` (your own methods, sent with `DoMethod`) are there too. `OnSet` also sees the starting attributes, before the wrapper has its
MUI object: `Set`, `Get` and `Redraw` on the object itself do nothing at that point. MUI calls these on the program's own task, so they
may allocate and print. One MUI class per superclass serves every such C# class; it is made with the first object and deleted after the
last.

For mouse and key input the class names the IDCMP classes it wants and overrides `OnEvent` (MUIM_HandleEvent; MUI's event handler node
is added to the window at setup and removed at cleanup). The mouse position is relative to the contents box; returning true eats the
event, so no other handler sees it. `WantEvents` may be called again at any time, also from `OnEvent`: ask for mouse moves only while a
drag goes on, as each move in the window is otherwise a call:

```csharp
public Meter() { WantEvents(MuiEvent.MouseButtons); }

protected override bool OnEvent(MuiEvent e)
{
    if (e.Class == MuiEvent.MouseButtons && e.Code == MuiEvent.SelectDown && e.Inside)
    {
        _drag = true;
        WantEvents(MuiEvent.MouseButtons | MuiEvent.MouseMove);
        Set(Level, e.MouseX * 100 / e.Width);
        return true;
    }
    if (e.Class == MuiEvent.MouseButtons && e.Code == MuiEvent.SelectUp && _drag) { _drag = false; WantEvents(MuiEvent.MouseButtons); return true; }
    if (e.Class == MuiEvent.MouseMove && _drag) { Set(Level, e.MouseX * 100 / e.Width); return true; }
    return false;
}
```

Every wanted event in the window arrives while the object is shown, wherever the mouse is: check `e.Inside`. On the host,
`HostEvent(class, code, qualifier, x, y)` sends one to a shown object, for tests.

In FS-UAE (m68k only) `--mui 3.8|5` mounts MUI from a per-user cache and `--workbench <Workbench 3.x disk 2 .adf>` (your own
disk, mounted read-only for `C:` and the system libraries; or `ESHARP_MUI`, `ESHARP_WORKBENCH`):

```bash
tools/mui/fetch-mui.sh 3.8        # downloads and checks the official archive into a per-user cache (tool package: <esharp folder>/mui/fetch-mui.sh)
esharp test samples/mui --target m68k --docker <image> --model A1200 --kickstart <rom> --mui 3.8 --workbench <disk 2 .adf>
```

On AmigaOS 4.1 with MUI 5 installed use `--target os4`; on hosted AROS use `tools/aros-hosted/aros-hosted.sh mui`
(Zune, AROS's MUI). What was verified where, and what was not, is in docs/STATUS.md ("MUI wrappers", "Classic MUI in the
emulator"); see [tools/mui/README.md](../tools/mui/README.md).

## Debugging

Logic is easiest to debug on the host: the template's `launch.json` runs the program under the coreclr debugger. On the Amiga
side there is a serial `Trace` (`--trace`, `--serial <file>`; `Trace.Where("label")` writes where the call is as a hunk and
offset), `esharp addr <exe | dir> [<where>]` to turn a crash address into a C# file and line or a data address into the
nearest static field (m68k: hunk and offset, any number of hunks, chip ones too; AROS: the alert's segment and offset; AmigaOS 4:
the Grim Reaper's `Function()+0x…`; every build writes `obj/<target>/<name>.map` and `.o` (m68k) or `.debug` (ELF) for it), and
`--debug` for bounds-checked arrays. A source-level debugger for the Amiga build is open (issue #14). See [DEBUGGING.md](DEBUGGING.md).

## esharp commands

```
esharp new <name> [--source [dir] | --package [version]]   new hello-world project (csproj, .vscode)
esharp build [path] [options]      check the subset, emit C, run the target C compiler
esharp run [path] [options]        build and run (m68k in FS-UAE, os4 in the VM, sdl on the desktop)
esharp test [path] [options]       run on the host; with --target m68k|os4|aros|aros-i386|sdl also on the target, and compare
esharp assets [dir|assets.json] [--force]   PNG, ILBM and WAV to Assets.g.cs
esharp addr <exe | dir> [<where>] [--target <t>] [--docker [image] | --toolchain <dir>]   crash address to C# file:line
esharp os4vm check|start|install-hook|remove-hook   AmigaOS 4.1 QEMU VM helpers
esharp doctor [--target <name> [--toolchain <dir>]]   what builds and runs here: Docker images, local toolchains, emulators
esharp --version
```

`esharp` with no arguments, or `esharp --help`, prints every option. Other tools in the repository (run from a checkout with
`dotnet run --project tools/<name> --`):

| Tool | Use |
|---|---|
| `tools/BindGen` | C# bindings from `.fd` files and prototypes (`gen`), comparison with `src/ESharp.Platform` (`check`, `compare`), struct layouts (`layout`, `layouts.sh`) |
| `tools/CardArt`, `tools/AssetSamples`, `tools/PackageIcon` | generate the card sprite sheet, the asset sample inputs and the package icon (original art, drawn in code) |
| `tools/hwpack` | the real-hardware test pack and its emulated runner |
| `tools/aros-hosted` | run an AROS build on hosted AROS (in the image or from an extracted nightly with `AROS_HOSTED=<dir>`) |
| `tools/mui` | fetch classic MUI into a per-user cache |
| `tools/os4vm` | the AmigaOS 4.1 VM helpers |
| `tools/package-smoke.sh` | install the packed tool from a folder feed and exercise it |
| `tools/local-toolchain/compare.sh` | copy the m68k toolchain out of the pinned image, build samples with `--toolchain` and with `--docker`, compare the bytes (also in CI) |
