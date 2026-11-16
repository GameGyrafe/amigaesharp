#!/usr/bin/env python3
"""Tests for mkadf.py: python3 -I tools/hwpack/test_mkadf.py  (standard library only)."""
import os
import subprocess
import sys
import tempfile
import unittest

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import mkadf  # noqa: E402


class RoundTrip(unittest.TestCase):
    def build(self, ofs):
        w = mkadf.AdfWriter("HWTEST", ofs=ofs, when=1_800_000_000)
        big = bytes((i * 7) % 251 for i in range(100_000))  # > 72 data blocks: needs extension blocks
        files = {
            "RunAll": (b"; script\n", "srwed"),
            "prog": (b"\x00\x00\x03\xf3" * 300, "rwed"),
            "results/a.txt": (b"x", "rwed"),
            "deep/er/est/file.bin": (big, "rwed"),
            "empty": (b"", "rwed"),
            "Case/Mixed": (b"m" * 512, "rwed"),
        }
        for p, (d, f) in files.items():
            w.add_file(p, d, f)
        w.mkdir("emptydir")
        return w.finish(), files

    def check(self, ofs):
        img, files = self.build(ofs)
        r = mkadf.AdfReader(img)
        self.assertEqual(r.volume, "HWTEST")
        self.assertEqual(r.ofs, ofs)
        got = {e.path: e for e in r.entries()}
        self.assertEqual(r.problems, [])
        self.assertEqual(r.check(), [])
        for p, (d, f) in files.items():
            e = got[p]
            self.assertFalse(e.is_dir)
            self.assertEqual(e.data, d, p)
            self.assertEqual(mkadf.flags_from_protect(e.prot).replace("-", ""), f.replace("s", "s"), p)
        self.assertTrue(got["emptydir"].is_dir)
        self.assertTrue(got["deep/er"].is_dir)
        # protection of a script: s set, e allowed; rwed all allowed (bits 0-3 clear)
        self.assertEqual(got["RunAll"].prot, 0x40)
        self.assertEqual(got["prog"].prot, 0)
        # boot block checksum
        self.assertEqual(mkadf._bootsum(img[:1024]), 0xFFFFFFFF)

    def test_protection_bits(self):
        # dos/dos.h: FIBB_ARCHIVE 4, PURE 5, SCRIPT 6, HOLD 7; rwed are inverted (set = denied). List order: h s p a r w e d
        self.assertEqual(mkadf.flags_from_protect(0x60), "-sp-rwed")
        self.assertEqual(mkadf.flags_from_protect(0x50), "-s-arwed")
        self.assertEqual(mkadf.flags_from_protect(0x10), "---arwed")
        self.assertEqual(mkadf.flags_from_protect(0x80), "h---rwed")
        self.assertEqual(mkadf.flags_from_protect(0x0F), "--------")
        self.assertEqual(mkadf.protect_from_flags("sp"), 0x60 | 0x0F)
        self.assertEqual(mkadf.protect_from_flags("arwed"), 0x10)
        for p in (0, 0x40, 0x50, 0xF5):
            self.assertEqual(mkadf.protect_from_flags(mkadf.flags_from_protect(p).replace("-", "")), p)

    def test_check_finds_bitmap_errors(self):
        w = mkadf.AdfWriter()
        w.add_file("a", b"x" * 2000)
        img = bytearray(w.finish())
        self.assertEqual(mkadf.AdfReader(bytes(img)).check(), [])
        bad = bytearray(img)
        # mark a used block free again (and fix the bitmap checksum): check must notice
        blk = mkadf.BITMAP * 512
        used = [b for b in range(2, mkadf.NBLOCKS) if not w.free[b] and b not in (mkadf.ROOT, mkadf.BITMAP)][0]
        i = used - 2
        off = blk + 4 + 4 * (i // 32)
        v = int.from_bytes(bad[off:off + 4], "big") | (1 << (i % 32))
        bad[off:off + 4] = v.to_bytes(4, "big")
        bad[blk:blk + 4] = mkadf.checksum_block(bytes(bad[blk:blk + 512]), 0).to_bytes(4, "big")
        self.assertTrue(any("marked free" in p for p in mkadf.AdfReader(bytes(bad)).check()))

    def test_ffs(self):
        self.check(False)

    def test_ofs(self):
        self.check(True)

    def test_disk_full(self):
        w = mkadf.AdfWriter()
        with self.assertRaises(mkadf.AdfError):
            w.add_file("big", bytes(900 * 1024))

    def test_duplicate_and_names(self):
        w = mkadf.AdfWriter()
        w.add_file("A", b"1")
        with self.assertRaises(mkadf.AdfError):
            w.add_file("a", b"2")
        with self.assertRaises(mkadf.AdfError):
            w.add_file("x" * 31, b"")

    def test_hash_collisions(self):
        w = mkadf.AdfWriter()
        names = ["f%d" % i for i in range(300)]  # many more than 72 slots: chains
        for n in names:
            w.add_file(n, n.encode())
        r = mkadf.AdfReader(w.finish())
        got = {e.path: e.data for e in r.entries()}
        self.assertEqual(got, {n: n.encode() for n in names})

    def test_cli(self):
        with tempfile.TemporaryDirectory() as t:
            src = os.path.join(t, "src")
            os.makedirs(os.path.join(src, "sub"))
            with open(os.path.join(src, "RunAll"), "wb") as f:
                f.write(b"hi\n")
            with open(os.path.join(src, "sub", "p"), "wb") as f:
                f.write(b"prog")
            adf = os.path.join(t, "o.adf")
            run = lambda *a: subprocess.run([sys.executable, "-I", os.path.join(here, "mkadf.py"), *a], capture_output=True, text=True)
            r = run("create", src, adf, "--name", "T", "--protect", "RunAll=srwed")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertEqual(os.path.getsize(adf), 901120)
            self.assertRegex(run("ls", adf).stdout, r"-s--rwed\s+3\s+RunAll")
            self.assertIn("RunAll", run("ls", adf).stdout)
            self.assertEqual(run("check", adf).returncode, 0)
            dest = os.path.join(t, "out")
            self.assertEqual(run("extract", adf, dest).returncode, 0)
            with open(os.path.join(dest, "sub", "p"), "rb") as f:
                self.assertEqual(f.read(), b"prog")
            # copy from another adf with a rename, replacing a file of the same name
            adf2 = os.path.join(t, "o2.adf")
            r = run("create", "-", adf2, "--from-adf", adf + ":sub=moved", "--bootblock-from", adf)
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertEqual(run("cat", adf2, "moved/p").stdout, "prog")
            # empty source path with a destination: the whole disk goes under dest/
            adf3 = os.path.join(t, "o3.adf")
            r = run("create", "-", adf3, "--from-adf", adf + ":=dest")
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertEqual(run("cat", adf3, "dest/sub/p").stdout, "prog")


if __name__ == "__main__":
    unittest.main()
