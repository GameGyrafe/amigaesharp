# Contributing to E#

Thanks for your interest. E# compiles a restricted subset of C# to C and builds it into native Amiga code (68020+, with AROS as a second family). Everything useful starts with reading
[README.md](README.md), [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) (design and roadmap) and [docs/STATUS.md](docs/STATUS.md) (what works, what is verified, what is not).
Work is tracked in the [issues](https://github.com/GameGyrafe/amigaesharp/issues) (v1 tracker #6, v2 epic #5); issues labelled `good first issue` are a good place to start once some are marked.

## Ground rules

These matter more than style:

- **Never copy code from Amiga E.** Its compiler source is GPL. E# takes design inspiration only. Do not paste, port or closely paraphrase code from Amiga E, PortablE, E-VO or other GPL/LGPL projects, and avoid GPL/LGPL in anything that gets linked into games.
- **No copyrighted Amiga files.** Kickstart ROMs, Workbench disks and similar files must never be committed, attached to issues or shared in the repo (`*.rom` is git-ignored). Bring your own Kickstart; read it in place. The same goes for the Amiga NDK (headers, autodocs, `.fd` files): download it yourself from its official source and point tools at your copy. Do not commit it or text copied from it (see issue #18).
- **No third-party game content.** Code, art, sound and rules text from other games must not be added. Samples use original art and standard rules written from scratch.
- **Emitter gaps are errors, not guesses.** If the compiler cannot translate something it reports `ES0099`. Never emit something that might be wrong. Prefer adding the feature over silencing the diagnostic.
- **Test the behaviour.** A new emitter feature needs a test that compiles the generated C and runs it (see `tests/ESharp.Compiler.Tests/CEmitterTests.cs`), not only a check of the C text.
- **Keep `docs/STATUS.md` honest.** If you did not run it, write "not verified". Say what you ran it on (emulator, which Kickstart, which AROS build, real hardware).
- **Minimum system:** an Amiga 1200 (68020, AGA, 2 MB chip RAM, Kickstart 3.1, hard disk). Do not add support for A500/A600, the 68000 or Kickstart 1.x/2.x. Nothing in the front end or platform contracts may assume a single target.

## Building and testing

You need the .NET 10 SDK (`global.json`). Common commands are in [AGENTS.md](AGENTS.md):

```bash
dotnet test                                              # unit tests, including generated C built and run with host gcc
dotnet run --project src/ESharp.Cli -- build samples/hello --emit-only
```

Building for the Amiga and running in an emulator needs Docker (the toolchain images are pinned by digest) and FS-UAE; see the README. Update a pinned toolchain deliberately and re-run `docker/smoke-test.sh`.

## Pull requests

1. Fork or branch from `main` and keep the change focused. Do not commit `obj/`, `bin/` or generated output that the build recreates.
2. Run `dotnet test`. For platform or emitter changes, also run the affected samples on the emulator (Kickstart 3.1 on `--model A1200`) and say what you ran.
3. Update `docs/STATUS.md` if what works or what is verified changed.
4. Open a pull request against `main` and fill in the template.

By contributing you agree that your contribution is licensed under the project's [MIT licence](LICENSE).

## Testing on real hardware

Real Amiga hardware is rare, and reports from people who have it are valuable. Build the test pack (`tools/hwpack/build.sh`, instructions in `tools/hwpack/README.txt`) and use the "Hardware test report" issue template: say which machine, Kickstart/Workbench version, CPU and RAM, and what you ran. Mark emulated or FPGA machines as such.

## Reporting problems

Use the issue templates for bugs and ideas. For a security problem, see [SECURITY.md](SECURITY.md). Be kind: see the [code of conduct](CODE_OF_CONDUCT.md).
