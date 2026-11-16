#!/usr/bin/env python3
"""mkadf.py: write and read Amiga DD floppy images (.adf, 880 KB) for the E# hardware test pack.

Python 3 standard library only; run it as  python3 -I tools/hwpack/mkadf.py ...
Written for E# from the documented AmigaDOS disk layout (boot block, root block, bitmap block, file header,
file extension, directory and data blocks; OFS and FFS). MIT licence, Gyrafe Software. It does not contain code
from any other tool.

  mkadf.py create <dir> <out.adf> [--name HWTEST] [--ofs] [--protect PATTERN=FLAGS ...]
                  [--bootblock-from SRC.adf] [--name-from SRC.adf] [--from-adf SRC.adf:PATH[=DESTPATH] ...] [--mkdir PATH ...] [--file DEST=LOCALFILE ...]
  mkadf.py ls <adf>                         list the whole disk (protection bits, sizes)
  mkadf.py extract <adf> <dest dir> [PATH ...]   copy files (default: everything) out of the image
  mkadf.py cat <adf> <PATH>                 write one file to stdout
  mkadf.py check <adf>                      verify checksums, and that the bitmap marks exactly the blocks the tree uses

Protection FLAGS use the letters AmigaDOS prints: h s p a r w e d (a letter present = that bit is on; for r w e d
"on" means the action is allowed). Files without a --protect rule get "rwed" (what a new file has). A script needs
"srwed", a program "rwed" (e is on). PATTERN is an fnmatch pattern matched against the path relative to the disk root
(case-insensitive, "/" separated) or just the file name; the first matching rule wins.

--bootblock-from takes the boot block code (bytes 12 to 1023) from another disk, normally your own Workbench disk, so the
image boots (the disk still needs S/Startup-sequence and the commands it uses); this tool holds no boot code of its own.
Dates are written as local time (AmigaDOS keeps local time; TZ=UTC gives reproducible images). --from-adf copies a file or a whole directory from another image (read, never modified).
"""
import fnmatch
import os
import struct
import sys
import time

BSIZE = 512
NBLOCKS = 1760
ROOT = 880
BITMAP = 881
HT_SIZE = 72
T_HEADER, T_DATA, T_LIST = 2, 8, 16
ST_ROOT, ST_USERDIR, ST_FILE = 1, 2, -3
AMIGA_EPOCH = 252460800  # 1978-01-01 in Unix seconds


class AdfError(Exception):
    pass


def _upper(ch):
    # Non-international hashing (DOS\\0 and DOS\\1 disks, the only ones this writer makes). DOS\\2/\\3 disks would also
    # fold the Latin-1 letters 0xE0-0xFE (except 0xF7); the reader walks the hash tables and never hashes, so it does not need that.
    return ch.upper() if "a" <= ch <= "z" else ch


def name_hash(name):
    h = len(name)
    for ch in name:
        h = (h * 13 + ord(_upper(ch))) & 0x7FF
    return h % HT_SIZE


def checksum_block(b, off=20):
    """Return the value to store at `off` so that all longs of the block sum to zero."""
    b = bytearray(b)
    struct.pack_into(">I", b, off, 0)
    s = sum(struct.unpack(">128I", bytes(b))) & 0xFFFFFFFF
    return (-s) & 0xFFFFFFFF


def _bootsum(b):
    s = 0
    for (v,) in struct.iter_unpack(">I", b):
        s += v
        if s > 0xFFFFFFFF:
            s = (s & 0xFFFFFFFF) + 1
    return s & 0xFFFFFFFF


def _amiga_date(t):
    # AmigaDOS stores local time: shift the Unix time by the local UTC offset (set TZ=UTC for reproducible images).
    t = int(t) + time.localtime(int(t)).tm_gmtoff - AMIGA_EPOCH
    if t < 0:
        t = 0
    days, rem = divmod(t, 86400)
    return days, rem // 60, (rem % 60) * 50


def protect_from_flags(flags):
    """AmigaDOS protection long from letters. rwed are inverted in the long (set = denied)."""
    flags = flags.lower()
    bad = set(flags) - set("hsparwed")
    if bad:
        raise AdfError("bad protection letters %r" % "".join(sorted(bad)))
    p = 0
    bits = {"a": 4, "p": 5, "s": 6, "h": 7}  # dos/dos.h: FIBB_ARCHIVE 4, FIBB_PURE 5, FIBB_SCRIPT 6, FIBB_HOLD 7
    for letter, bit in bits.items():
        if letter in flags:
            p |= 1 << bit
    for letter, bit in (("d", 0), ("e", 1), ("w", 2), ("r", 3)):
        if letter not in flags:
            p |= 1 << bit  # denied
    return p


def flags_from_protect(p):
    out = ""
    for letter, bit in (("h", 7), ("s", 6), ("p", 5), ("a", 4)):
        out += letter if p & (1 << bit) else "-"
    for letter, bit in (("r", 3), ("w", 2), ("e", 1), ("d", 0)):
        out += "-" if p & (1 << bit) else letter
    return out


# ---------------------------------------------------------------------------------------------- writer

class _Dir:
    def __init__(self, block, parent, name):
        self.block, self.parent, self.name = block, parent, name
        self.entries = {}  # upper(name) -> real name, to detect duplicates


class AdfWriter:
    def __init__(self, volume="HWTEST", ofs=False, when=None):
        self.img = bytearray(NBLOCKS * BSIZE)
        self.ofs = ofs
        self.volume = volume
        self.when = when if when is not None else int(os.environ.get("SOURCE_DATE_EPOCH", time.time()))
        self.free = [True] * NBLOCKS
        self.free[0] = self.free[1] = False
        self.free[ROOT] = self.free[BITMAP] = False
        self.order = list(range(BITMAP + 1, NBLOCKS)) + list(range(2, ROOT))
        self.dirs = {"": _Dir(ROOT, 0, volume)}
        self.bootable = False

    # blocks
    def _alloc(self):
        for b in self.order:
            if self.free[b]:
                self.free[b] = False
                return b
        raise AdfError("disk full (%d KB used of 880 KB)" % (self.used_bytes() // 1024))

    def used_bytes(self):
        return sum(1 for f in self.free if not f) * BSIZE

    def free_bytes(self):
        return sum(1 for f in self.free[2:] if f and True) * BSIZE

    def _blk(self, n):
        return memoryview(self.img)[n * BSIZE:(n + 1) * BSIZE]

    def _seal(self, n):
        b = self._blk(n)
        struct.pack_into(">I", b, 20, checksum_block(bytes(b)))

    def _put_name(self, b, name):
        raw = name.encode("latin-1")
        if not raw or len(raw) > 30:
            raise AdfError("name %r must be 1 to 30 characters" % name)
        if b"/" in raw or b":" in raw:
            raise AdfError("name %r contains / or :" % name)
        b[432] = len(raw)
        b[433:433 + len(raw)] = raw

    def _link(self, parent, name, block):
        """Insert header `block` into the hash table of directory `parent` (a _Dir)."""
        key = name.upper()
        if key in parent.entries:
            raise AdfError("duplicate name %r" % name)
        parent.entries[key] = name
        h = name_hash(name)
        pb = self._blk(parent.block)
        off = 24 + 4 * h
        head = struct.unpack_from(">I", pb, off)[0]
        struct.pack_into(">I", self._blk(block), 496, head)  # hash_chain
        struct.pack_into(">I", pb, off, block)
        self._seal(parent.block)
        self._seal(block)

    def _dir_for(self, path):
        d = self.dirs[""]
        cur = ""
        for part in [p for p in path.split("/") if p]:
            cur = (cur + "/" + part).lstrip("/")
            key = cur.upper()
            if key not in self.dirs:
                d = self._mkdir(d, part, key)
            else:
                d = self.dirs[key]
        return d

    def _mkdir(self, parent, name, key, prot=0):
        b = self._alloc()
        blk = self._blk(b)
        struct.pack_into(">III", blk, 0, T_HEADER, b, 0)
        struct.pack_into(">I", blk, 320, prot)
        struct.pack_into(">III", blk, 420, *_amiga_date(self.when))
        self._put_name(blk, name)
        struct.pack_into(">I", blk, 500, parent.block)
        struct.pack_into(">i", blk, 508, ST_USERDIR)
        d = _Dir(b, parent.block, name)
        self.dirs[key] = d
        self._link(parent, name, b)
        return d

    def mkdir(self, path):
        self._dir_for(path)

    def add_file(self, path, data, flags="rwed"):
        path = path.strip("/")
        if "/" in path:
            dpath, name = path.rsplit("/", 1)
        else:
            dpath, name = "", path
        parent = self._dir_for(dpath)
        nd = -(-len(data) // (BSIZE if not self.ofs else 488))
        dsz = BSIZE if not self.ofs else 488
        hdr = self._alloc()
        data_blocks = [self._alloc() for _ in range(nd)]
        # data blocks
        for i, db in enumerate(data_blocks):
            chunk = data[i * dsz:(i + 1) * dsz]
            blk = self._blk(db)
            if self.ofs:
                nxt = data_blocks[i + 1] if i + 1 < nd else 0
                struct.pack_into(">IIIIII", blk, 0, T_DATA, hdr, i + 1, len(chunk), nxt, 0)
                blk[24:24 + len(chunk)] = chunk
                self._seal(db)
            else:
                blk[0:len(chunk)] = chunk
        # header + extension blocks, 72 pointers each
        groups = [data_blocks[i:i + HT_SIZE] for i in range(0, max(nd, 1), HT_SIZE)] if nd else [[]]
        blocks = [hdr] + [self._alloc() for _ in groups[1:]]
        for gi, (blk_no, grp) in enumerate(zip(blocks, groups)):
            blk = self._blk(blk_no)
            if gi == 0:
                struct.pack_into(">III", blk, 0, T_HEADER, hdr, len(grp))
                struct.pack_into(">I", blk, 16, data_blocks[0] if data_blocks else 0)
                struct.pack_into(">I", blk, 320, protect_from_flags(flags))
                struct.pack_into(">I", blk, 324, len(data))
                struct.pack_into(">III", blk, 420, *_amiga_date(self.when))
                self._put_name(blk, name)
                struct.pack_into(">I", blk, 500, parent.block)
                struct.pack_into(">i", blk, 508, ST_FILE)
            else:
                struct.pack_into(">III", blk, 0, T_LIST, blk_no, len(grp))
                struct.pack_into(">I", blk, 500, hdr)
                struct.pack_into(">i", blk, 508, ST_FILE)
            for i, p in enumerate(grp):
                struct.pack_into(">I", blk, 308 - 4 * i, p)
            if gi + 1 < len(blocks):
                struct.pack_into(">I", blk, 504, blocks[gi + 1])
            self._seal(blk_no)
        self._link(parent, name, hdr)

    def finish(self):
        # boot block (no boot code here: the code comes from the user's own disk, see --bootblock-from)
        boot = self.img[0:2 * BSIZE]
        boot[0:4] = b"DOS" + bytes([1 if not self.ofs else 0])
        struct.pack_into(">I", boot, 4, 0)
        struct.pack_into(">I", boot, 8, ROOT)
        struct.pack_into(">I", boot, 4, (~_bootsum(bytes(boot))) & 0xFFFFFFFF)
        self.img[0:2 * BSIZE] = boot
        # bitmap (set bit = free), block 2 is bit 0 of the first long
        bm = bytearray(BSIZE)
        for blk in range(2, NBLOCKS):
            if self.free[blk]:
                i = blk - 2
                w = 1 + i // 32
                v = struct.unpack_from(">I", bm, 4 * w)[0] | (1 << (i % 32))
                struct.pack_into(">I", bm, 4 * w, v)
        struct.pack_into(">I", bm, 0, checksum_block(bytes(bm), 0))
        self.img[BITMAP * BSIZE:(BITMAP + 1) * BSIZE] = bm
        # root block (hash table already filled in by _link)
        r = self._blk(ROOT)
        struct.pack_into(">III", r, 0, T_HEADER, 0, 0)
        struct.pack_into(">I", r, 12, HT_SIZE)
        struct.pack_into(">i", r, 312, -1)
        struct.pack_into(">I", r, 316, BITMAP)
        d = _amiga_date(self.when)
        struct.pack_into(">III", r, 420, *d)
        self._put_name(r, self.volume)
        struct.pack_into(">III", r, 472, *d)
        struct.pack_into(">III", r, 484, *d)
        struct.pack_into(">i", r, 508, ST_ROOT)
        self._seal(ROOT)
        return bytes(self.img)


# ---------------------------------------------------------------------------------------------- reader

class Entry:
    def __init__(self, path, is_dir, size, prot, block, data=None):
        self.path, self.is_dir, self.size, self.prot, self.block, self.data = path, is_dir, size, prot, block, data


class AdfReader:
    def __init__(self, path_or_bytes):
        if isinstance(path_or_bytes, (bytes, bytearray)):
            self.img = bytes(path_or_bytes)
        else:
            with open(path_or_bytes, "rb") as f:
                self.img = f.read()
        if len(self.img) != NBLOCKS * BSIZE:
            raise AdfError("not a DD 880 KB image (%d bytes)" % len(self.img))
        if self.img[0:3] != b"DOS":
            raise AdfError("not an AmigaDOS disk (boot block says %r)" % self.img[0:4])
        self.flags = self.img[3]
        self.ofs = not (self.flags & 1)
        self.intl = bool(self.flags & 6)
        self.problems = []
        self.used = {0, 1, ROOT}  # blocks reachable from the root (plus the boot blocks), filled by entries()
        self.volume = self._name(self._b(ROOT))

    def _b(self, n):
        if not 0 <= n < NBLOCKS:
            raise AdfError("block %d out of range" % n)
        return self.img[n * BSIZE:(n + 1) * BSIZE]

    @staticmethod
    def _name(b):
        n = b[432]
        return b[433:433 + min(n, 30)].decode("latin-1")

    def _verify(self, n, b):
        if checksum_block(b) != struct.unpack_from(">I", b, 20)[0]:
            self.problems.append("block %d: bad checksum" % n)

    def entries(self):
        """All files and directories, depth first, as Entry objects with paths relative to the root."""
        out = []
        self.used = {0, 1, ROOT}
        self._verify(ROOT, self._b(ROOT))
        self._walk(ROOT, "", out, set())
        return out

    def _walk(self, dblock, prefix, out, seen):
        b = self._b(dblock)
        for i in range(HT_SIZE):
            n = struct.unpack_from(">I", b, 24 + 4 * i)[0]
            while n:
                if n in seen:
                    self.problems.append("block %d: loop in a hash chain" % n)
                    break
                seen.add(n)
                self.used.add(n)
                e = self._b(n)
                self._verify(n, e)
                typ = struct.unpack_from(">i", e, 0)[0]
                sec = struct.unpack_from(">i", e, 508)[0]
                path = prefix + self._name(e)
                prot = struct.unpack_from(">I", e, 320)[0]
                if typ == T_HEADER and sec == ST_USERDIR:
                    out.append(Entry(path, True, 0, prot, n))
                    self._walk(n, path + "/", out, seen)
                elif typ == T_HEADER and sec == ST_FILE:
                    size = struct.unpack_from(">I", e, 324)[0]
                    out.append(Entry(path, False, size, prot, n, self._file_data(n, size)))
                else:
                    self.problems.append("block %d: unsupported entry (type %d, secondary %d), skipped" % (n, typ, sec))
                n = struct.unpack_from(">I", e, 496)[0]

    def _file_data(self, hdr, size):
        ptrs = []
        n = hdr
        guard = 0
        while n:
            guard += 1
            if guard > 100000:
                raise AdfError("extension chain too long")
            b = self._b(n)
            self._verify(n, b)
            self.used.add(n)
            cnt = struct.unpack_from(">I", b, 8)[0]
            for i in range(min(cnt, HT_SIZE)):
                ptrs.append(struct.unpack_from(">I", b, 308 - 4 * i)[0])
            n = struct.unpack_from(">I", b, 504)[0]
        out = bytearray()
        for p in ptrs:
            self.used.add(p)
            blk = self._b(p)
            if self.ofs:
                self._verify(p, blk)
                dsz = struct.unpack_from(">I", blk, 12)[0]
                out += blk[24:24 + min(dsz, 488)]
            else:
                out += blk
        if len(out) < size:
            self.problems.append("block %d: file shorter than its size" % hdr)
        return bytes(out[:size])

    def check(self):
        self.entries()
        # bitmap (one bitmap block covers a DD disk): checksum, then used (clear bit) against the blocks the tree references
        bn = struct.unpack_from(">I", self._b(ROOT), 316)[0]
        self.used.add(bn)
        bm = self._b(bn)
        if checksum_block(bm, 0) != struct.unpack_from(">I", bm, 0)[0]:
            self.problems.append("bitmap block: bad checksum")
        for blk in range(2, NBLOCKS):
            i = blk - 2
            free = bool(struct.unpack_from(">I", bm, 4 + 4 * (i // 32))[0] >> (i % 32) & 1)
            if blk in self.used and free:
                self.problems.append("block %d is in use but marked free in the bitmap" % blk)
            elif blk not in self.used and not free:
                self.problems.append("block %d is marked used in the bitmap but nothing references it" % blk)
        return self.problems


# ---------------------------------------------------------------------------------------------- command line

def _rules(args):
    rules = []
    for a in args:
        if "=" not in a:
            raise AdfError("--protect wants PATTERN=FLAGS, got %r" % a)
        pat, fl = a.rsplit("=", 1)
        protect_from_flags(fl)
        rules.append((pat.lower(), fl))
    return rules


def _flags_for(path, rules):
    low = path.lower()
    for pat, fl in rules:
        if fnmatch.fnmatchcase(low, pat) or fnmatch.fnmatchcase(low.rsplit("/", 1)[-1], pat):
            return fl
    return None


def cmd_create(argv):
    name, ofs, bootfrom = "HWTEST", False, None
    protect, from_adf, mkdirs, files = [], [], [], []
    pos = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--name":
            name = argv[i + 1]; i += 2
        elif a == "--ofs":
            ofs = True; i += 1
        elif a == "--bootblock-from":
            bootfrom = argv[i + 1]; i += 2
        elif a == "--name-from":
            name = AdfReader(argv[i + 1]).volume; i += 2
        elif a in ("--protect", "--from-adf", "--mkdir", "--file"):
            {"--protect": protect, "--from-adf": from_adf, "--mkdir": mkdirs, "--file": files}[a].append(argv[i + 1]); i += 2
        else:
            pos.append(a); i += 1
    if len(pos) != 2:
        raise AdfError("usage: mkadf.py create <dir|-> <out.adf> [options]")
    src, out = pos
    explicit = _rules(protect)
    # plan: path -> (data, flags or None to take the rule/default); a later source replaces an earlier file of the same name
    plan, dirs = {}, []

    def put(path, data, flags=None):
        plan[path.lower()] = (path, data, flags)

    if src != "-":
        for root, ds, fs in os.walk(src):
            ds.sort()
            rel = os.path.relpath(root, src)
            rel = "" if rel == "." else rel.replace(os.sep, "/")
            for d in ds:
                dirs.append((rel + "/" + d).lstrip("/"))
            for f in sorted(fs):
                with open(os.path.join(root, f), "rb") as fh:
                    put((rel + "/" + f).lstrip("/"), fh.read())
    dirs += mkdirs
    for spec in from_adf:
        srcadf, _, rest = spec.partition(":")
        path, _, dest = rest.partition("=")
        r = AdfReader(srcadf)
        want = path.strip("/").lower()
        hit = False
        for e in r.entries():
            low = e.path.lower()
            if not want or low == want or low.startswith(want + "/"):
                hit = True
                rel = e.path[len(want):].lstrip("/")
                target = "/".join(x for x in (dest.strip("/"), rel) if x) if dest else e.path
                if e.is_dir:
                    dirs.append(target)
                else:
                    put(target, e.data, flags_from_protect(e.prot).replace("-", ""))  # keep the source's bits
        if not hit:
            raise AdfError("%s: %s not found" % (srcadf, path))
    for spec in files:
        dest, local = spec.split("=", 1)
        with open(local, "rb") as fh:
            put(dest, fh.read())
    w = AdfWriter(name, ofs)
    for d in dirs:
        w.mkdir(d)
    for path, data, flags in plan.values():
        fl = _flags_for(path, explicit) or flags or "rwed"
        w.add_file(path, data, fl)
    img = bytearray(w.finish())
    if bootfrom:
        # keep the other disk's boot code (bytes 12..1023), then recompute our boot block checksum
        old = AdfReader(bootfrom).img[:2 * BSIZE]
        img[12:2 * BSIZE] = old[12:2 * BSIZE]
        struct.pack_into(">I", img, 8, ROOT)
        struct.pack_into(">I", img, 4, 0)
        struct.pack_into(">I", img, 4, (~_bootsum(bytes(img[:2 * BSIZE]))) & 0xFFFFFFFF)
    with open(out, "wb") as fh:
        fh.write(img)
    print("%s: %s %s, %d KB used, %d KB free" % (out, name, "OFS" if ofs else "FFS", w.used_bytes() // 1024, w.free_bytes() // 1024))


def cmd_ls(argv):
    r = AdfReader(argv[0])
    print("%s (%s%s)" % (r.volume, "OFS" if r.ofs else "FFS", ", intl" if r.intl else ""))
    for e in r.entries():
        print("%s %8s  %s" % (flags_from_protect(e.prot), "Dir" if e.is_dir else e.size, e.path))
    for p in r.problems:
        print("PROBLEM:", p)
    return 1 if r.problems else 0


def cmd_extract(argv):
    r = AdfReader(argv[0])
    dest = argv[1]
    want = [p.strip("/").lower() for p in argv[2:]]
    for e in r.entries():
        low = e.path.lower()
        if want and not any(low == w or low.startswith(w + "/") for w in want):
            continue
        t = os.path.join(dest, *e.path.split("/"))
        if e.is_dir:
            os.makedirs(t, exist_ok=True)
        else:
            os.makedirs(os.path.dirname(t), exist_ok=True)
            with open(t, "wb") as fh:
                fh.write(e.data)
    for p in r.problems:
        print("PROBLEM:", p, file=sys.stderr)
    return 1 if r.problems else 0


def cmd_cat(argv):
    r = AdfReader(argv[0])
    want = argv[1].strip("/").lower()
    for e in r.entries():
        if e.path.lower() == want and not e.is_dir:
            sys.stdout.buffer.write(e.data)
            return 0
    print("%s not found" % argv[1], file=sys.stderr)
    return 1


def cmd_check(argv):
    r = AdfReader(argv[0])
    p = r.check()
    for x in p:
        print("PROBLEM:", x)
    print("%s: %s, %d entries, %d problems" % (argv[0], r.volume, len(r.entries()), len(p)))
    return 1 if p else 0


def main(argv):
    cmds = {"create": cmd_create, "ls": cmd_ls, "extract": cmd_extract, "cat": cmd_cat, "check": cmd_check}
    if len(argv) < 2 or argv[1] not in cmds:
        print(__doc__)
        return 2
    try:
        return cmds[argv[1]](argv[2:]) or 0
    except (AdfError, OSError, IndexError, ValueError) as ex:
        print("mkadf.py: %s" % ex, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
