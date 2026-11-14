# E# (Amiga E#)

Write Amiga software in a restricted, statically compiled subset of C#. E# translates your
`.cs` files to C and builds them into native 68020+ code with `amiga-gcc`. The same source also
builds under plain `dotnet`, so game logic can be unit tested and debugged on the PC before it
ever touches an emulator.

```csharp
using ESharp;
using ESharp.Platform;

public static class Program
{
    [EntryPoint]
    public static void Main()
    {
        Dos.PutStr("Hello from E#!\n");
    }
}
```

## What it is, and is not

- **Not .NET on the Amiga.** No CLR, JIT, garbage collector or runtime IL. The output is plain native code.
- **Not a new language.** Normal `.cs` files, so Roslyn, IntelliSense and analyzers work unchanged.
  Extra meaning comes from attributes (`[EntryPoint]`, `[AmigaLib]`, `[ChipRam]`, ...) and a small intrinsics library.
- **Not all of C#.** A defined subset is accepted and the rest is rejected with clear diagnostics (`ES0001`...).

## Homage

The name and spirit come from **Wouter van Oortmerssen's Amiga E**, and from AMOS: a productive
language for a retro machine with fast edit-build-run cycles. E# is an unrelated project that
shares only that ambition. It reuses no code from Amiga E (whose compiler source is GPL); it is a
design inspiration only.

## Related projects

- [CopperSharp68k](https://github.com/ilehtoranta/CopperSharp68k) (MIT) takes the other route to C# on the Amiga:
  an ahead-of-time compiler from compiled .NET IL straight to 68k code, with a managed runtime (garbage
  collector, exceptions) and a growing part of the .NET 10 class library. Choose it if you want ordinary .NET code
  on a 68k Amiga. E# instead compiles a checked subset of C# source to C (no runtime), aims at games, and targets
  the A1200 (68020/AGA), AmigaOS 4 and MorphOS (PowerPC), AROS and an SDL desktop build from the same source.

## Status

Phases 1 to 3 and most of phase 4 (a card demo) pass on the emulator; nothing has run on real hardware yet. See [docs/STATUS.md](docs/STATUS.md) for what works today and
[docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) for the design and roadmap.

## Installing

E# is on nuget.org as a .NET tool ([AmigaESharp.Tool](https://www.nuget.org/packages/AmigaESharp.Tool)), with the
[AmigaESharp.Sdk](https://www.nuget.org/packages/AmigaESharp.Sdk) package for projects; every change merged into main
publishes a new `0.1.x` release:

```bash
dotnet tool install -g AmigaESharp.Tool      # the esharp command
esharp new mygame && cd mygame
dotnet run                                   # on the PC: Hello from E#!
esharp test                                  # the ok/FAIL checks on the PC
esharp build --target m68k --docker amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859
```

`esharp new` writes a csproj that references **AmigaESharp.Sdk** (the runtime, the platform contracts with their host
fakes, and the subset analyzer) at the tool's own version. The installed tool needs no checkout for `new`, `build`,
`run`, `test`, `assets`, `addr` and `os4vm`. A checkout is still needed to build the `esharp-amiga-toolchain` and
`esharp-sdl` images (the pinned m68k image above works without one), and for the scripts in `tools/` other than the
os4 VM helper. To try a change before it is released, pack it and install from a local folder feed:

```bash
dotnet pack ESharp.sln -c Release -o artifacts/packages -p:Version=0.1.0-local.1
tools/package-smoke.sh artifacts/packages 0.1.0-local.1      # installs into a temporary tool path and tests a new project
```

## Building

Requires the .NET 10 SDK.

```bash
dotnet test
dotnet run --project src/ESharp.Cli -- build samples/hello --emit-only
```

To build for the Amiga, use the pinned toolchain image (68000, AmigaOS 4 PowerPC and MorphOS PowerPC):

```bash
docker build -t esharp-amiga-toolchain docker
dotnet run --project src/ESharp.Cli -- build samples/hello --target m68k --docker   # or os4, morphos
```

Run it on an emulated Amiga (needs `fs-uae`; add `--headless` for no window, which also needs `xvfb`):

```bash
dotnet run --project src/ESharp.Cli -- run samples/hello --docker --headless   # prints: Hello from E#!
```

`run` uses FS-UAE's built-in AROS ROM replacement unless you pass `--kickstart <rom>` (or set
`ESHARP_KICKSTART`). Use a ROM you own; ROMs are git-ignored and must never be committed. On macOS
pass `--fsuae /Applications/FSUAE.app/Contents/MacOS/fs-uae` (or set `ESHARP_FSUAE`).

The same game also builds for a desktop through SDL 3 (`--target sdl`, Linux x86-64 in the `esharp-sdl`
image, SDL linked statically):

```bash
docker build -t esharp-sdl docker/sdl
dotnet run --project src/ESharp.Cli -- run samples/cards --target sdl --docker esharp-sdl   # a window on this desktop
dotnet run --project src/ESharp.Cli -- test samples/cards --target sdl --docker esharp-sdl  # headless: host and SDL agree
```

| Target | Symbols | Runs on | Status |
|---|---|---|---|
| `m68k` (default) | `AMIGA M68K AMIGAOS3` | A1200 / Kickstart 3.1+ (FS-UAE) | runs |
| `os4` | `AMIGA PPC AMIGAOS4` | AmigaOS 4.1 FE PowerPC (QEMU pegasos2 VM you provide; `tools/os4vm/README.md`) | runs in the VM (`esharp run/test --target os4`); real hardware not tried |
| `morphos` | `AMIGA PPC MORPHOS` | MorphOS PowerPC | builds only |
| `aros` | `AMIGA X86_64 AROS` | AROS x86-64 (hosted) | runs |
| `sdl` | `SDL` | Linux desktop through SDL 3 | runs (headless and under Xvfb; macOS/Windows not tried) |

The Amiga-only APIs (GadTools `Gui`, `Asl`, `Iff`, `Fonts`, `Joystick`) do not exist on `sdl` (error ES0102).

### Editing in VS Code

`esharp new <name>` (or a path such as `games/demo`; the last segment is the project name and must be a valid C# identifier) writes a project you can open in VS Code (with the C# extension): `Program.cs`, a `<name>.csproj`
that builds the same source under .NET against the E# runtime and loads the subset analyzer, and a `.vscode/` folder
with `tasks.json` (build for m68k, run in FS-UAE, run on the host, check the subset), `launch.json` (debug the host run
with the coreclr debugger, so logic can be stepped through; the Amiga build cannot be debugged yet) and
`extensions.json`. The ES0001-ES0010 subset errors show as you type and in `dotnet build`. ES0101 (a library call newer than the target's libraries; opt in with `[RequiresLib("lib", version)]`) is reported only by `esharp build`/`run`/`test` for the m68k and aros targets, not in the IDE or the host build. With the installed tool
the csproj references the AmigaESharp.Sdk package and the tasks call `esharp`. When the CLI runs from a checkout
(`dotnet run --project src/ESharp.Cli -- new ...`), or with `esharp new <name> --source [dir]`, the csproj and the tasks
point at that checkout instead (found from the CLI's location, or `ESHARP_ROOT`; edit `ESharpRoot` in the csproj if you
move it); `--package [version]` forces the package reference.

Without Docker, install the compiler yourself and drop `--docker` (`--cc` and `--cflags` override it).

## Layout

```
src/
  ESharp.Compiler/     Roslyn front end, lowering, C emitter
  ESharp.Analyzers/    subset rules (shared by the IDE and the compiler)
  ESharp.Cli/          esharp new | build | run | test | assets
  ESharp.Runtime/      attributes and intrinsics
  ESharp.Platform/     Video, Audio, Input, Storage, Timing, Dos contracts
  ESharp.Target.Host/  managed fakes for tests
  ESharp.Sdk/          packs AmigaESharp.Sdk (host assemblies + analyzer); ESharp.Cli packs AmigaESharp.Tool
docker/                pinned Amiga toolchain image and smoke test
samples/               hello, ...
tests/
docs/
```

## Requirements on the Amiga

**Minimum system: an Amiga 1200** (68020, AGA, 2 MB chip RAM, Kickstart/AmigaOS 3.1, installed on a hard disk). Older machines (A500, A600, Kickstart 1.x/2.x) are not supported. Planned targets beyond classic 68k: PowerPC on AmigaOS 4 and MorphOS, RTG graphics cards, and AROS.

## Contributing

Contributions and hardware test reports are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md) (it includes the ground rules: no code from the GPL Amiga E, no Kickstart ROMs) and the [code of conduct](CODE_OF_CONDUCT.md).

## Licence

MIT. See [LICENSE](LICENSE).
