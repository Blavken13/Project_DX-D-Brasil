"""Read focused estate diagnostics and update the connected tester in place."""
import argparse
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ADB = Path('C:/Users/Ander/AppData/Local/Android/Sdk/platform-tools/adb.exe')
PACKAGE = 'com.losthorizon.tester'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--serial', required=True)
    parser.add_argument('action', choices=('status', 'logs', 'capture', 'install', 'launch'))
    parser.add_argument('--label', default='estate')
    parser.add_argument('--apk', type=Path, default=ROOT / 'dist/losthorizon-build-tester-007.apk')
    args = parser.parse_args()
    work = ROOT / 'work/app-tester-007' / ('device-' + args.serial)
    work.mkdir(parents=True, exist_ok=True)

    def adb(*command):
        result = subprocess.run([str(ADB), '-s', args.serial, *map(str, command)], capture_output=True)
        if result.returncode:
            raise RuntimeError((result.stdout + result.stderr).decode('utf-8', 'replace'))
        return result.stdout

    if args.action == 'status':
        package = adb('shell', 'dumpsys', 'package', PACKAGE).decode('utf-8', 'replace')
        pid = adb('shell', 'pidof', PACKAGE).decode().strip()
        result = dict(serial=args.serial, pid=pid,
                      package_lines=[line.strip() for line in package.splitlines()
                                     if re.search(r'versionCode=|versionName=|lastUpdateTime=|primaryCpuAbi=', line)])
        (work / 'status.json').write_text(json.dumps(result, indent=2) + '\n', 'utf-8')
        print(json.dumps(result, indent=2))
    elif args.action == 'logs':
        pid = adb('shell', 'pidof', PACKAGE).decode().strip().split()[0]
        data = adb('logcat', '--pid=' + pid, '-d', '-v', 'threadtime', '-s',
                   'AppTester:I', 'FanNotice:I', '*:S').decode('utf-8', 'replace')
        lines = [line for line in data.splitlines()
                 if 'AppTester:' in line or ('FanNotice:' in line and 'prologue not relocatable' in line)]
        text = '\n'.join(lines) + '\n'
        (work / (args.label + '.log')).write_text(text, 'utf-8')
        print(text)
    elif args.action == 'capture':
        window = adb('shell', 'dumpsys', 'window').decode('utf-8', 'replace')
        focus = next((line for line in window.splitlines() if 'mCurrentFocus=' in line), '')
        if PACKAGE not in focus:
            raise RuntimeError('Capture skipped: the tester game is not in the foreground')
        data = adb('exec-out', 'screencap', '-p')
        assert data.startswith(b'\x89PNG\r\n\x1a\n')
        output = work / (args.label + '.png')
        output.write_bytes(data)
        print(output)
    elif args.action == 'install':
        assert args.apk.is_file()
        print(adb('install', '--no-incremental', '-r', args.apk).decode('utf-8', 'replace'))
    elif args.action == 'launch':
        print(adb('shell', 'am', 'start', '-W', '-n',
                  PACKAGE + '/com.durango.offserver.DurangoActivity').decode('utf-8', 'replace'))


if __name__ == '__main__':
    main()
