# AGENTS.md: working on E# (Amiga E#)

E# compiles a restricted subset of C# to C and builds it into native Amiga code (68020+, AmigaOS 4
PowerPC, MorphOS PowerPC). Same `.cs` source also runs under plain dotnet as a test host. Read
`README.md`, then `docs/PROJECT_CONTEXT.md` (design and roadmap) and `docs/STATUS.md` (what works
today, what is verified, what is not). Keep `docs/STATUS.md` honest: say "not verified" for anything
you have not actually run.

## Commands

```bash
dotnet test                                     # unit tests, incl. generated C built and run with host gcc
dotnet run --project src/ESharp.Cli -- build samples/hello --emit-only
dotnet run --project src/ESharp.Cli -- build samples/hello --target m68k --docker <image>   # also writes obj/m68k/hello.info (Workbench Tool icon; csproj properties EsStackSize, EsToolTypes, EsIcon; Amiga targets only, not --emit-only; src/ESharp.Assets/Icon*.cs, src/ESharp.Cli/IconStep.cs)
dotnet run --project src/ESharp.Cli -- build samples/hello --target m68k --docker <image>   # os4, morphos too
dotnet run --project src/ESharp.Cli -- run samples/hello --docker <image> --headless        # m68k in FS-UAE
dotnet run --project src/ESharp.Cli -- assets samples/screen                                # PNG -> Assets.g.cs   # also automatic in build/run when stale (mtime); --force rebuilds
dotnet run --project src/ESharp.Cli -- run samples/screen --docker <image> --headless --screenshot shot.png
dotnet run --project src/ESharp.Cli -- run samples/cards --docker <image> --headless --input "click 400,300;rel -80,50;press;key 9"   # scripted mouse/keys (XTEST)
dotnet run --project src/ESharp.Cli -- run samples/rummy --docker <image> --headless --input "click 400,300;rel 16,-80;press;key 9"   # rummy: stock at (128,80)
dotnet run --project src/ESharp.Cli -- run samples/aga --docker <image> --headless --model A1200 --screenshot shot.png   # AGA 256 colours (a non-AGA machine would run the fallback checks)
dotnet run --project src/ESharp.Cli -- run samples/assets --docker <image> --headless --model A1200 --screenshot shot.png   # dithered gradients, an ILBM picture and an embedded WAV from assets.json (inputs made by tools/AssetSamples)
dotnet run --project src/ESharp.Cli -- run samples/rtg --docker <image> --headless --model A1200   # RTG: no card in the emulator, so this runs the fallback checks
dotnet run --project src/ESharp.Cli -- run samples/inherit --docker <image> --headless --model A1200   # inheritance, virtual members, is/as
dotnet run --project src/ESharp.Cli -- build samples/linq --target aros --docker amigadev/crosstools@sha256:9c4e978301da6b6584d68d4014caa1fa9e2689529e79e71f7190d40d1cb62e50   # AROS x86-64: builds; the pure samples run on hosted AROS (see docs/STATUS.md)
docker build -t esharp-aros-hosted docker/aros-hosted && docker/aros-hosted/smoke-test.sh   # hosted AROS x86-64 nightly (pinned by date and sha256) + Xvfb in an image; --build-context archive=<folder> builds from a local archive (docker/aros-hosted/README.md)
tools/aros-hosted/aros-hosted.sh cards --docker esharp-aros-hosted --shot shot.png --input "click 400,300"   # same, in the image: no AROS tree on the host, a fresh copy of the tree per run (needs a --target aros build)
AROS_HOSTED=<extracted hosted AROS dir> tools/aros-hosted/aros-hosted.sh cards --shot shot.png --input "click 400,300"   # run a --target aros sample on hosted AROS under Xvfb (needs the nightly archive; re-run on the 20261106 nightly: cards passes with a screenshot and a scripted click; the 20261104 nightly has been rotated out)
dotnet run --project src/ESharp.Cli -- run samples/controls --docker <image> --headless --model A1200 --kickstart <rom>   # timer E clock and lowlevel joystick (no Libs: there, so the no-lowlevel path; see docs/STATUS.md)
dotnet run --project src/ESharp.Cli -- run samples/keys --docker <image> --headless --model A1200 --input "click 400,300;key 50+38"   # keymap: raw keys to characters (Shift+A types 65)
dotnet run --project src/ESharp.Cli -- run samples/dates --docker <image> --headless --model A1200   # utility: dates, case-insensitive text
dotnet run --project src/ESharp.Cli -- run samples/fonts --docker <image> --headless --model A1200   # fonts: topaz 8; disk fonts only with diskfont.library in LIBS: (and FONTS: assigned); see docs/STATUS.md for the Workbench-disk startup
dotnet run --project src/ESharp.Cli -- run samples/iff --docker <image> --headless --model A1200   # IFF write and read back in RAM: (needs iffparse.library in LIBS:; else it says so)
dotnet run --project src/ESharp.Cli -- run samples/asl --docker <image> --headless --model A1200   # asl.library: allocate/free; create RAM:asl-open first to show the file requester after SHOT, then --input clicks OK or Cancel
dotnet run --project src/ESharp.Cli -- run samples/names --docker <image> --headless --model A1200   # E# classes named Window, Node, List, ... and same-named types in two namespaces (prefixed C names)
dotnet run --project src/ESharp.Cli -- test samples/objects --target m68k --docker <image> --model A1200 --kickstart <rom>   # class array fields and initializers, Vec of objects, nested classes, interface ref/out and is/as, static initializers that call code
dotnet run --project src/ESharp.Cli -- test samples/objects --target m68k --docker <image> --model A1200 --kickstart <rom> --args "SEED=42 auto"   # the same argument line to the host and the target run (also --target sdl, os4)
dotnet run --project src/ESharp.Cli -- run samples/objects --docker <image> --headless --model A1200 --args "SEED=42 auto"   # program arguments (Args); also --target sdl
dotnet run --project src/ESharp.Cli -- run samples/textspeed --docker <image> --headless --model A1200 --kickstart <rom>   # Font.Draw speed (one BltTemplate per line) against one rectangle per run
dotnet run --project src/ESharp.Cli -- run samples/trace --trace --serial trace.txt --docker <image> --headless --model A1200   # serial Trace.Line/Value: FS-UAE writes the Amiga serial port into trace.txt, printed after the run (docs/DEBUGGING.md)
dotnet run --project src/ESharp.Cli -- run samples/timing --docker <image> --headless --model A1200   # frame timing: Frame.WaitVBlank (WaitTOF), PAL/NTSC rate, FrameTimer fixed steps; prints a note instead of failing if the emulator is not real time
dotnet run --project src/ESharp.Cli -- test samples/dbuffer --target m68k --docker <image> --model A1200 --kickstart <rom>   # double buffer: Video.EnableDoubleBuffer/Flip (screen buffers, offscreen-bitmap fallback); also --target sdl, --target os4; run --headless --screenshot shot.png for the last frame
dotnet run --project src/ESharp.Cli -- addr samples/trace 0 e4 --docker <image>   # crash address (hunk 0, hex offset) -> C# file:line, using obj/m68k/<name>.o that every m68k build writes (docs/DEBUGGING.md)
dotnet run --project src/ESharp.Cli -- run samples/muicheck --docker <image> --headless --model A1200 --kickstart <rom> --mui 3.8 --workbench <Workbench 3.x disk 2 .adf>   # classic MUI 3.8 or 5 in FS-UAE (m68k only; tools/mui/fetch-mui.sh <3.8|5> first: shareware, fetched per user into a cache, never committed or shipped; needs your own Workbench disk for C: and the libraries; also ESHARP_MUI, ESHARP_WORKBENCH, and test; tools/mui/README.md)
dotnet run --project src/ESharp.Cli -- run samples/lambdas --docker <image> --headless --model A1200   # capturing lambdas and delegates
dotnet run --project src/ESharp.Cli -- test samples/closures --target m68k --docker <image> --model A1200 --kickstart <rom>   # heap closures (stored, returned) and events, Heap.Free and the Heap.LiveClosures leak check; also --target sdl, os4
dotnet run --project src/ESharp.Cli -- run samples/linq --docker <image> --headless --model A1200   # LINQ over arrays, turned into loops
dotnet run --project src/ESharp.Cli -- test samples/interrupts --target m68k --docker <image> --model A1200 --kickstart <rom>   # [InlineAsm] (68020 and PowerPC code, C# fallback) and [Interrupt] VBlank servers (Interrupts.AddVBlank)
dotnet run --project src/ESharp.Cli -- run samples/refs --docker <image> --headless --model A1200   # ref locals and returns, foreach over Vec and struct enumerators
dotnet run --project src/ESharp.Cli -- test samples/patterns --target m68k --docker <image> --model A1200 --kickstart <rom>   # positional, property and list patterns, pattern switch statements
dotnet run --project src/ESharp.Cli -- run samples/gui --target os4 --docker esharp-amiga-toolchain --input "click 208,101;click 57,45"   # os4 (QEMU VM, ESHARP_OS4_VM): --input through the QEMU monitor; key N is an Amiga raw code there (docs/STATUS.md)
dotnet run --project src/ESharp.Cli -- test samples/controls                                # run on the host (ok/FAIL lines, non-zero exit on FAIL); add --target m68k --docker <image> [--kickstart <rom>] [--model A1200] to also run in FS-UAE and compare
dotnet run --project tools/CardArt -- samples/cards/cards.png                               # redraw the card sprite sheet
dotnet run --project tools/BindGen -- check <sdk include dir> src/ESharp.Platform   # compare our [AmigaLib] vectors/arg counts with an SDK you provide (AROS include dir; not committed)
dotnet run --project tools/BindGen -- gen <fd dir> exec dos graphics --include <include dir> --out <dir>   # C# bindings from fd files + prototypes (NDK or AROS form); add --docs, --namespace N
dotnet run --project tools/BindGen -- gen <fd dir> exec dos graphics intuition gadtools lowlevel keymap utility diskfont iffparse asl timer --include <include dir> --check src/ESharp.Platform   # hand-written vs generated: problems (exit 1) and notes
ESHARP_SDK=<fd dir> ESHARP_SDK_INCLUDE=<include dir> dotnet test --filter BindGen   # the SDK-backed BindGen test (skipped without ESHARP_SDK)
dotnet run --project src/ESharp.Cli -- run samples/gui --docker <image> --headless --model A1200 --timeout 60 --input "click 400,300;rel -96,-100;press;rel -72,-63;rel -28,-10;press"   # GadTools window on the Workbench screen: Hello button, then the close gadget (rel moves were found by screenshot, re-found after the exact-rel fix f76a167; add --screenshot and drop the last press to look)
dotnet run --project src/ESharp.Cli -- run samples/guiclass --docker <image> --headless --model A1200 --kickstart <rom> --timeout 60 --input "click 400,300;rel -96,-100;press;rel -72,-63;rel -28,-10;press"   # the same window as a class derived from GtWindow ([Extensible]) with overridden OnClicked/OnCloseRequest; also --target os4 --input "click 208,101;click 57,45"
dotnet run --project src/ESharp.Cli -- test samples/boopsi --target m68k --docker <image> --model A1200 --kickstart <rom>   # BOOPSI core: ROM classes, typed attributes, a lambda as a struct Hook ([HookEntry] entries per target), ownership; also --target os4; AROS: tools/aros-hosted/aros-hosted.sh boopsi --docker esharp-aros-hosted
dotnet run --project src/ESharp.Cli -- run samples/boopsi --args WAIT --docker <image> --headless --model A1200 --kickstart <rom> --timeout 90 --input "click 400,300;rel -140,-115;press;rel 120,46;press;rel -51,0;down;rel -60,0;up;rel 33,44;press;key 65;key 10;key 11;key 19;key 19;key 36;rel -92,-144;press"   # button, slider page and drag (down/up), typing through the edit hook, close; os4: --input "click 74,80;click 220,100;click 200,127;key 64;key 1;key 2;key 10;key 10;key 68;click 57,45"; hosted AROS: --args WAIT --input "click 75,57;click 220,84;click 47,37"
AROS_HOSTED=<extracted hosted AROS dir> tools/aros-hosted/aros-hosted.sh gui --shot shot.png --input "click 208,74;click 47,37"   # same sample on hosted AROS (needs a --target aros build)
ESHARP_OS4_VM=<AmigaOS 4.1 VM dir> dotnet run --project src/ESharp.Cli -- test samples/mui --target os4 --docker esharp-amiga-toolchain   # MUI (MUI 5 on OS4): application, window, gadgets, notifications to C# lambdas; without MUI (stock 3.1) it prints "skip: MUI not installed"
ESHARP_OS4_VM=<AmigaOS 4.1 VM dir> dotnet run --project src/ESharp.Cli -- run samples/mui --target os4 --docker esharp-amiga-toolchain --args WAIT --input "click 562,360;click 655,406;click 566,591;click 653,360"   # Hello, a slider page, a list line, Quit
tools/aros-hosted/aros-hosted.sh mui --docker esharp-aros-hosted --wait 0 --shot shot.png   # Zune (AROS's MUI); --args WAIT --input "click 300,163;click 400,189;click 297,298;click 393,163" for Hello, slider, list, Quit (needs a --target aros build)
dotnet run --project tools/BindGen -- check <dir with the AROS muimaster_lib.fd> src/ESharp.Platform muimaster   # MuiMaster vectors against the AROS fd (MUI facts come from the AROS Zune headers only)
ESHARP_OS4_VM=<AmigaOS 4.1 VM dir> dotnet run --project src/ESharp.Cli -- test samples/linq --target os4 --docker esharp-amiga-toolchain   # host vs AmigaOS 4.1 in QEMU pegasos2 (overlay boot, ~15 s; tools/os4vm/README.md)
ESHARP_OS4_VM=<AmigaOS 4.1 VM dir> dotnet run --project src/ESharp.Cli -- run samples/cards --target os4 --docker esharp-amiga-toolchain --screenshot shot.png   # screendump after SHOT; --trace --serial f for Trace lines; no --input yet
ESHARP_OS4_VM=<AmigaOS 4.1 VM dir> python3 -I tools/os4vm/os4vm.py check|start|install-hook|remove-hook   # VM helpers: start = VNC 127.0.0.1:5901 on an overlay; the hook is a one-time change to amiga.img (a one-time change to your own VM image)
dotnet run --project tools/BindGen -- compare <aros include dir> <fd dir> lowlevel asl   # AROS headers vs the m68k toolchain's fd files (ndk/lib/fd); fd versions are right for the classic OS
tools/BindGen/layouts.sh                                  # re-measure struct offsets (tools/BindGen/structs.txt) for m68k and AROS into src/ESharp.Platform/Layout.g.cs (docker)
tools/hwpack/build.sh <m68k image>                       # build the real-hardware test pack into obj/hwtest (run with Execute RunAll on the Amiga; README.txt in the pack)
tools/hwpack/run-emulated.sh obj/hwtest --rom <rom> --wb <Workbench 3.1 disk .adf> [--profile fast8]   # the whole pack in FS-UAE (A1200, accuracy 1, 2 MB chip), prints SUMMARY.txt
tools/hwpack/build.sh <m68k image> [--boot-wb <Workbench 3.x disk 2 .adf>] [--adf-only]   # also obj/hwtest.adf (one 880 KB floppy, our files only); --boot-wb adds obj/hwtest-boot.adf with a few AmigaOS files from YOUR disk (own use only, never share)
tools/hwpack/run-emulated.sh --floppy obj/hwtest.adf --rom <rom> --wb <Workbench 3.1 disk .adf>   # floppy mode: stock A1200, no hard disk, DF0 Workbench copy + DF1 pack disk; --boot-floppy obj/hwtest-boot.adf runs the bootable disk alone
python3 -I tools/hwpack/test_mkadf.py                      # tests of tools/hwpack/mkadf.py (ADF writer and reader)
docker build -t esharp-amiga-toolchain docker && docker/smoke-test.sh    # combined toolchain image
docker build -t esharp-sdl docker/sdl && docker/sdl/smoke-test.sh   # gcc + static SDL 3.4.20 (Linux x86-64) for the sdl target; offscreen/dummy drivers
dotnet run --project src/ESharp.Cli -- run samples/cards --target sdl --docker esharp-sdl --headless --input "click 60,145;key 9" --screenshot shot.png   # sdl, headless in the image: --input X,Y are the game's screen pixels (keys are X11 codes, as for FS-UAE), replayed after SHOT; the shot is the game's own frame
dotnet run --project src/ESharp.Cli -- test samples/language --target sdl --docker esharp-sdl   # host vs SDL (headless in the image): same ok lines
dotnet run --project src/ESharp.Cli -- run samples/cards --target sdl --docker esharp-sdl   # no --headless: builds in the image, runs the static binary on this desktop (X11/Wayland); ESHARP_SCALE=n sets the window scale
dotnet pack ESharp.sln -c Release -o artifacts/packages -p:Version=0.1.0-local.1   # AmigaESharp.Tool + AmigaESharp.Sdk (one version, Directory.Build.props); never push them anywhere
tools/package-smoke.sh artifacts/packages 0.1.0-local.1 <m68k image>   # install from that folder feed into a temp tool path, esharp new, build/run/test, ES0001 from the packaged analyzer, m68k build
```

`<image>` is `esharp-amiga-toolchain` (all four targets: m68k, os4, morphos, aros) or, for m68k only,
`amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859`
(1.1 GB instead of ~5.7 GB for the four-target image). Toolchains are pinned by digest in `docker/Dockerfile`; update them
deliberately and re-run the smoke test.

Requires the .NET 10 SDK (`global.json`). `src/ESharp.Analyzers` stays on `netstandard2.0` so IDEs can load it.

## Layout and how the pieces fit

- `src/ESharp.Analyzers`: subset rules (`ES0001`-`ES0010`, `ES0100`, `ES0101`). One analyzer, used by the IDE and by the compiler. A program class may derive only from its own classes or from a library class marked `[Extensible]` (GtWindow); the rule must hold where the library is source (compiler) and where it is metadata (IDE).
- `src/ESharp.Compiler`: `EsCompiler.Compile` (parse, C# errors, analyzer, entry point check) then `CEmitter` (C output with `#line`). `Targets.cs` defines the CPU x OS target model. `CRuntime/es_std.h` (standard C) and `CRuntime/es_sdl.h` (SDL 3) are pasted into the generated C of the sdl target when a `[Native]` extern of `src/ESharp.Platform/Crt.cs` named `es_std_*` / `es_sdl_*` is called.
- `src/ESharp.Runtime` and `src/ESharp.Platform`: parsed together with user code. They are embedded in the compiler as source, built by dotnet with `HOST` defined, and parsed with `AMIGA` defined for Amiga targets. Target-specific code uses `#if`.
- `src/ESharp.Assets`: PNG reader/writer, colour reduction, planar packing and the `assets.json` -> `Assets.g.cs` generator (used by `esharp assets`), with an incremental `Sync` (run by `esharp build`/`run`/`test`; commit the `Assets.g.cs` it writes).
- `src/ESharp.Target.Host`: managed fakes for tests. Its assemblies (with Runtime and Platform) are copied to `host/` next to `esharp.dll`, which `esharp test` uses when no checkout is named (`ESHARP_ROOT`).
- Packages (on nuget.org since 0.1.1, 2026-11-14; `docs/STATUS.md` "NuGet packages"): `src/ESharp.Cli` packs the dotnet tool **AmigaESharp.Tool** (command `esharp`, with `host/` and `os4vm/`), `src/ESharp.Sdk` packs **AmigaESharp.Sdk** (Runtime, Platform, Target.Host in `lib/net10.0`, the analyzer in `analyzers/dotnet/cs`). `.github/workflows/release.yml` publishes both on every push to main (not for `*.md` or workflow-only changes) as `0.1.<run number>` through nuget.org trusted publishing (GitHub OIDC, environment `nuget` with secret `NUGET_USER`, no stored API key), then tags `vX.Y.Z` and makes a GitHub release; agents never push tags or publish by hand. Anything the CLI needs at run time must ship in the tool package, not be read from the repo layout.
- `src/ESharp.Cli`: `esharp new|build|run|test|assets|addr|os4vm`; `esharp new` also writes a host csproj (a PackageReference to AmigaESharp.Sdk at the tool's version, or ProjectReferences when the CLI runs from a checkout or with `--source`; analyzer wired in either way) and `.vscode/` tasks, launch and extensions (`ProjectTemplate.cs`, tested in `ProjectTemplateTests`).
- Emitter gaps are reported as `ES0099`, never miscompiled. Prefer adding the feature over silencing the diagnostic.

## Rules

- Minimum system is an Amiga 1200: 68020, AGA, 2 MB chip RAM, Kickstart/AmigaOS 3.1 (v40), installed on a hard disk (the maintainer's decision). Open libraries with version 39 or higher, except where the Kickstart/Workbench 3.0 copy is older and every call used exists in it (keymap.library is opened at 37: its 3.0 ROM copy is below 39); never below the highest `[Since]` of the calls used, and lowlevel.library (v40, from Workbench 3.1) stays optional. muimaster.library is third-party with its own numbers: opened at 19 (MUI 3.8) and optional; MUI facts (class names, IDs, values, message layouts, vectors) come only from the AROS Zune headers and fd, never from the MUI distribution. Kickstart 3.0 runs the samples in the emulator (docs/STATUS.md) but is not in the test matrix; 68020 instructions and AGA may be assumed on classic builds; no support for A500/A600, 68000 or Kickstart 1.x/2.x. Planned: PowerPC (AmigaOS 4, MorphOS), RTG cards, AROS.
- Symbols: `AMIGA` is defined for every Amiga-family target; `M68K`/`PPC` and `AMIGAOS3`/`AMIGAOS4`/`MORPHOS` narrow it. The desktop target `sdl` defines `SDL` (not `AMIGA`); the dotnet test host defines `HOST`. Platform code is `#if AMIGA` / `#elif SDL` / `#else` (host fake); Amiga-only classes (Gui, Asl, Iff, Fonts, Joystick and the raw library bindings) are left out on SDL, and using them is error ES0102. Nothing in the front end or platform contracts may assume a single target. The 68020 A1200 is the performance baseline (the 68000 no longer is).
- Test the behaviour, not just the text: new emitter features need a test that compiles the generated C and runs it (see `CEmitterTests`).
- Never commit Kickstart ROMs or other copyrighted Amiga files (`*.rom` is git-ignored). Never copy code from Amiga E (its compiler is GPL); design inspiration only. Avoid GPL/LGPL in anything linked into games.
- Licence is MIT, copyright Gyrafe Software. The README must keep crediting Wouter van Oortmerssen's Amiga E.
- Develop on the designated feature branch; do not push to `main` or open pull requests unless the maintainer asks.
- Generated output (`obj/`, `bin/`, `samples/*/obj`) is ignored; do not commit it.

## Running the emulator

`esharp run` uses FS-UAE's built-in AROS ROM replacement unless given a real Kickstart
(`--kickstart <rom>` or `ESHARP_KICKSTART`; `--fsuae` / `ESHARP_FSUAE` for the executable). FS-UAE
cannot run the PowerPC builds. `--target os4` runs in an AmigaOS 4.1 QEMU pegasos2 VM instead
(`ESHARP_OS4_VM`, `tools/os4vm/README.md`): every run is a fresh boot on a throwaway overlay, so never boot `amiga.img`
by hand while a run is going (one VM at a time; the CLI refuses then). The OS4 VM is the contributor's own and its AmigaOS files stay in its own directory: never copy
them into the repo or any output. MorphOS and PowerPC real hardware: not done yet.

## Dev host (Linux, possibly shared)

If the machine is shared with other people or agents running their own build and test gates: check free
memory before heavy work, run long jobs (full toolchain image builds, big test runs) in their own tmux
session, and do not install packages or change the host without asking its owner. Docker is assumed to run
without sudo. fs-uae and xvfb are needed for headless emulator runs. If this repo's persistent clone is the main
dev checkout, use scratch directories only for throwaway experiments.

Kickstart ROMs: the maintainer keeps theirs outside any repo, and this file deliberately does not give a
path. Ask the machine's owner for the location, or use `ESHARP_KICKSTART` if it is set. Read ROMs in place,
quote the path, never copy them into the repo, a branch, an artifact or a message. The test matrix
is Kickstart 3.1 (40.069) on `--model A1200` (default). Kickstart 2.04 / A500+ is no longer part of the matrix.

## Next up

See "Next" in `docs/STATUS.md` and the open GitHub issues (tracker #6, v2 epic #5). At the time of writing: the classic A1200 baseline (Kickstart 3.1 in FS-UAE), AmigaOS 4.1 in the QEMU VM, hosted AROS x86-64 and the `sdl` target all run the samples; the packages are on nuget.org. Open: real hardware (#7), MorphOS and PowerPC hardware runs, Windows (#11), debugging (#14), MUI wrappers on the BOOPSI core (#40; the core is done), and the v2 items. Kickstart 2.04 / A500+ is no longer part of the test matrix.
