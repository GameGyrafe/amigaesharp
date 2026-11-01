# AGENTS.md: working on E# (Amiga E#)

E# compiles a restricted subset of C# to C and builds it into native Amiga code (68000, AmigaOS 4
PowerPC, MorphOS PowerPC). Same `.cs` source also runs under plain dotnet as a test host. Read
`README.md`, then `docs/PROJECT_CONTEXT.md` (design and roadmap) and `docs/STATUS.md` (what works
today, what is verified, what is not). Keep `docs/STATUS.md` honest: say "not verified" for anything
you have not actually run.

## Commands

```bash
dotnet test                                     # unit tests, incl. generated C built and run with host gcc
dotnet run --project src/ESharp.Cli -- build samples/hello --emit-only
dotnet run --project src/ESharp.Cli -- build samples/hello --target m68k --docker <image>   # os4, morphos too
dotnet run --project src/ESharp.Cli -- run samples/hello --docker <image> --headless        # m68k in FS-UAE
docker build -t esharp-amiga-toolchain docker && docker/smoke-test.sh    # combined toolchain image
```

`<image>` is `esharp-amiga-toolchain` (all three targets) or, for m68k only,
`amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859`
(1.1 GB instead of ~5.7 GB). Toolchains are pinned by digest in `docker/Dockerfile`; update them
deliberately and re-run the smoke test.

Requires the .NET 10 SDK (`global.json`). `src/ESharp.Analyzers` stays on `netstandard2.0` so IDEs can load it.

## Layout and how the pieces fit

- `src/ESharp.Analyzers`: subset rules (`ES0001`-`ES0010`, `ES0100`). One analyzer, used by the IDE and by the compiler.
- `src/ESharp.Compiler`: `EsCompiler.Compile` (parse, C# errors, analyzer, entry point check) then `CEmitter` (C output with `#line`). `Targets.cs` defines the CPU x OS target model.
- `src/ESharp.Runtime` and `src/ESharp.Platform`: parsed together with user code. They are embedded in the compiler as source, built by dotnet with `HOST` defined, and parsed with `AMIGA` defined for Amiga targets. Target-specific code uses `#if`.
- `src/ESharp.Target.Host`: managed fakes for tests. `src/ESharp.Cli`: `esharp new|build|run`.
- Emitter gaps are reported as `ES0099`, never miscompiled. Prefer adding the feature over silencing the diagnostic.

## Rules

- Minimum OS for classic Amiga builds is Kickstart/AmigaOS 2.04 (v37); no Kickstart 1.x support. Open libraries with version 37 or higher.
- Symbols: `AMIGA` is defined for every Amiga-family target; `M68K`/`PPC` and `AMIGAOS3`/`AMIGAOS4`/`MORPHOS` narrow it. Nothing in the front end or platform contracts may assume a single target. 68000 is the performance baseline.
- Test the behaviour, not just the text: new emitter features need a test that compiles the generated C and runs it (see `CEmitterTests`).
- Never commit Kickstart ROMs or other copyrighted Amiga files (`*.rom` is git-ignored). Never copy code from Amiga E (its compiler is GPL); design inspiration only. Avoid GPL/LGPL in anything linked into games.
- Licence is MIT, copyright Gyrafe Software. The README must keep crediting Wouter van Oortmerssen's Amiga E.
- Develop on the designated feature branch; do not push to `main` or open pull requests unless Dave asks.
- Generated output (`obj/`, `bin/`, `samples/*/obj`) is ignored; do not commit it.

## Running the emulator

`esharp run` uses FS-UAE's built-in AROS ROM replacement unless given a real Kickstart
(`--kickstart <rom>` or `ESHARP_KICKSTART`; `--fsuae` / `ESHARP_FSUAE` for the executable). FS-UAE
cannot run the PowerPC builds; that needs QEMU or real hardware (not done yet).

## Dev host (Linux, shared)

Dave's Linux machine runs other agents' build and test gates, so: check free memory before heavy
work, run long jobs in their own tmux session, clone into a scratch directory, and do not install
packages or change the host without asking Dave. Docker runs without sudo. fs-uae and xvfb are
installed. Kickstart ROMs are not on the host unless Dave has put them there; do not go looking for
them, and never copy them into the repo.

## Next up

See "Next" in `docs/STATUS.md`. At the time of writing: phase 1 passes on the emulator with Kickstart 2.04 and 3.1 (hardware not yet run), so start phase 2:
arrays, fixed-point types and span views (the emitter currently reports `ES0099` for arrays).
