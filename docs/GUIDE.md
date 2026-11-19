# E# guide

The reference material behind the [README](../README.md): installing, the language subset, the platform APIs, assets,
icons, MUI, running in emulators and VMs, and the `esharp` commands. [STATUS.md](STATUS.md) is the source of truth for what
is verified (it says "not verified" where something was not run); this guide says how to use things.

Contents: [Install](#install-and-the-packages) - [Projects and VS Code](#projects-and-vs-code) - [The language subset](#the-language-subset) -
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
with `tasks.json` (build for m68k, run in FS-UAE, run on the host, check the subset), `launch.json` (debug the host run
with the coreclr debugger, so logic can be stepped through; the Amiga build cannot be debugged at source level yet) and
`extensions.json`. The ES0001-ES0010 subset errors show as you type and in `dotnet build`. ES0101 (a library call newer than
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
`morphos`, `aros`, `aros-i386`, `sdl`. The compilers live in pinned Docker images, so the build is `--docker <image>`; without
Docker, install the compiler yourself and drop `--docker` (`--cc` and `--cflags` override it).

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

Build options: `-o <dir>`, `--emit-only`, `--docker [image]`, `--cc`, `--cflags`, `--allow-float`, `--chip-budget <n>`,
`--debug` (bounds-checked arrays and spans, poisoned arena `Reset`; also defines `TRACE`) and `--trace` (keep `Trace` calls).
Symbols: `AMIGA` is defined for every Amiga-family target and `M68K`/`PPC`/`X86_64`/`I386` and
`AMIGAOS3`/`AMIGAOS4`/`MORPHOS`/`AROS` narrow it; `sdl` defines `SDL` only; the dotnet host defines `HOST`.

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
esharp run samples/cards --target sdl --docker esharp-sdl                      # a window on this desktop (no --headless)
tools/aros-hosted/aros-hosted.sh cards --docker esharp-aros-hosted --shot shot.png --input "click 400,300"   # hosted AROS (needs a --target aros build)
```

- `--args "SEED=42 auto"` gives the same argument line to the program (`Args.Count`, `Args.Get`, `Args.Value`); `esharp test`
  passes it to both the host and the target run.
- `--input` scripts: `click X,Y;key 9;key 50+38` with display pixels and X11 key codes (`+` holds keys; needs libXtst),
  `rel DX,DY` moves, `press` clicks where the pointer is, `down`/`up` hold and release (a drag). On `os4` the QEMU monitor sends
  the input and `key N` is an Amiga raw code; on `sdl` the program replays the script itself, with X,Y in its own screen pixels.
- `--serial <file>` with `--trace` collects the Amiga serial port (`Trace`); `esharp addr` maps a crash address back to C# (see
  [Debugging](#debugging)).
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

`OnAskMinMax` sizes are added to the superclass's (frame, spacing); `OnDraw` coordinates are relative to the contents box and clipped to
the object; `OnSetup`/`OnCleanup` and `OnMethod` (your own methods, sent with `DoMethod`) are there too. MUI calls them on the program's
own task, so they may allocate and print. One MUI class per superclass serves every such C# class; it is made with the first object and
deleted after the last. Mouse and key input in a custom class is not wrapped yet.

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
side there is a serial `Trace` (`--trace`, `--serial <file>`), `esharp addr <exe | dir> <hunk> <hex offset>` to turn a crash
address into a C# file and line (every m68k build writes `obj/m68k/<name>.o` for it), and `--debug` for bounds-checked
arrays. A source-level debugger for the Amiga build is open (issue #14). See [DEBUGGING.md](DEBUGGING.md).

## esharp commands

```
esharp new <name> [--source [dir] | --package [version]]   new hello-world project (csproj, .vscode)
esharp build [path] [options]      check the subset, emit C, run the target C compiler
esharp run [path] [options]        build and run (m68k in FS-UAE, os4 in the VM, sdl on the desktop)
esharp test [path] [options]       run on the host; with --target m68k|os4|sdl also on the target, and compare
esharp assets [dir|assets.json] [--force]   PNG, ILBM and WAV to Assets.g.cs
esharp addr <exe | dir> <hunk> <hex offset> [--docker [image]]   crash address to C# file:line
esharp os4vm check|start|install-hook|remove-hook   AmigaOS 4.1 QEMU VM helpers
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
