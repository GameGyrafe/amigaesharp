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

## Status

Phases 1 to 3 and most of phase 4 (a card demo) pass on the emulator; nothing has run on real hardware yet. See [docs/STATUS.md](docs/STATUS.md) for what works today and
[docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) for the design and roadmap.

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
`extensions.json`. The ES0001-ES0010 subset errors (and ES0101, a library call newer than the target's libraries; opt in with `[RequiresLib("lib", version)]`) show as you type and in `dotnet build`. The csproj and the tasks
point at the E# checkout the CLI was built from (found from the CLI's location, or `ESHARP_ROOT`); edit `ESharpRoot`
in the csproj if you move it.

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
