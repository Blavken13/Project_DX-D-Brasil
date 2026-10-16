"""Collect local ADB evidence and launch Lost Horizon without clearing app data.

Example: python android-client/diagnose_poco_f7.py --serial 10.168.149.53:34803
Optional --install PATH performs an update only (never uninstall/clear).
Raw logs stay in ignored android-client/work, because they may contain private data.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parent
ADB = Path.home() / 'AppData/Local/Android/Sdk/platform-tools/adb.exe'
PACKAGE = 'com.nexon.durango.global'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial', required=True)
    parser.add_argument('--install', type=Path)
    parser.add_argument('--seconds', type=int, default=45)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 300:
        parser.error('--seconds must be between 1 and 300')
    if args.install and not args.install.is_file():
        parser.error('APK does not exist')
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    output = ROOT / 'work' / ('poco-f7-device-' + stamp)
    output.mkdir(parents=True)
    commands = []

    def run(name, *command, device=True, timeout=30):
        argv = [str(ADB)] + (['-s', args.serial] if device else []) + list(command)
        result = subprocess.run(argv, capture_output=True, timeout=timeout)
        (output / (name + '.txt')).write_bytes(result.stdout + result.stderr)
        commands.append(dict(name=name, returncode=result.returncode))
        return result

    run('connect', 'connect', args.serial, device=False, timeout=15)
    state = run('state', 'get-state')
    if state.returncode or state.stdout.strip() != b'device':
        (output / 'summary.json').write_text(json.dumps(dict(
            status='blocked_before_launch', serial=args.serial, commands=commands,
            cause='ADB session unavailable; pairing/authorization or updated connection port required',
            crash_cause='unknown: application was not executed'), indent=2), 'utf-8')
        print(f'ADB unavailable. No game launch/install performed. Evidence: {output}')
        return 2
    properties = {}
    for prop in ('ro.product.manufacturer', 'ro.product.model', 'ro.product.device',
                 'ro.build.version.release', 'ro.build.version.sdk',
                 'ro.build.display.id', 'ro.product.cpu.abilist'):
        result = run(prop.replace('.', '_'), 'shell', 'getprop', prop)
        properties[prop] = result.stdout.decode('utf-8', 'replace').strip()
    page = run('kernel-page-size', 'shell', 'getconf', 'PAGE_SIZE')
    properties['kernel_page_size'] = page.stdout.decode('utf-8', 'replace').strip()
    run('screen-size', 'shell', 'wm', 'size')
    run('package-before', 'shell', 'dumpsys', 'package', PACKAGE)
    run('exit-info-before', 'shell', 'dumpsys', 'activity', 'exit-info', PACKAGE)
    run('crash-before', 'logcat', '-b', 'crash', '-d', '-v', 'threadtime')
    if args.install:
        installed = run('install-update', 'install', '-r', str(args.install.resolve()), timeout=240)
        if installed.returncode or b'Success' not in installed.stdout:
            print(f'Update failed; app data preserved. Inspect: {output}')
            return 3
    # Starting the existing launcher retains credentials and normal authentication.
    # The operator may need to tap Login/Start during the observation window.
    launch = run('launch', 'shell', 'am', 'start', '-W', '-n',
                 PACKAGE + '/com.newdawn.launcher.OriginalAuthActivity')
    print(f'Launcher requested; observe/tap Login or Start if required. Collecting {args.seconds}s. Logs: {output}', flush=True)
    time.sleep(args.seconds)
    run('crash-after', 'logcat', '-b', 'crash', '-d', '-v', 'threadtime')
    run('runtime-log', 'logcat', '-d', '-t', '20000', '-v', 'threadtime',
        'Unity:I', 'LHService:I', 'AndroidRuntime:E', 'DEBUG:I', 'linker:E', '*:S')
    run('exit-info-after', 'shell', 'dumpsys', 'activity', 'exit-info', PACKAGE)
    run('process-after', 'shell', 'pidof', PACKAGE)
    run('memory-after', 'shell', 'dumpsys', 'meminfo', PACKAGE)
    run('activity-after', 'shell', 'dumpsys', 'activity', 'activities')
    # Only the explicit diagnostic directory, never authentication preferences.
    run('private-diagnostic-list', 'shell', 'run-as', PACKAGE, 'ls', '-R', 'files/diagnostics')
    (output / 'summary.json').write_text(json.dumps(dict(
        serial=args.serial, properties=properties, commands=commands,
        launch_requested=launch.returncode == 0,
        validation='Logs require analysis; launcher start does not confirm entry into Unity or gameplay',
        raw_logs='local only; review/redact before sharing'), indent=2), 'utf-8')
    print(f'Capture complete: {output}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
