# MUI in the emulator

`esharp run` and `esharp test` (m68k, FS-UAE) can mount the classic MUI so that programs which open `muimaster.library`
run on a real Kickstart 3.0/3.1 A1200:

```bash
tools/mui/fetch-mui.sh 3.8        # or 5; once per user and version
esharp run samples/muicheck --docker <image> --headless --model A1200 --kickstart <rom> --mui 3.8 --workbench <Workbench disk 2 .adf>
```

`ESHARP_MUI=3.8|5` does the same as `--mui`. `--target os4`, `aros` and `sdl` reject `--mui`: the AmigaOS 4.1 VM has MUI 5
installed, AROS has Zune, and SDL has no MUI.

## Licence: why it is fetched, not shipped

MUI is third-party shareware. Its licence allows only verbatim copies of the whole archive and forbids disassembly. So
E# never contains, commits or redistributes any MUI file: `fetch-mui.sh` downloads the official archive from the MUI
project's GitHub releases (`amiga-mui/muidev`: tag `3.8`, asset `mui38usr.lha`; tag `MUI-5.0-20210831`, asset
`MUI-5.0-20210831-os3.lha`), checks it against a pinned sha256, and unpacks it with the toolchain image's `lha` in a
container with no network into a per-user cache (`ESHARP_MUI_CACHE`, else `$XDG_CACHE_HOME/esharp/mui`, else
`~/.cache/esharp/mui`). Running unregistered is fine for tests. Registration is the user's business. Never put MUI on the
hardware-pack ADFs.

## What the run does

MUI cannot start on a bare Kickstart: `muimaster.library` opens asl, commodities, icon, locale, iffparse and more from the
Workbench disk's `LIBS:`, and a bare emulated boot has no `C:Assign`. So `--mui` also needs your own Workbench 3.x disk 2
(`--workbench <adf>` or `ESHARP_WORKBENCH`), mounted read-only as `DF0:` and never copied. The generated startup then
assigns `C:`, `T:` and `LIBS:` from it, `ENV:` to `RAM:Env`, and adds what MUI's installer would: the unpacked drawer is the
read-only volume `MUI:` with `Assign LIBS: MUI:Libs ADD`, `Assign LOCALE: MUI:Locale ADD` and `Assign IMAGES: MUI:Images`.
Without the Workbench disk, `muimaster.library` does not open (tried: Libs copied into the boot volume, still "not installed").

```bash
esharp run samples/muicheck --docker <image> --headless --model A1200 --kickstart <rom> --mui 3.8 --workbench <Workbench 3.1 disk 2 .adf>
```

prints `muimaster.library 19.35` (MUI 3.8) or `muimaster.library 21.227` (MUI 5.0-20210831).
