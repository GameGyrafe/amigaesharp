# E# library packages: a library and a game

- `Tween/` is an E# library (`EsLibrary=true` in `Tween.csproj`): easing curves on fixed-point time, a generic `Tween<T>`
  struct and a generic method, a class games may derive from (`Mover`, marked `[Extensible]`: a point moving along a tween,
  with an overridable curve `Shape` and an `OnArrived` hook that its `Step` calls), and assets of its own: `assets.json` lists `ball.png` (a 16x16 shaded ball, masked) and
  `blip.wav` (a short falling tone), both drawn in code by `tools/AssetSamples` (`dotnet run --project tools/AssetSamples --
  --tween samples/packages/Tween`). `esharp assets samples/packages/Tween` turns them into `Assets.g.cs` (class `Tween.Art`),
  which is committed and shipped as one of the library's sources; `Ball.cs` draws the sprite and plays the sound. `dotnet pack`
  makes the NuGet package `ESharpSample.Tween`, whose DLL carries the C# sources (with `Assets.g.cs`) and the manifest.
- `Checksum/` is an E# library with C of its own (`EsFeatures` `Native;NativeC`, `EsNativePrefix` `cksum_`): `Adler32.cs`
  declares the C function `cksum_adler32` as a `[Native]` extern (with a C# version under `#if HOST` for dotnet), and
  `native/common/adler32.c` is that function, an Adler-32 checksum written for this sample. `native/common/cksum_tune.h`
  says how many bytes the inner loop sums per pass (1); `native/m68k/cksum_tune.h` replaces it on m68k (8). `dotnet pack`
  makes `ESharpSample.Checksum`, whose DLL carries the C# source, the three C files and the manifest. The game's build prints
  the C files it compiles and writes them to `obj/<target>/esharp-native/ESharpSample.Checksum/`.
- `TweenDemo/` is a game that references both packages. `esharp test` runs its checks on the PC and, with a target, on the
  Amiga, and compares them. It plays the blip and draws the ball at five points of a tween on a 320x200 screen, prints
  `SHOT`, and keeps the screen up for three seconds (`esharp run ... --headless --screenshot shot.png` to see it). It also
checks the Checksum package's C against a known value and against an Adler-32 loop of its own in C#, and derives its own
`Hopper` from the package's `Mover` (an ease-in curve and one hop back; both overrides are called from the package's `Step`).

The projects reference AmigaESharp.Sdk at `0.1.0-dev` (what `dotnet pack` of a checkout makes; pass
`-p:ESharpSdkVersion=<version>` to use a released one). From the repository root, with a local folder feed (nothing is
published, and nothing for this sample comes from nuget.org):

```bash
dotnet pack src/ESharp.Sdk -o /tmp/esfeed                    # AmigaESharp.Sdk 0.1.0-dev, with the E# library step
dotnet pack samples/packages/Tween -o /tmp/esfeed --source /tmp/esfeed   # ESharpSample.Tween 1.0.0
dotnet pack samples/packages/Checksum -o /tmp/esfeed --source /tmp/esfeed   # ESharpSample.Checksum 1.0.0
dotnet restore samples/packages/TweenDemo --source /tmp/esfeed
dotnet run --project src/ESharp.Cli -- test samples/packages/TweenDemo                       # host: 22 ok
dotnet run --project src/ESharp.Cli -- test samples/packages/TweenDemo --target m68k --docker <image> --kickstart <rom>
```

NuGet keeps the first copy of a version in its package cache (`~/.nuget/packages`): after changing the Sdk or the library,
pack a new version, or set `NUGET_PACKAGES` to an empty folder for these commands. After changing `ball.png`, `blip.wav` or
`assets.json`, run `esharp assets samples/packages/Tween` before packing (the library build warns, ESL011, when
`Assets.g.cs` was made from other contents: it records a SHA-256 per input).

The unit test `PackageRoundTripTests` does the same in a temporary folder (with its own package cache), and builds the
game for m68k when the m68k image is in the local docker. How to write and use a library: docs/GUIDE.md, "Libraries".
