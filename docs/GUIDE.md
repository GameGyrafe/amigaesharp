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
embedded resources (issue #31). The IDE, `dotnet build`, `dotnet run` and the host side of `esharp test` use the
compiled DLL (the library built with `HOST`). `esharp build`, `run` and `test --target` read the sources back out of the
restored package and compile them with your program for the chosen target, so every `#if AMIGA` / `#elif SDL` branch is
there; a package packed with its precompiled binaries is linked from the binary for the target instead ("Precompiled binaries"
below). `samples/packages` has two libraries (`Tween`: easing curves, a generic tween struct, and a ball sprite and a blip sound
from its own `assets.json`; `Checksum`: an Adler-32 checksum in C of its own) and a game that uses both (`TweenDemo`), with the
steps in its README.

**A new library project.** `esharp new --library <name>` (a path works too; the last segment is the name, a valid C# identifier)
writes a folder with the library `<name>/` (`<name>.csproj` as below, with all six targets, no features, `ImplicitUsings` disabled, a
placeholder `PackageId` `MyCompany.<name>` that you must change (ids starting with `AmigaESharp` are reserved), the MIT licence
expression, and `Counter.cs`, a public sample class in the namespace `<name>`), a demo program `<name>.Demo/` that uses the library
and prints `ok` lines so `esharp test` works, a `nuget.config` with the local folder feed `feed/`, the same `.vscode/` files as a program
project (the tasks work on the demo; one more packs the library) and a README with the steps. Like `esharp new`, both csproj files
reference the AmigaESharp.Sdk package at the tool's version, or with a checkout (`--source`, or a CLI run from one) its projects, and
the library then imports the Sdk's library step from the checkout (`--package` forces the package; publish only a library made that way).
Then `dotnet pack <name> -o feed`, `dotnet restore <name>.Demo`, `esharp test <name>.Demo [--target m68k --docker <image> --kickstart <rom>]`.

**Writing a library.** Reference AmigaESharp.Sdk and set `EsLibrary`; the Sdk's MSBuild step (in the package's
`buildTransitive/`) embeds every C# file of the project and writes the manifest when the library is built:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>disable</ImplicitUsings>       <!-- required: the Amiga build sees only the embedded files -->
  <EsLibrary>true</EsLibrary>
  <EsTargets>m68k;os4;morphos;aros;aros-i386;sdl</EsTargets>   <!-- required: the targets it supports -->
  <EsFeatures></EsFeatures>                      <!-- AmigaLib, Native, InlineAsm, Interrupt, HookEntry, NativeC: platform-level features it uses -->
  <PackageId>MyName.Easing</PackageId>
  <Version>1.0.0</Version>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="AmigaESharp.Sdk" Version="<the esharp version>" />
</ItemGroup>
```

then `dotnet pack -o <feed>`. The manifest (`esharp/library.json` in the DLL) holds the package id and version, the targets,
`minSdk` (`EsMinSdkVersion`, by default the AmigaESharp.Sdk version the library was built against), the features, the
library's own symbols, the licence expression and, for a library with assets, its asset class. Rules for the code: the same subset as a program (the analyzer from the Sdk runs on the library in the
IDE and in `dotnet build`); no `[EntryPoint]`; types in a namespace of your own; `public` for what programs may use
(`internal` stays inside the library, as it would between two assemblies). Declaring `[AmigaLib]` bindings, `[Native]`
externs, `[InlineAsm]` bodies, `[Interrupt]` servers or `[HookEntry]` methods needs `AmigaLib`, `Native`, `InlineAsm`, `Interrupt` or `HookEntry` in `EsFeatures`, and so does calling the platform's
raw ones: any `[AmigaLib]` or `[Native]` extern, such as `Exec.AllocVec`, `Asl.AllocAslRequest` or `Dos.PutStr` (a dos.library
binding on the Amiga targets; on sdl it is a plain method, so an sdl build does not ask for it). The platform API built on
them (`Video`, `Gfx`, `Files`, `Font`, ...) needs nothing. Every file needs its own `using` lines: `ImplicitUsings` and
`<Using>` items stop the library build (ESL004, ESL008), since their generated file is not part of the embedded sources. The
library's own `DefineConstants` symbols (other than DEBUG, TRACE, RELEASE and HOST) go into the manifest and `esharp build`
defines them for the library's files only, next to the target's; an E# target symbol there (`AMIGA`, `M68K`, `SDL`, ...)
stops the library build (ESL009). C files go in `native/` ("C in a library" below); anywhere else in the project they
stop the library build (ESL006), as does prebuilt code. Community packages use any id plus the `esharp` tag (the step adds it).
The `AmigaESharp` id prefix is reserved on nuget.org for the official packages (since 2026-11-24): nuget.org refuses a
new package id that starts with it (in any letter case) from any other account, and marks the official packages with
its reserved-prefix check mark, so pick your own id for a community package. A `Compile` item from outside the project
folder without a `Link` (source-only package contentFiles, for one) is not part of the library and is skipped, with warning ESL010 naming the file (the HOST DLL
still has it, so the Amiga build would miss it); give it a `Link` to embed it. A file on another drive (Windows) counts as
outside too. The step sets `SetLinkMetadataAutomatically` to false only when the project does not set it; set it to true and
the SDK links such files by their file name, and they are embedded. A package whose compile assets list several DLLs that
carry a manifest (`lib/` and `ref/`) is one library, read from the `lib/` one (the restore's runtime assets are read first,
then its compile assets).

**Assets in a library.** A library can ship images and sounds the way a program does ([Assets](#assets)): an `assets.json`
in the library's project folder, and the `Assets.g.cs` that `esharp assets <library dir>` writes next to it (commit it, as for
a program). That generated file is an ordinary source of the library: it is compiled into the HOST DLL (so the IDE, `dotnet
test` and the host side of `esharp test` see the same data) and embedded with the other sources, and the manifest names it
(`"assets": { "source": "Assets.g.cs", "namespace": ..., "class": ... }`, the namespace and class as `Assets.g.cs` has them). A game that references the package gets the
library's planar images, masks, palettes and samples as `[ChipRam]` static data, compiled with the program exactly as the
game's own `Assets.g.cs` is: in chip RAM on m68k (`__chip`, counted by the chip report and `--chip-budget`), and as plain
static data on the other targets (where `[ChipRam]` is a no-op, as for program assets), on every target the library lists. The build prints them (`assets: Tween.Art (Assets.g.cs; ...)`).

```json
{ "namespace": "Tween", "class": "Art",
  "images":  [ { "name": "Ball", "file": "ball.png", "colors": 4, "mask": true } ],
  "samples": [ { "name": "Blip", "file": "blip.wav" } ] }
```

- **Why the converted file and not the PNGs.** The conversion does not depend on the target: a program's one `Assets.g.cs`
  serves m68k, os4, morphos, aros, aros-i386 and sdl (planar bytes, 12-bit or 24-bit palette values and signed sample bytes
  read the same on each), and the colour count, depth, dither and AGA choice are set per image in `assets.json`, not per
  target. Shipping the converted data means the game gets exactly the bytes the library author built, looked at and tested,
  whichever esharp version the game uses (a later colour reducer cannot change a published library's art), the HOST DLL and the
  Amiga build hold the same data, and the game's build has nothing to convert. The PNG, ILBM and WAV inputs and
  `assets.json` stay in the library's source; the package does not carry them. Should a conversion that depends on the target
  ever be added, the manifest can then name the inputs as well.
- **Names.** `assets.json` must set `"namespace"` (ESL012), normally the library's own, so the class is `Tween.Art` rather
  than the default `Assets.Assets`. The same class in the game (its own `assets.json` with the same namespace and class) or
  in another package is error ES0115, which says that it comes from an `assets.json`; in their own namespaces the C names
  carry the namespace (`Es_Tween_Art_BallPlanes`), so they never clash with the game's assets.
- **Checks when the library is built.** `assets.json` without an `Assets.g.cs` next to it stops the build (ESL005: run
  `esharp assets`); an `Assets.g.cs` made from another `assets.json` or input than the current one is warning ESL011 (it compares the
  SHA-256 per input that `esharp assets` writes at the top of the file, so a fresh clone does not warn) (run `esharp assets`
  again so the package carries the current art); a project that removes `Assets.g.cs` from `Compile` stops it (ESL013). Only
  the `assets.json` in the project folder counts as the library's assets.

**C in a library.** A library can bring C source of its own, compiled into the game with the program's C (feature
`NativeC`). The C# side declares each C function as a `[Native]` extern (feature `Native`), with a C# version under `#if HOST`
for the IDE, `dotnet test` and the host side of `esharp test`, which have no C:

```
MyLib/
  MyLib.csproj            EsFeatures Native;NativeC, optional EsNativePrefix
  Checksum.cs             [Native("cksum_adler32")] static extern uint Run(uint sum, byte* data, int length);  (#if HOST: C#)
  native/common/          every target: adler32.c, cksum_tune.h
  native/m68k/            m68k only: cksum_tune.h (replaces the common file of the same name)
```

- **Layout and targets.** `native/common/` holds the files for every target, `native/<target>/` (`m68k`, `os4`, `morphos`,
  `aros`, `aros-i386`, `sdl`) those for one; a target's file replaces the common file of the same name. One level deep, `.c` and
  `.h` only. The game's build takes the common files plus its target's; when that set has no `.c` file and the package declares
  `[Native]` externs for its C (a C name with its prefix) in the C# compiled for the target, the build stops (ES0119). A library
  with C for m68k only and C# for the other targets puts the externs under `#if M68K` and builds elsewhere without error. File
  names that differ only in case (`common/Tune.h` and `m68k/tune.h`) are one file on Windows and macOS: the library build
  refuses them (ESL006), and so does the game's build for a package that carries them (ES0118). The library build refuses
  (ESL006) a `.c` or `.h` anywhere else in the project, any other file in `native/`, and prebuilt code anywhere (`.o`, `.obj`,
  `.a`, `.lib`, `.so`, `.dylib`): a package carries C source only, which the game's own compiler builds for its target with its
  flags. (The only binaries a package carries are the ones the pack step builds from these sources, "Precompiled binaries" below,
  and those include the package's C.) `NativeC` without a `native/` folder, or the other way round, stops it too (ESL015). The files are embedded in the DLL
  (`esharp/native/<folder>/<name>`) and listed in the manifest (`"native": { "prefix": ..., "files": [...] }`); the package
  carries them nowhere else.
- **How it is built.** `esharp build` (also `run` and `test --target`, with `--docker` or `--toolchain`) writes the chosen set to
  `obj/<target>/esharp-native/<package id>/` next to the program's C and passes each `.c` to the same compiler command as the
  program's C, as a translation unit of its own (`--emit-only` writes them and stops). The program's C does not include the
  package's headers: it gets a prototype of each `[Native]` extern of the package, made from the C# declaration. The same
  prototypes are in `esharp_natives.h`, which every `.c` of the package includes first, so a C definition that does not match its
  C# declaration is a compile error (conflicting types). `#include "x.h"` in the package's C finds its own headers (no include
  path is added for them, so the game and other packages do not see them); compiler messages name
  `<package Id Version>/native/common/x.c`. `esharp addr` does not map addresses in package C (its line table is the program's).
- **Types.** A package's own C function takes and returns numbers (`int` is `int32_t`, `byte` `uint8_t`, `nint` `intptr_t`, ...),
  `bool`, `string` (`const char *`) and pointers to those or `void`: no `ref`/`out`/`in`, structs or generic methods (ES0121).
  Pass an array or span as a pointer taken in `fixed` and its length.
- **Names.** Every function and variable the package's C exports (not `static`) starts with its native prefix (ES0120):
  `EsNativePrefix`, by default the package id in lower case with other characters as `_` (`ESharpSample.Checksum` gives
  `esharpsample_checksum_`). It must start with a letter, end in `_`, and not start with `es_` (E#'s own C names are `es_*`,
  `Es_*` and `ES_*`; ESL014). Two packages whose prefixes overlap (one starts with the other) stop the game's build (ES0122). So
  no two packages, and no package and the program, define the same C name. The check reads the C source; a name made by a macro
  escapes it, and then the linker's duplicate-symbol error stops the build (a build with package C passes `-fno-common`, so
  this holds for uninitialised variables too: gcc before 10, the m68k one among them, would otherwise merge `int x;` in two
  files into one variable). `static` helpers need no prefix. A file-level macro that defines a function, such as
  `DEFINE_FN(x) { }` with `#define DEFINE_FN(n) void n(void)`, is reported by ES0120 under the macro's name (`DEFINE_FN`), the
  only name the reading sees; the C it expands to is not read (a name the macro makes is not checked, as above). Only the package
  declares its own C functions: a `[Native]` extern in the program (or in another package) whose C name starts with a package's
  prefix is refused (ES0123), because the program's C gets the prototype from the package's own declaration; call the package's
  C# method instead.
- **Security.** A package's C is compiled into your game and runs with the game's privileges, as the game's own code does: on
  the Amiga that is the whole machine (no memory protection on AmigaOS 3). Read it before you use the package, as you would read
  its C#. The build prints every C file it compiles for the target:
  `native C (prefix cksum_; compiled into the program, runs with its privileges): native/common/adler32.c, native/m68k/cksum_tune.h`.
  The licence check (ES0111) covers the package as a whole, C included.

**Classes to derive from.** A class of a library that games may derive from is marked `[Extensible]` (from `ESharp`, as
`GtWindow` is in the platform); its virtual and abstract members are then the library's extension points:

```csharp
// in the library (samples/packages/Tween/Mover.cs)
[Extensible]
public class Mover
{
    public Mover(Point from, Point to, int frames) { ... }
    protected virtual int Shape(int t) => Ease.Linear(t);   // the curve
    protected virtual void OnArrived() { }                    // called by Step at the end of the path
    public bool Step() { ... Shape(...) ... OnArrived(); ... }
}

// in the game (samples/packages/TweenDemo/Program.cs)
public sealed class Hopper : Mover
{
    public Hopper() : base(new Point(0, 0), new Point(100, 40), 4) { }
    protected override int Shape(int t) => Tween.Ease.InQuad(t);
    protected override void OnArrived() { ... }
}
```

The game's class works as any class of a hierarchy within a program: constructors chain into the library's (`: base(...)`),
`override`, `base.` calls, abstract members, `is`/`as`, a derived class of the derived class, the object passed to the
library's code as its base type and freed through it (`Heap.Free`). Since the package is compiled from its sources with the
game, the C is the same as for classes of one program (one class table per class, the derived struct starting with the
library's), on every target. A class of a package that is not `[Extensible]` cannot be a base class of the game's classes or of
another package's (ES0117: "'Dog' derives from 'Zoo.Animal' of package 'Sample.Zoo', which is not marked [Extensible]; ...");
within its own package any class may derive from any other. The IDE and `dotnet build` of the game give the same ES0117 and
accept the same `[Extensible]` bases, reading the attribute from the package's DLL: the Sdk step writes the package id into
that DLL (`[assembly: ESharp.EsLibrary("<PackageId>")]`, in the generated assembly info, not in the embedded sources), so the
IDE names the package as `esharp build` does. Marking a class `[Extensible]` makes its protected and virtual members part of
the library's public contract: changing them later breaks the games that derive from it. That holds across versions of the
package (names, signatures, which members are virtual or abstract, what the library calls and in which order), and for a package class that itself
derives from a platform `[Extensible]` class such as `GtWindow`: the game's class derives from the package's, which derives from the platform's, so
the protected and virtual members of both are part of the contract. Nothing checks this for you; treat such a change as a major version.

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
| The package needs a newer esharp (`minSdk`; not checked by a `-dev` build from a checkout; `esharp test` reports it from the manifest before the host run loads the package DLL) | ES0112 |
| `[AmigaLib]`, `[Native]`, `[InlineAsm]`, `[Interrupt]` or `[HookEntry]` declared in the package, or an `[AmigaLib]`/`[Native]` extern of the platform called from it, without the matching `EsFeatures` entry | ES0113 |
| A package with `[Native]` externs for its own C but no `.c` file for the chosen target (`native/common/` and `native/<target>/`) | ES0119 |
| A function or variable of a package's C (not `static`) whose name does not start with the package's native prefix | ES0120 |
| A `[Native]` extern outside the package whose C name starts with that package's native prefix | ES0123 |
| A `[Native]` extern for the package's own C with a type C cannot be given (`ref`, a struct, a generic method) | ES0121 |
| Two packages whose native prefixes overlap | ES0122 |
| An `[EntryPoint]` in a package | ES0114 |
| The same type (namespace, name, arity; also a `partial` one) in a package and the program, or in two packages (also an asset class: the message names the `assets.json`) | ES0115 |
| An `internal` member of a package used from the program or another package, also through a derived class (`base(...)` or the implicit `base()` to an internal constructor, an override of an internal or `private protected` member) | ES0116 |
| A class of the program or of another package derived from a package class that is not marked `[Extensible]` (also in the IDE and `dotnet build`, from the package's DLL) | ES0117 |
| A package that cannot be used: an unknown manifest format or resource, asset inputs (images, sounds) in it, a bad path, a reserved or invalid symbol in its `defines`, an asset declaration it cannot read or whose generated source it does not carry, a C file outside `esharp/native/common` or `esharp/native/<target>`, prebuilt code, C files the manifest does not list (or listed files it does not carry), native C without the `NativeC` feature or the other way round, a bad native prefix; a precompiled binary that is not as its `binary.json` says (a file it does not list, a SHA-256 that differs, other objects in the archive, another package, version or sources) and prebuilt code anywhere else in the package (`.o`, `.obj`, `.a`, `.lib`, `.so`, `.dylib`) | ES0118 |
| The package has precompiled binaries, but none for this target, or made by another E#, or for another configuration (`--debug`, `--trace`): its sources are compiled instead (a warning) | ES0124 |

Types with the same name in different namespaces (the program's `Game.Ease` and the package's `Tween.Ease`) are fine: their C
names carry the namespace, as for any two namespaces. Package code is emitted in full, like program code (unused package
functions are not pruned yet). `esharp.include` stays the way to share source folders between local projects without NuGet.

### Precompiled binaries

The same model as the native libraries of .NET binding packages (issue #154): the library's author builds a static library per
target when packing, and the package carries it next to the DLL. A game built with the same E# links it instead of compiling the
package's sources. The sources still ship: they are needed for generic copies over the game's own types (`Pool<MyEnemy>`), as the
fallback, and for the IDE, the analyzer, the licence checks and `esharp verify-package`.

- **Packing.** `dotnet pack` of an `EsLibrary` project runs `esharp precompile` (the AmigaESharp.Sdk targets call the esharp tool,
  `EsCommand`, default `esharp` on PATH; a checkout's template sets `dotnet run --project <checkout>/src/ESharp.Cli --`). For every
  target in `EsTargets` it compiles the library alone (a library build of the compiler: the package's C, no `main`) and builds it
  with that target's toolchain into `lib/net10.0/esharp-bin/<target>/library.a` (`sdl` is `sdl-linux-x64`, the only sdl build so
  far; one folder per OS and CPU later, #156), with the package's C files as objects of their own, and its manifest
  `binary.json`: the target, the E# stamp (the esharp version and the identity of its compiler, because names, layouts and the runtime
  change with it), the configuration (`release`), the C names of the generic copies it holds, the runtime pieces and library bases its
  code uses, the archive's objects and its SHA-256, and a hash of the sources it was built from. The toolchain per target: an image
  from `EsPrecompileArgs` (`--image m68k=<image>`, or `--docker [image]` for all), else `ESHARP_TOOLCHAIN_<TARGET>` (or
  `ESHARP_TOOLCHAIN`), else the default image (`esharp-amiga-toolchain`, `esharp-aros-i386`, `esharp-sdl`) when it is in the local
  Docker, else a compiler of the pinned version on PATH. **The pack fails** (ESL016) when a listed target has none: remove the target
  from `EsTargets`, or set `<EsPrecompile>false</EsPrecompile>` to ship sources only (as before). A library whose code calls the
  sdl C runtime's `[Native]` functions directly cannot be precompiled (ES0125: that runtime keeps its state in the program).
- **Linking.** `esharp build` (and `run`, `test --target`) uses a package's binary when there is one for the target, made by this
  E# for this configuration; it prints `Package X 1.0.0: linked from its precompiled m68k binary (...); its sources are not compiled`
  and copies the archive to `obj/<target>/esharp-bin/<id>/`. Otherwise it compiles the sources with warning ES0124 saying why (a
  package without binaries compiles from source without a warning). `--from-source` compiles every package from its sources.
  The program's C then declares the package's functions, statics and class tables instead of defining them, and defines only the
  generic copies the binary does not hold (over the program's own types); every copy has a fixed C name (`Es_Tween_Tween__int`), so
  none is defined twice. The archive goes after the program's C on the compiler line (before the system libraries); the build
  passes `-fno-common`.
- **One runtime.** The E# runtime's state (the counters of `Heap.LiveClosures`, the exit hook of a fatal error, the text ring of
  string formatting, the command line of `Args`, the library bases) is the program's: a binary declares it, and the program defines
  what its packages' binaries use (their manifests say which). The program's code also defines every platform function a binary
  calls, and all the virtual members of the platform classes its classes derive from (`GtWindow`).
- **Start-up order.** With binaries, `main` runs the platform's static initializers first, then each binary's (an init function in
  the binary, in package order), then the program's and those of packages compiled from source.
- **Reproducible.** The compiler runs in a work folder with fixed relative names (in Docker always `/work`), without debug
  information, and `ar` writes the archive in deterministic mode (no times, owners or modes), so two builds in different folders give
  the same bytes, and the pinned m68k image and `esharp-amiga-toolchain` give the same m68k binary.
- **Checking a package.** `esharp verify-package <nupkg or extracted folder>` rebuilds each binary from the sources the package
  carries, with the same toolchain choice as the pack (`--image <target>=<image>`, `--docker`), and compares the bytes: `ok m68k:
  library.a rebuilt from the package's sources, byte for byte`, or `FAIL` and exit code 1. It needs the esharp version that built the
  binaries (the stamp); a package that depends on other E# packages is not handled yet. The game's build itself checks only that the
  binaries are the ones `binary.json` lists (ES0118 otherwise); any other prebuilt file in the package is refused.
- **Not covered.** `esharp addr` maps addresses in the program's own C only (a binary's code has no line table in the map).
  A binary built for one esharp version is used only by that version (no stable ABI yet).

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
- User classes that derive from a platform class marked `[Extensible]` (so far `GtWindow`, see `samples/guiclass`) or from
  an `[Extensible]` class of an E# library package (see "Classes to derive from" under [Libraries](#libraries-e-packages)).
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
Fonts, BOOPSI, MUI and the raw library bindings such as `LowLevel`) do not exist on `sdl`: using them is error `ES0102`.
`Joystick` and the message box `Requester` exist on every target (below).

| Area | Classes | Sample |
|---|---|---|
| Program | `Dos` (output, delays), `Args` (arguments and Workbench tool types), `Trace`, `Files` | `hello`, `mainargs`, `trace` |
| Memory | `Mem`, `Arena`, `Pool<T>`, `Vec<T>`, `Heap`, `Dictionary`, `HashSet`, `StringMap` | `memory`, `collections` |
| Graphics | `Video` (screens, blits, palettes, double buffer, AGA 24-bit palettes, RTG), `Font`, `Frame`/`FrameTimer` | `screen`, `aga`, `dbuffer`, `rtg`, `timing` |
| Input and time | `Input` (mouse, keyboard), `Keys` (keymap), `Joystick` (lowlevel; SDL gamepads), `Clock` (timer) | `cards`, `keys`, `controls` |
| Message box | `Requester.Show` (EasyRequest; SDL_ShowMessageBox) | `requester` |
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

**Joystick on SDL.** The same `Joystick` calls and bit constants read SDL game pads: port 1 is the first pad connected, port 0
the second, and a pad connected later is picked up by the next `Read` or `Kind` (when the port-1 pad goes, the port-0 one moves
to port 1). `Fire` is the south face button (A on an Xbox pad), `Fire2` east (B), `Yellow` west (X), `Green` north (Y),
`Forward`/`Reverse` the right/left shoulder buttons and `Play` Start; the directions come from the d-pad and from the left stick
past a dead zone of a quarter of its travel. `Kind` is `KindGamepad` for a port with a pad; without one, port 0 is `KindMouse`
(the desktop pointer, as the Amiga mouse port) and port 1 `KindNone`. `Force` works as on the Amiga (a port forced to a
joystick reads only the directions, `Fire` and `Fire2`) and `Close` undoes it. With `Joystick.KeyboardStandIn = true` and no pad
on port 1, the cursor keys, Space (`Fire`) and Left Alt (`Fire2`) read as a joystick there (`Kind` 1 is then `KindJoystick`);
the keys come through the `Video` window, so call `Input.Poll` each frame as usual. Other targets keep the setting and ignore
it. Tests can plug in SDL virtual pads from the program itself (`Joystick.SdlAttachVirtual`, `SdlVirtualButton`,
`SdlVirtualAxis`, `SdlDetachVirtual`, `SdlVirtualKey`; SDL only, like `HostSet` on the host).

```csharp
Joystick.Open();
Joystick.KeyboardStandIn = true;          // SDL: play with the keyboard when no pad is plugged in
var (dx, dy) = Joystick.Direction(1);
bool fire = (Joystick.Read(1) & Joystick.Fire) != 0;
```

**Message box.** `Requester.Show(title, text, buttons)` shows `text` (several lines with `\n`) with the buttons listed in
`buttons` separated by `|` (`"Yes|No"`, up to 8; an empty string is one `OK`), waits, and returns the index of the chosen
button from the left (0 first). By the Amiga's convention the last button is the negative one ("No", "Cancel"): it is also the
answer when no box can be shown or it is closed without a button. On the Amiga targets it is intuition.library
`EasyRequestArgs`, on the screen of the `Video` window when one is open, else on the default public screen (Workbench); `%` in
the text is shown as typed. Keep lines short on a 320-pixel screen (about 36 characters of topaz 8; a wider requester runs off
the screen). On SDL it is `SDL_ShowMessageBox` over the game's window; without a desktop (headless, offscreen video) SDL has no
message box, so the answer is the last button and SDL's reason goes to standard error. On the host, `Requester.HostSet(n)`
presets the answer (the last button until then). `Requester.Count(buttons)` gives the number of buttons.

```csharp
if (Requester.Show("Cards", "Quit the game?", "Quit|Play on") == 0) return;
```

Hardware facts: 2 MB of chip RAM is the baseline; `[ChipRam]` is a no-op on PowerPC (chip memory is allocated at run time
there); both PowerPC targets are big-endian, so asset formats need no change.

## Assets

`assets.json` lists images, tile sheets, palettes and sounds; `esharp assets samples/screen` turns them into
`Assets.g.cs` (planar chip data, palettes, 8-bit signed samples) next to the manifest. `esharp build`, `run` and `test` do
this automatically when an input is newer than the output (`--force` rebuilds); commit the `Assets.g.cs` it writes. A library
package ships its assets the same way ([Libraries](#libraries-e-packages), "Assets in a library").
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
   pinned image has at `/opt/m68k-amigaos`; `bin/<compiler>.exe` on Windows). An empty `<dir>` counts as not given. On Windows a stray quote at either end of `<dir>` is dropped (also from `ESHARP_TOOLCHAIN_<TARGET>`, `ESHARP_TOOLCHAIN`, `--sysroot` and the sysroot variables; a value that is only a quote counts as not given): `--toolchain "C:\x y\"` reaches the program as `C:\x y"` because the backslash escapes the closing quote (leave the trailing backslash off to be safe).
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
- **macOS and Windows, m68k (not verified):** build AmigaPorts m68k-amigaos-gcc yourself and pass its install prefix; the
  Linux copies above are Linux programs and do not run there. Step by step in
  [Local m68k toolchain on macOS and Windows](#local-m68k-toolchain-on-macos-and-windows) below.
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

#### Local m68k toolchain on macOS and Windows

**Not verified.** Nobody has run any step of this section on a Mac or a Windows machine yet. It is written from the project
pages named in each step, in our own words; where a step is a guess it says so. The checklist at the end is for whoever tries
it first: please paste the results back on issue #122. Docker (`--docker`) stays the default and the safe way on both systems.

What you are building is the same compiler the pinned Docker image has: AmigaPorts m68k-amigaos-gcc
(https://github.com/AmigaPorts/m68k-amigaos-gcc, the continuation of Bebbo's amiga-gcc) with its **legacy gcc 6.5.0b**, which
reports `6.5.0b` for `-dumpversion`. `esharp build` refuses any other version (without `--any-cc-version`). The project's
default is a much newer gcc (16.2 at the time of writing), so the step that switches the gcc branch below is not optional.
The pinned image's own build string is `m68k-amigaos-gcc (AmigaDev amiga6-g0a5f82c43) 6.5.0b 20260819091705`; a build you make
yourself will be the same gcc branch, but libnix, the NDK headers and binutils come from the checkout you build, so the
generated executable may differ from the Docker one even when the version matches (the comparison step below tells you).
Building takes a while (the project says roughly 5 to 15 minutes on current Linux hardware; not measured on a Mac or on
Windows) and a few GB of disk.

**macOS (Intel and Apple Silicon), not verified.** Source: the project's build page,
https://github.com/AmigaPorts/m68k-amigaos-gcc/blob/master/doc/build.md ("macOS"). It does not name Apple Silicon either way,
so that is part of what the checklist finds out.

1. Install the Xcode command line tools (`xcode-select --install`) and Homebrew (https://brew.sh).
2. Install the build tools Apple does not ship in GNU form:
   `brew install bash bison coreutils curl flex gettext gnu-sed gnu-tar grep make wget xz`
3. Put Homebrew's GNU versions first on `PATH` under their plain names (the project says its build needs this and that Apple's
   old GNU make 3.81 is known to give link failures). In `~/.zshrc`:

   ```bash
   export PATH="$(brew --prefix bison)/bin:$(brew --prefix flex)/bin:$PATH"
   for pkg in coreutils gnu-sed gnu-tar grep make; do
     export PATH="$(brew --prefix $pkg)/libexec/gnubin:$PATH"
   done
   ```

   Open a new terminal; `make --version` should say GNU Make 4.x. (On Apple Silicon `brew --prefix` is `/opt/homebrew`; Intel
   `/usr/local`; the lines above work for both.)
4. Get the sources, pick the legacy gcc branch, and build into a folder you own (no `sudo`; the path may contain no spaces,
   to be safe: the project does not say it works with them):

   ```bash
   git clone https://github.com/AmigaPorts/m68k-amigaos-gcc
   cd m68k-amigaos-gcc
   make update
   make branch mod=gcc branch=amiga6      # branches.md: amiga6 is the legacy gcc 6.5.0b; the default is 16.2
   make all PREFIX="$HOME/amiga/m68k-amigaos" -j"$(sysctl -n hw.ncpu)"
   ```

   The branch step is from https://github.com/AmigaPorts/m68k-amigaos-gcc/blob/master/doc/branches.md. Whether the `amiga6`
   branch still builds on a current Mac, and whether it needs the extra Homebrew gcc the build page mentions for gdb
   (`brew install gcc@12`, then `CC=gcc-12 CXX=g++-12 make all`), is not known; gdb is not needed by E#. There is no separate
   install step: the build installs into `PREFIX` as it goes.
5. Check the compiler, then tell E# where it is:

   ```bash
   "$HOME/amiga/m68k-amigaos/bin/m68k-amigaos-gcc" -dumpversion        # must print 6.5.0b
   export ESHARP_TOOLCHAIN_M68K="$HOME/amiga/m68k-amigaos"             # or pass --toolchain on each command
   esharp doctor --target m68k
   esharp build samples/hello
   ```

**Windows with MSYS2, not verified.** Source: the same build page ("Windows with msys2"). The project documents MSYS2 only; its
build host list is not specific about native Windows beyond that.

1. Install MSYS2 (https://www.msys2.org) and open an *MSYS2 shell* (the `MSYS` one, not UCRT64: the project's package list is
   for the MSYS environment).
2. `pacman -S curl git base-devel gcc flex rsync`
3. Get the sources and build, **entering the directory by its absolute path** (the build page says `make` can fail to find
   files otherwise, because of an MSYS2 bug), and switch to the legacy gcc as on macOS:

   ```bash
   cd /c/msys64/home/<you>
   git clone https://github.com/AmigaPorts/m68k-amigaos-gcc
   cd /c/msys64/home/<you>/m68k-amigaos-gcc
   make update
   make branch mod=gcc branch=amiga6
   make all PREFIX=/c/amiga/m68k-amigaos -j$(nproc)
   ```

   `PREFIX` is the folder E# will be pointed at: `C:\amiga\m68k-amigaos` here. Avoid spaces in it. Whether `amiga6` builds
   under MSYS2 today is not known.
4. The result is `C:\amiga\m68k-amigaos\bin\m68k-amigaos-gcc.exe`. If it was built in the `MSYS` environment it may need the
   MSYS runtime DLLs (`msys-2.0.dll`, in `C:\msys64\usr\bin`) to start: if running it from PowerShell or cmd fails with a
   missing DLL, either add `C:\msys64\usr\bin` to the Windows `PATH`, or run `esharp` from the MSYS2 shell. Not verified, and
   it is a guess from how MSYS2 programs work.
5. Check and use it from PowerShell (`esharp` finds `bin\m68k-amigaos-gcc.exe`; paths with spaces are fine for `--toolchain`
   and `ESHARP_TOOLCHAIN_M68K`, quote them):

   ```powershell
   & C:\amiga\m68k-amigaos\bin\m68k-amigaos-gcc.exe -dumpversion     # must print 6.5.0b
   $env:ESHARP_TOOLCHAIN_M68K = "C:\amiga\m68k-amigaos"                # or --toolchain "C:\amiga\m68k-amigaos"
   esharp doctor --target m68k
   esharp build samples\hello
   ```

**Windows with WSL2, not verified.** Install WSL2 with Ubuntu 24.04 or newer (`wsl --install -d Ubuntu-24.04`), then follow the
**Linux** route above inside WSL (Docker Desktop with WSL integration for the image copy, or a Linux build of the toolchain),
and run `esharp` itself inside WSL too. Keep the project and the toolchain in the Linux file system (`~/...`), not under
`/mnt/c`: file access there is slow. This is the route with the fewest unknowns, because it is the verified Linux one.

**Compare with the Docker build, not verified on these systems.** `tools/local-toolchain/compare.sh` (Linux, in the E#
repository) does this with a copy of the image's own toolchain; it cannot be pointed at your own build, so on a Mac or Windows
do it by hand with Docker Desktop running (three commands per sample; on Windows use `samples\hello` and
`Get-FileHash -Algorithm SHA256 <file>`):

```bash
esharp build samples/hello --docker amigadev/crosstools:m68k-amigaos@sha256:32ec76dac03a642203f0f09f0416480d6c25de1092ed7ed472db870304a64859 -o hello-docker
esharp build samples/hello --toolchain "$HOME/amiga/m68k-amigaos" -o hello-local
shasum -a 256 hello-docker/hello hello-local/hello hello-docker/hello.c hello-local/hello.c
```

(The output folder holds the executable `<name>`, its icon `<name>.info` and the generated C `<name>.c`; compare all three.) Identical
hashes are the best result. Different hashes are not a failure of the setup if `-dumpversion` matches: record both hashes, the
`--version` first line of your compiler against the image's, and which sample it was. Then run the program in FS-UAE
(`esharp run samples/hello --headless --kickstart <rom>`; headless needs Xvfb, so on macOS and Windows leave `--headless` off).
Generated C does not depend on the toolchain: it is the same on every machine.

**Checklist for the first person to try this** (paste the answers into issue #122, one line each; say which of macOS Intel,
macOS Apple Silicon, Windows MSYS2 or Windows WSL2 you used):

- [ ] OS, version and CPU (`sw_vers` and `uname -m`, or `winver` and `systeminfo`).
- [ ] Which steps above worked as written, and the first line of any step that failed (with the error text).
- [ ] Did `make branch mod=gcc branch=amiga6` and `make all` finish? How long did it take, and how big is the install folder?
- [ ] Output of `m68k-amigaos-gcc -dumpversion` (expected `6.5.0b`) and of `m68k-amigaos-gcc --version | head -1`.
- [ ] Output of `esharp doctor --target m68k` (it should show the compiler, `6.5.0b`, "as in the pinned image").
- [ ] On Windows: does the compiler start from PowerShell without MSYS2 on `PATH`? Does a `--toolchain` folder with a space in it work?
- [ ] On Windows: `esharp build samples\hello` with the toolchain only on `PATH` (no `--toolchain`): does it find the `.exe`?
- [ ] The two SHA-256 hashes from the comparison above (Docker build and local build) for `samples/hello`, and whether they match.
- [ ] Did `esharp run samples/hello --kickstart <rom>` show the program's output in FS-UAE?
- [ ] Any message from `esharp` that was unclear or gave wrong advice.

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
esharp new [--library] <name> [--source [dir] | --package [version]]   new hello-world project (csproj, .vscode); --library: an E# library with a demo program and a local feed
esharp build [path] [options]      check the subset, emit C, run the target C compiler
esharp run [path] [options]        build and run (m68k in FS-UAE, os4 in the VM, sdl on the desktop)
esharp test [path] [options]       run on the host; with --target m68k|os4|aros|aros-i386|sdl also on the target, and compare
esharp assets [dir|assets.json] [--force]   PNG, ILBM and WAV to Assets.g.cs
esharp addr <exe | dir> [<where>] [--target <t>] [--docker [image] | --toolchain <dir>]   crash address to C# file:line
esharp os4vm check|start|install-hook|remove-hook   AmigaOS 4.1 QEMU VM helpers
esharp precompile <library dir> --dll <dll> --out <dir> [--targets <a;b>] [--docker [image]] [--image <t>=<image>]   an E# library's per-target binaries (run by dotnet pack)
esharp verify-package <nupkg | folder> [--docker [image]] [--image <t>=<image>]   rebuild a package's binaries from its sources and compare the bytes
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
