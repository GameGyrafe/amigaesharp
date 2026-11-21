# E# library packages: a library and a game

- `Tween/` is an E# library (`EsLibrary=true` in `Tween.csproj`): easing curves on fixed-point time, a generic `Tween<T>`
  struct and a generic method. `dotnet pack` makes the NuGet package `ESharpSample.Tween`, whose DLL carries the C# sources
  and the manifest.
- `TweenDemo/` is a game that references that package. `esharp test` runs its checks on the PC and, with a target, on the
  Amiga, and compares them.

Both projects reference AmigaESharp.Sdk at `0.1.0-dev` (what `dotnet pack` of a checkout makes; pass
`-p:ESharpSdkVersion=<version>` to use a released one). From the repository root, with a local folder feed (nothing is
published, and nothing for this sample comes from nuget.org):

```bash
dotnet pack src/ESharp.Sdk -o /tmp/esfeed                    # AmigaESharp.Sdk 0.1.0-dev, with the E# library step
dotnet pack samples/packages/Tween -o /tmp/esfeed --source /tmp/esfeed   # ESharpSample.Tween 1.0.0
dotnet restore samples/packages/TweenDemo --source /tmp/esfeed
dotnet run --project src/ESharp.Cli -- test samples/packages/TweenDemo                       # host: 12 ok
dotnet run --project src/ESharp.Cli -- test samples/packages/TweenDemo --target m68k --docker <image> --kickstart <rom>
```

NuGet keeps the first copy of a version in its package cache (`~/.nuget/packages`): after changing the Sdk or the library,
pack a new version, or set `NUGET_PACKAGES` to an empty folder for these commands.

The unit test `PackageRoundTripTests` does the same in a temporary folder (with its own package cache), and builds the
game for m68k when the m68k image is in the local docker. How to write and use a library: docs/GUIDE.md, "Libraries".
