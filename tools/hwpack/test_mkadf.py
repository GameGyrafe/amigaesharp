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
        return w.finish(bootable=True), files

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
        self.assertEqual(got["RunAll"].prot, 0x10)
        self.assertEqual(got["prog"].prot, 0)
        # boot block checksum
        self.assertEqual(mkadf._bootsum(img[:1024]), 0xFFFFFFFF)

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


if __name__ == "__main__":
    unittest.main()
