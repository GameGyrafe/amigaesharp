# E# (Amiga E#)

Write Amiga software in a restricted, statically compiled subset of C#. E# translates your
`.cs` files to C and builds them into native 68000 code with `amiga-gcc`. The same source also
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

Phase 1 (hello world) is under way. See [docs/STATUS.md](docs/STATUS.md) for what works today and
[docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) for the design and roadmap.

## Building

Requires the .NET 8 SDK.

```bash
dotnet test
dotnet run --project src/ESharp.Cli -- build samples/hello --emit-only
```

To build for the Amiga, install Bebbo's `amiga-gcc` and drop `--emit-only`
(`--cc` and `--cflags` override the compiler).

## Layout

```
src/
  ESharp.Compiler/     Roslyn front end, lowering, C emitter
  ESharp.Analyzers/    subset rules (shared by the IDE and the compiler)
  ESharp.Cli/          esharp new | build | run | test | assets
  ESharp.Runtime/      attributes and intrinsics
  ESharp.Platform/     Video, Audio, Input, Storage, Timing, Dos contracts
  ESharp.Target.Host/  managed fakes for tests
samples/               hello, ...
tests/
docs/
```

## Licence

MIT. See [LICENSE](LICENSE).
