#!/usr/bin/env python3
"""Helpers for the AmigaOS 4.1 (Pegasos II) QEMU VM that `esharp run/test --target os4` boots. Run with `python3 -I`.

  os4vm.py check                       check the VM directory (ESHARP_OS4_VM) and the host tools
  os4vm.py start [--write] [--iso F]   boot the VM with its screen on VNC 127.0.0.1:5901 (Ctrl+C stops it); without
                                       --write it runs on a throwaway overlay, so amiga.img is not changed
  os4vm.py install-hook [--docker IMG] one time: add the E# test hook to S:User-Startup in amiga.img (see README.md)
  os4vm.py remove-hook [--docker IMG]  put back the S:User-Startup saved by install-hook

The VM directory holds your own AmigaOS files (never copy them into the repo): see README.md for the layout.
install-hook and remove-hook boot the VM on an overlay, type one command into a Shell through the QEMU monitor, check the
result on the serial output, and only then merge the overlay into amiga.img (qemu-img commit).
start, install-hook and remove-hook take /tmp/esharp-os4vm.lock, like esharp run/test --target os4, and wait for it.
"""
import contextlib, fcntl, os, shutil, signal, socket, subprocess, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_IMAGE = 'esharp-amiga-toolchain'
# The marker is printed after the changed files were written; the filesystem and the virtual disk then get this long (the
# script waits, and so does the host) before the monitor's quit, which is a power-off with no shutdown.
FLUSH_SECS = 15


def _terminate(signum, frame):
    raise SystemExit(128 + signum)   # so the finally blocks run and no qemu is left behind


for _sig in (signal.SIGTERM, signal.SIGHUP):
    signal.signal(_sig, _terminate)


def die(msg):
    print('os4vm: ' + msg, file=sys.stderr)
    sys.exit(1)


LOCK_FILE = '/tmp/esharp-os4vm.lock'


@contextlib.contextmanager
def vm_lock():
    """The lock `esharp run/test --target os4` takes for a run (flock on the same file; the CLI's FileShare.None is a flock
    too), so start and the hook commands never run while a test run uses the VM, and the other way round."""
    f = open(LOCK_FILE, 'a+')
    said = False
    try:
        while True:
            try:
                fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if not said:
                    print('os4vm: waiting for another os4 run to finish...', file=sys.stderr)
                    said = True
                time.sleep(2)
        yield
    finally:
        f.close()   # closing drops the flock


def vm_dir():
    d = os.environ.get('ESHARP_OS4_VM')
    if not d:
        die('set ESHARP_OS4_VM to your AmigaOS 4.1 VM dir (see tools/os4vm/README.md)')
    d = os.path.abspath(os.path.expanduser(d))
    missing = [f for f in ('bboot', 'Kickstart.zip', 'amiga.img') if not os.path.isfile(os.path.join(d, f))]
    if missing:
        die(f'{d} lacks {", ".join(missing)} (see tools/os4vm/README.md)')
    return d


def vm_busy(vm):
    """PIDs of running qemu-system-ppc processes that use this VM directory (only one VM at a time may use amiga.img)."""
    pids = []
    for pid in os.listdir('/proc'):
        if not pid.isdigit():
            continue
        try:
            with open(f'/proc/{pid}/cmdline', 'rb') as f:
                args = f.read().split(b'\0')
        except OSError:
            continue
        if not args or os.path.basename(args[0]) != b'qemu-system-ppc':
            continue
        try:
            cwd = os.readlink(f'/proc/{pid}/cwd')
        except OSError:
            cwd = ''
        if cwd == vm or any(vm.encode() in a for a in args):
            pids.append(int(pid))
    return pids


def qemu_args(vm, disk, disk_format, iso, serial, monitor, vnc=True):
    data = os.path.join(vm, 'qemu-data')
    a = ['qemu-system-ppc']
    if os.path.isdir(data):
        a += ['-L', data]
    a += ['-L', '/usr/share/qemu', '-M', 'pegasos2', '-m', '2048',
          '-kernel', os.path.join(vm, 'bboot'), '-initrd', os.path.join(vm, 'Kickstart.zip'), '-append', 'serial',
          '-vga', 'none', '-device', 'sm501', '-rtc', 'base=localtime',
          '-display', 'vnc=127.0.0.1:1' if vnc else 'none',
          '-serial', 'file:' + serial]
    if monitor:
        a += ['-monitor', 'unix:' + monitor + ',server,nowait']
    if iso:
        a += ['-drive', f'format=raw,file={iso},index=2,media=cdrom,readonly=on']
    a += ['-drive', f'format={disk_format},file={disk},index=0,media=disk']
    return a


class Monitor:
    KEYS = {' ': 'spc', ':': 'shift-semicolon', '.': 'dot', '/': 'slash', '-': 'minus', '_': 'shift-minus', '\n': 'ret',
            '>': 'shift-dot', '"': 'shift-apostrophe', '=': 'equal'}

    def __init__(self, path, timeout=20):
        deadline = time.time() + timeout
        while True:
            try:
                self.s = socket.socket(socket.AF_UNIX)
                self.s.connect(path)
                break
            except OSError:
                if time.time() > deadline:
                    raise
                time.sleep(0.5)
        self.s.settimeout(0.3)
        self.drain()

    def drain(self):
        try:
            while self.s.recv(65536):
                pass
        except socket.timeout:
            pass

    def cmd(self, c):
        self.s.sendall((c + '\n').encode())
        time.sleep(0.05)
        self.drain()

    def type(self, text):
        for ch in text:
            k = self.KEYS.get(ch) or ('shift-' + ch.lower() if ch.isupper() else ch)
            self.cmd('sendkey ' + k)


def build_estype(work, image):
    """Compiles estype.c into work/cd/estype (ppc-amigaos-gcc on this machine, else in the toolchain image)."""
    src = os.path.join(HERE, 'estype.c')
    if shutil.which('ppc-amigaos-gcc'):
        subprocess.run(['ppc-amigaos-gcc', '-O2', '-o', os.path.join(work, 'cd', 'estype'), src], check=True)
        return
    shutil.copy(src, os.path.join(work, 'estype.c'))
    subprocess.run(['docker', 'run', '--rm', '--user', f'{os.getuid()}:{os.getgid()}', '-v', f'{work}:/w', '-w', '/w', image,
                    'ppc-amigaos-gcc', '-O2', '-o', 'cd/estype', 'estype.c'], check=True)


def make_iso(cd, iso, image):
    for f in os.listdir(cd):
        os.chmod(os.path.join(cd, f), 0o755)   # Rock Ridge modes become the Amiga protection bits: executable
    tool = shutil.which('genisoimage') or shutil.which('xorrisofs') or shutil.which('mkisofs')
    if tool:
        subprocess.run([tool, '-quiet', '-R', '-V', 'ESTEST', '-o', iso, cd], check=True)
        return
    d = os.path.dirname(iso)
    subprocess.run(['docker', 'run', '--rm', '--user', f'{os.getuid()}:{os.getgid()}', '-v', f'{d}:/w', '-w', '/w', image,
                    'genisoimage', '-quiet', '-R', '-V', 'ESTEST', '-o', os.path.basename(iso), os.path.relpath(cd, d)], check=True)


# The hook is appended to S:User-Startup between the BEGIN/END ESHARP-TEST comment lines. With no volume ESTEST: (or no
# ESTEST:autorun on it) it does nothing, so Workbench boots as before; Assign EXISTS does not ask for a missing volume.
# The script runs synchronously, before LoadWB: a program's own screen then stays in front (with Run, Workbench and its
# WBStartup window came up over the cards sample's screen), and the autorun script can still Run things itself.
HOOK = [
    ';BEGIN ESHARP-TEST (E# os4 test harness hook, see tools/os4vm/README.md in the E# repo)',
    'Assign >NIL: ESTEST: EXISTS',
    'If NOT WARN',
    '  If EXISTS ESTEST:autorun',
    '    Execute ESTEST:autorun',
    '  EndIf',
    'EndIf',
    ';END ESHARP-TEST',
]

INSTALL = ['; Adds the E# test hook to S:User-Startup (the old file is kept as S:User-Startup.pre-esharp)',
           'FailAt 21',
           'Search >NIL: S:User-Startup "BEGIN ESHARP-TEST"',
           'If NOT WARN',
           '  ESTEST:estype -t "ESHARP-HOOK already installed"',
           '  Quit',
           'EndIf',
           'Copy S:User-Startup S:User-Startup.pre-esharp CLONE',
           'Echo >>S:User-Startup ""'] + \
          [f'Echo >>S:User-Startup "{line}"' for line in HOOK] + \
          ['ESTEST:estype S:User-Startup', 'Wait %d SECS' % FLUSH_SECS, 'ESTEST:estype -t "ESHARP-HOOK installed"']

REMOVE = ['; Puts back the S:User-Startup that install-hook saved',
          'FailAt 21',
          'If NOT EXISTS S:User-Startup.pre-esharp',
          '  ESTEST:estype -t "ESHARP-HOOK no saved copy, nothing changed"',
          '  Quit',
          'EndIf',
          'Copy S:User-Startup.pre-esharp S:User-Startup CLONE',
          'Delete >NIL: S:User-Startup.pre-esharp',
          'ESTEST:estype S:User-Startup',
          'Wait %d SECS' % FLUSH_SECS,
          'ESTEST:estype -t "ESHARP-HOOK removed"']

# After the commit, a boot of the new amiga.img (on a fresh overlay) looks for the hook once more.
VERIFY = ['FailAt 21',
          'Search >NIL: S:User-Startup "BEGIN ESHARP-TEST"',
          'If WARN',
          '  ESTEST:estype -t "ESHARP-HOOK verify absent"',
          'Else',
          '  ESTEST:estype -t "ESHARP-HOOK verify present"',
          'EndIf']


def boot_script(vm, base, script, done_words, image, work, base_format='raw'):
    """Boots an overlay of `base` with a CD holding `script` (named change), types Execute into Workbench, and waits for an
    ESHARP-HOOK <word> line. Returns (word or None, serial log, overlay path); a confirmed change stays flushed for
    FLUSH_SECS before the quit. The overlay is left for the caller (qemu-img commit)."""
    cd = os.path.join(work, 'cd')
    shutil.rmtree(cd, ignore_errors=True)
    os.mkdir(cd)
    build_estype(work, image)
    with open(os.path.join(cd, 'change'), 'w') as f:
        f.write('\n'.join(script) + '\n')
    iso = os.path.join(work, 'estest.iso')
    make_iso(cd, iso, image)
    overlay = os.path.join(work, 'overlay-%d.qcow2' % int(time.time() * 1000))
    subprocess.run(['qemu-img', 'create', '-q', '-f', 'qcow2', '-b', base, '-F', base_format, overlay], check=True)
    serial, mon = os.path.join(work, 'serial-%d.log' % int(time.time() * 1000)), os.path.join(work, 'mon.sock')
    q = subprocess.Popen(qemu_args(vm, overlay, 'qcow2', iso, serial, mon), stdin=subprocess.DEVNULL)
    result = None
    try:
        m = Monitor(mon)
        print('os4vm: booting (about 30 s to Workbench)...')
        time.sleep(40)
        for attempt in range(2):
            m.cmd('sendkey meta_r-e')   # Workbench: Execute Command
            time.sleep(2)
            m.type('Execute ESTEST:change\n')
            deadline = time.time() + 30 + FLUSH_SECS
            while time.time() < deadline and result is None:
                time.sleep(1)
                text = open(serial, errors='replace').read()
                for w in done_words:
                    if 'ESHARP-HOOK ' + w in text:
                        result = w
            if result:
                break
        if result:
            time.sleep(FLUSH_SECS)   # the script already waited; this covers the host side of the virtual disk
        m.cmd('quit')
    finally:
        try:
            q.wait(20)
        except subprocess.TimeoutExpired:
            q.kill()
            q.wait()
    return result, open(serial, errors='replace').read(), overlay


def change_startup(script, done_words, image):
    vm = vm_dir()
    img = os.path.join(vm, 'amiga.img')
    if vm_busy(vm):
        die('a qemu-system-ppc is using the VM already; stop it first (one VM at a time)')
    if not shutil.which('qemu-img'):
        die('qemu-img not found')
    work = tempfile.mkdtemp(prefix='esos4-', dir='/tmp')
    try:
        result, log, overlay = boot_script(vm, img, script, done_words, image, work)
        tail = log[max(0, log.find('ESHARP-HOOK') - 2000):] if 'ESHARP-HOOK' in log else log[-1500:]
        print(tail)
        if result is None:
            die('the change was not confirmed on the serial output; amiga.img is unchanged')
        if result not in ('installed', 'removed'):
            print('os4vm: nothing to change; amiga.img is unchanged')
            return
        backup = img + '.bak-' + time.strftime('%Y%m%d-%H%M%S')
        subprocess.run(['cp', '--reflink=auto', img, backup], check=True)
        print(f'os4vm: backup {backup}')
        subprocess.run(['qemu-img', 'commit', '-q', overlay], check=True)
        # Boot the committed image once (on a new overlay) and look at S:User-Startup before reporting success.
        want = 'verify present' if result == 'installed' else 'verify absent'
        v, vlog, _ = boot_script(vm, img, VERIFY, ('verify present', 'verify absent'), image, work)
        if v != want:
            die(f'amiga.img was updated but the check boot saw {v!r}, expected {want!r}; restore {backup} if the disk misbehaves')
        print(f'os4vm: hook {result}; amiga.img updated and checked by a second boot (backup {backup}; delete it when you are happy)')
    finally:
        shutil.rmtree(work, ignore_errors=True)


def start(args):
    vm = vm_dir()
    img = os.path.join(vm, 'amiga.img')
    if vm_busy(vm):
        die('a qemu-system-ppc is using the VM already (one VM at a time)')
    iso = args[args.index('--iso') + 1] if '--iso' in args else None
    work = tempfile.mkdtemp(prefix='esos4-', dir='/tmp')
    try:
        disk, fmt = img, 'raw'
        if '--write' not in args:
            disk, fmt = os.path.join(work, 'overlay.qcow2'), 'qcow2'
            subprocess.run(['qemu-img', 'create', '-q', '-f', 'qcow2', '-b', img, '-F', 'raw', disk], check=True)
        serial = os.path.join(work, 'serial.log')
        print(f'os4vm: VNC 127.0.0.1:5901, serial {serial}' + ('' if fmt == 'raw' else ' (overlay: amiga.img stays unchanged)'))
        subprocess.run(qemu_args(vm, disk, fmt, iso, serial, os.path.join(work, 'mon.sock')))
    except KeyboardInterrupt:
        pass
    finally:
        shutil.rmtree(work, ignore_errors=True)


def check():
    vm = vm_dir()
    ok = True
    for tool in ('qemu-system-ppc', 'qemu-img'):
        print(f'{tool}: {shutil.which(tool) or "MISSING"}')
        ok &= shutil.which(tool) is not None
    print('genisoimage: ' + (shutil.which('genisoimage') or shutil.which('xorrisofs') or 'not on the host (docker image used)'))
    print(f'VM dir: {vm}')
    busy = vm_busy(vm)
    print('VM in use by qemu pid ' + ', '.join(map(str, busy)) if busy else 'VM: not running')
    sys.exit(0 if ok else 1)


def main(argv):
    if not argv or argv[0] in ('-h', '--help'):
        print(__doc__)
        return
    image = argv[argv.index('--docker') + 1] if '--docker' in argv else DEFAULT_IMAGE
    if argv[0] == 'check':
        check()
    elif argv[0] == 'start':
        with vm_lock():
            start(argv[1:])
    elif argv[0] == 'install-hook':
        with vm_lock():
            change_startup(INSTALL, ('installed', 'already installed'), image)
    elif argv[0] == 'remove-hook':
        with vm_lock():
            change_startup(REMOVE, ('removed', 'no saved copy'), image)
    else:
        die(f'unknown command {argv[0]!r} (see --help)')


if __name__ == '__main__':
    main(sys.argv[1:])
