# E# (Amiga E#)

Write Amiga software in a restricted, statically compiled subset of C#. E# translates your `.cs` files to C and builds
them into native code for the classic Amiga (68020+, Kickstart 3.1), AmigaOS 4 and MorphOS (PowerPC), AROS, and a
desktop build through SDL 3. The same source also builds under plain `dotnet`, so game logic can be unit tested and
debugged on the PC before it ever touches an emulator.

No CLR, JIT or garbage collector runs on the Amiga: the output is plain native code.

## Packages

- **AmigaESharp.Tool**: the `esharp` command (`new`, `build`, `run`, `test`, `assets`, `addr`, `os4vm`).
- **AmigaESharp.Sdk**: what a game's host project references: the E# runtime, the platform contracts and their managed
  host fakes (built with `HOST`), and the subset analyzer (errors ES0001-ES0010 in the editor and in `dotnet build`).
  `esharp new` adds it for you.

## Getting started

```bash
dotnet tool install -g AmigaESharp.Tool
esharp new mygame
cd mygame
dotnet run                                   # on the PC: Hello from E#!
esharp test                                  # the ok/FAIL checks on the PC
esharp build --target m68k --docker amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859
esharp run --docker amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859 --headless
```

Building for the Amiga needs Docker (or a cross compiler on PATH); running needs FS-UAE (and Xvfb for `--headless`).
Use your own Kickstart ROM with `--kickstart <rom>`; otherwise FS-UAE's built-in AROS ROM replacement is used.

Documentation, samples and the status of each target: https://github.com/GameGyrafe/amigaesharp

## Homage

The name and spirit come from **Wouter van Oortmerssen's Amiga E**, and from AMOS: a productive language for a retro
machine with fast edit-build-run cycles. E# is an unrelated project that shares only that ambition. It reuses no code
from Amiga E (whose compiler source is GPL); it is a design inspiration only.

## Licence

MIT, copyright Gyrafe Software.
