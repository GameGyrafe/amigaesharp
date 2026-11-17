#!/usr/bin/env python3
"""Removes the NewIcons image (the marker tool type and the IM1=/IM2= lines after it, issue #101) from Workbench icons, in place.

The floppy pack (hwtest.adf) has no room for it: with the NewIcons lines the icons need about 55 KB more than the disk has free.
Usage: stripni.py <file.info>...   Only tool and project icons as esharp writes them (no drawer record); anything else is left alone.
Python standard library only. The icon layout is described in src/ESharp.Assets/Icon.cs.
"""
import struct, sys

MARKER = b"*** DON'T EDIT THE FOLLOWING LINES!! ***"


def strip(d):
    """The icon bytes without the NewIcons lines (the same bytes when there are none). Raises ValueError for what it does not read."""
    be32 = lambda o: struct.unpack_from(">I", d, o)[0]
    be16 = lambda o: struct.unpack_from(">H", d, o)[0]
    if len(d) < 78 or be16(0) != 0xE310: raise ValueError("not an icon file")
    if be32(66) != 0: raise ValueError("drawer icons are not handled")
    pos = 78
    for field in (22, 26):                       # normal and selected image
        if be32(field) == 0: continue
        w, h, depth = be16(pos + 4), be16(pos + 6), be16(pos + 8)
        pos += 20 + ((w + 15) // 16 * 2 * h * depth if be32(pos + 10) else 0)
    def string(p):
        n = be32(p); return p + 4 + n
    if be32(50): pos = string(pos)               # default tool
    if not be32(54): return d                    # no tool types
    start = pos; count = be32(pos) // 4 - 1; pos += 4
    keep = []
    for _ in range(count):
        n = be32(pos); s = d[pos + 4:pos + 4 + n]; pos += 4 + n
        keep.append(s)
    cut = next((i for i, s in enumerate(keep) if s[:-1] == MARKER), None)
    if cut is None: return d
    kept = keep[:cut]
    table = b"" if not kept else struct.pack(">I", (len(kept) + 1) * 4) + b"".join(struct.pack(">I", len(s)) + s for s in kept)
    out = bytearray(d[:start] + table + d[pos:])
    struct.pack_into(">I", out, 54, 1 if kept else 0)
    return bytes(out)


if __name__ == "__main__":
    for f in sys.argv[1:]:
        data = open(f, "rb").read()
        try: new = strip(data)
        except ValueError as e: print(f"{f}: {e}, left as it is"); continue
        if new != data: open(f, "wb").write(new)
