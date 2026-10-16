"""Operate the explicitly authorized USB test phone without printing credentials."""
import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import build_apk as shared
ADB = shared.SDK / 'platform-tools/adb.exe'
OUT = ROOT / 'work/engine-login-50226/device'
PACKAGE = 'com.nexon.durango.global'


def adb(*args, binary=False, check=True):
    r = subprocess.run([str(ADB), '-s', '725b9a01', *args], capture_output=True)
    if check and r.returncode:
        raise RuntimeError(r.stderr.decode('utf-8', errors='replace') + r.stdout.decode('utf-8', errors='replace'))
    return r.stdout if binary else r.stdout.decode('utf-8', errors='replace').strip()


def capture(name):
    path = OUT / (name + '.png')
    path.write_bytes(adb('exec-out', 'screencap', '-p', binary=True))
    log = OUT / 'relevant-logcat.txt'
    if log.exists(): (OUT / (name + '.log')).write_bytes(log.read_bytes())
    print('Screenshot: ' + str(path))


def status():
    print('PID:', adb('shell', 'pidof', PACKAGE, check=False))
    print(adb('shell', 'dumpsys', 'activity', 'activities').split('mResumedActivity:')[-1][:220])
    pid = adb('shell', 'pidof', PACKAGE, check=False)
    logs = adb('logcat', '-d', '-v', 'threadtime', '--pid=' + pid) if pid else adb('logcat', '-d', '-v', 'threadtime', '-t', '1500')
    lines = [l for l in logs.splitlines() if re.search(r'ENGINE_|LHCrash|LHNative|FATAL EXCEPTION|Fatal signal|NoSuchMethod|UnsatisfiedLink|AndroidRuntime|JNI DETECTED|Exception|Error:|Failed|missing|Could not|Unity|LHRuntime', l)]
    safe = '\n'.join(lines)
    safe = re.sub(r'(?i)((?:accessToken|refreshToken|password|authorization|token)\s*[:=]\s*)\S+', r'\1<redacted>', safe)
    (OUT / 'relevant-logcat.txt').write_text(safe, 'utf-8')
    print(safe[-14000:])
    print(adb('shell', 'run-as', PACKAGE, 'ls', 'files', check=False))
    journal = adb('shell', 'run-as', PACKAGE, 'cat', 'files/diagnostics/runtime-events.txt', check=False)
    journal = '\n'.join(l for l in journal.splitlines() if re.search(r'ENGINE_|ACCOUNT_|SESSION_', l))
    (OUT / 'runtime-events.txt').write_text(journal, 'utf-8'); print(journal[-4500:])


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--launch', action='store_true')
    p.add_argument('--status', action='store_true')
    p.add_argument('--capture')
    p.add_argument('--tap', nargs=2, type=int)
    p.add_argument('--key', type=int)
    p.add_argument('--text')
    p.add_argument('--info', action='store_true')
    p.add_argument('--install', action='store_true')
    p.add_argument('--record-tests', action='store_true')
    args = p.parse_args(); OUT.mkdir(parents=True, exist_ok=True)
    if args.install: print(adb('install', '-r', str(ROOT / 'dist/LostHorizon-login-no-jogo-50226.apk')))
    if args.info:
        values = {prop: adb('shell', 'getprop', prop) for prop in
                  ('ro.product.model', 'ro.build.version.sdk', 'ro.build.version.release', 'ro.build.version.security_patch')}
        values['size'] = adb('shell', 'wm', 'size'); values['page_size'] = adb('shell', 'getconf', 'PAGE_SIZE')
        (OUT / 'device.json').write_text(json.dumps(values, indent=2), 'utf-8'); print(json.dumps(values, indent=2))
    if args.launch:
        print(adb('shell', 'am', 'force-stop', PACKAGE))
        print(adb('shell', 'am', 'start', '-W', '-n', PACKAGE + '/com.newdawn.launcher.CompatGameActivity'))
    if args.tap: print(adb('shell', 'input', 'tap', *map(str, args.tap)))
    if args.key: print(adb('shell', 'input', 'keyevent', str(args.key)))
    if args.text: print(adb('shell', 'input', 'text', args.text))
    if args.status: status()
    if args.capture: capture(args.capture)
    if args.record_tests:
        local_hash = hashlib.sha256((ROOT / 'dist/LostHorizon-login-no-jogo-50226.apk').read_bytes()).hexdigest()
        installed = adb('shell', 'pm', 'path', PACKAGE).splitlines()[0].removeprefix('package:').strip()
        installed_hash = adb('shell', 'sha256sum', installed).split()[0]
        assert installed_hash == local_hash, 'Installed APK differs from tested artifact'
        journal = (OUT / 'runtime-events.txt').read_text('utf-8')
        assert all(stage in journal for stage in ('ENGINE_LOGIN_MODAL_VISIBLE', 'ENGINE_AUTH_ACCEPTED', 'ENGINE_CHARACTERS_READY', 'ENGINE_AUTH_REJECTED'))
        physical = {'sha256': local_hash, 'installed_sha256': installed_hash, 'device': json.loads((OUT / 'device.json').read_text()),
                    'pid_final_revision': 23199, 'status': 'login and character list validated; wrong password rejected without closing',
                    'interaction': 'user touched the phone; ADB input rejected with INJECT_EVENTS',
                    'evidence': ['final-modal.png', 'wrong-password.png', 'runtime-events.txt'],
                    'earlier_revision': {'pid': 19808, 'result': 'login, session and tutorial without restarting',
                                         'sha256': '8c6642119203f5b948c86571f8788edcb248d99693da85a26ad36802e9d0f172'},
                    'limitations': ['different phone from Android 16 diagnostic', 'registration, expired session and diagnostic options not physically tested']}
        (OUT / 'physical-validation.json').write_text(json.dumps(physical, indent=2) + '\n', 'utf-8')
        print(json.dumps(physical, indent=2))
