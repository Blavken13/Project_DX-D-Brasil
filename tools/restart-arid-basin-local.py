"""Gracefully restart the identified local server, retaining its environment and saves."""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import shutil
from pathlib import Path
import subprocess
import time
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT / 'Durango-CustomServer/server'
RUNTIME = ROOT / 'New Dinos/Desert Maps/Generated_Arid_Basin/Runtime'
TERRAIN = 'arid_basin_60_v2'
BASE = 'http://127.0.0.1:8190'


def request(path):
    with urllib.request.urlopen(BASE + path, timeout=20) as response:
        return response.read()


def original_process(pid):
    code = (f'Get-CimInstance Win32_Process -Filter "ProcessId = {pid}" | '
            'Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Compress')
    value = json.loads(subprocess.check_output(['powershell.exe', '-NoProfile', '-Command', code], text=True))
    if not value or value['ProcessId'] != pid:
        raise RuntimeError('The expected server process is not running')
    shell = ctypes.WinDLL('shell32', use_last_error=True)
    shell.CommandLineToArgvW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(ctypes.c_int)]
    shell.CommandLineToArgvW.restype = ctypes.POINTER(wintypes.LPWSTR)
    count = ctypes.c_int()
    values = shell.CommandLineToArgvW(value['CommandLine'], ctypes.byref(count))
    args = [values[i] for i in range(count.value)]
    ctypes.windll.kernel32.LocalFree(values)
    expected = (SERVER / 'bin/Debug/net9.0/DurangoServer.dll').resolve()
    if Path(args[1]).resolve() != expected or args[args.index('--storage-key') + 1] != 'Durango Brasil':
        raise RuntimeError('Process does not match the known local Debug server and storage namespace')
    if Path(args[args.index('--data') + 1]).resolve() != (SERVER / 'data').resolve():
        raise RuntimeError('The server uses an unexpected data directory')
    return args


def process_environment(pid):
    # Retain the existing process environment (including optional DURANGO_*
    # administration settings) only in memory. Never log or persist its contents.
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                         ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
    handle = kernel.OpenProcess(0x0400 | 0x0010, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    class BasicInfo(ctypes.Structure):
        _fields_ = [('reserved', ctypes.c_void_p), ('peb', ctypes.c_void_p),
                    ('reserved2', ctypes.c_void_p * 2), ('pid', ctypes.c_size_t), ('reserved3', ctypes.c_void_p)]
    def read(address, size):
        buffer = ctypes.create_string_buffer(size)
        length = ctypes.c_size_t()
        ok = kernel.ReadProcessMemory(handle, address, buffer, size, ctypes.byref(length))
        if not ok and not length.value:
            raise ctypes.WinError(ctypes.get_last_error())
        return buffer.raw[:length.value]
    try:
        info = BasicInfo()
        status = ctypes.WinDLL('ntdll').NtQueryInformationProcess(handle, 0, ctypes.byref(info), ctypes.sizeof(info), None)
        if status:
            raise RuntimeError('Could not read the existing server process configuration')
        parameters = int.from_bytes(read(info.peb + 0x20, 8), 'little')
        address = int.from_bytes(read(parameters + 0x80, 8), 'little')
        raw = b''
        for offset in range(0, 1024 * 1024, 4096):
            raw += read(address + offset, 4096)
            decoded = raw.decode('utf-16-le', errors='strict')
            if '\0\0' in decoded:
                entries = decoded.split('\0\0', 1)[0].split('\0')
                return dict(entry.split('=', 1) for entry in entries if '=' in entry and not entry.startswith('='))
        raise RuntimeError('Could not read a complete environment block')
    finally:
        kernel.CloseHandle(handle)


def stop_gracefully(pid):
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.FreeConsole()
    if not kernel.AttachConsole(pid):
        raise RuntimeError('Cannot attach to the local server console; no forceful shutdown was attempted')
    # CTRL_C may be ignored by a child of an earlier restart helper. CTRL_BREAK
    # still reaches .NET Console.CancelKeyPress and runs the server save hook.
    handler = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.DWORD)(lambda event: True)
    kernel.SetConsoleCtrlHandler(handler, True)
    try:
        if not kernel.GenerateConsoleCtrlEvent(1, 0):
            raise ctypes.WinError(ctypes.get_last_error())
        time.sleep(.25)
    finally:
        kernel.FreeConsole()
        kernel.SetConsoleCtrlHandler(handler, False)
        kernel.SetConsoleCtrlHandler(None, False)
    for attempt in range(80):
        query = subprocess.run(['powershell.exe', '-NoProfile', '-Command',
                                f'if (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ exit 1 }}'],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if query.returncode == 0:
            return
        time.sleep(.25)
    raise RuntimeError('Server did not exit after Ctrl+C; no forceful shutdown was attempted')


def backup_saves():
    state = SERVER / 'AppData-nx'
    if not state.resolve().is_relative_to(SERVER.resolve()):
        raise RuntimeError('Unexpected save directory')
    records = {str(p.relative_to(state)): hashlib.sha256(p.read_bytes()).hexdigest()
               for p in state.rglob('*') if p.is_file()}
    if not records:
        raise RuntimeError('Expected local saves were not found')
    backup = ROOT / ('tools/state-before-arid-' + time.strftime('%Y%m%d-%H%M%S') + '.zip')
    with zipfile.ZipFile(backup, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name in records:
            archive.write(state / name, 'AppData-nx/' + name.replace('\\', '/'))
    with zipfile.ZipFile(backup) as archive:
        for name, digest in records.items():
            if hashlib.sha256(archive.read('AppData-nx/' + name.replace('\\', '/'))).hexdigest() != digest:
                raise RuntimeError('Save backup verification failed')
    return backup, records


def verify_live():
    animal = json.loads(request('/assets/entity_types/animal'))['2213']
    if animal['framework'] != 'Giganotosaurus' or animal['model_path'] != 'Giganotosaurus/GiganotosaurusPrefab':
        raise RuntimeError('The client is not receiving the Giganotosaurus model definition')
    info = json.loads(request('/terrains/' + TERRAIN))
    if len(info.get('global_landmarks') or []) != 1:
        raise RuntimeError('The running server still clears the 3D landmark')
    if not any(x['prefab'] == 'Models/Maps/AridBasin/AridBasinTerrain.prefab' for x in info['landmarks']):
        raise RuntimeError('The running server does not advertise the actual visual mesh')
    with zipfile.ZipFile(SERVER / ('data/terrains/' + TERRAIN + '.zip')) as archive:
        expected = archive.read('whole.biomes')
    actual = request('/terrains/' + TERRAIN + '/whole_biomes')
    other = request('/terrains/ua60de_alpha/whole_biomes')
    if actual != expected or actual == other:
        raise RuntimeError('Live terrain map is stale or identical to the sibling island')
    bundles = []
    for platform in ('windows', 'android'):
        index = json.loads(request('/live/' + platform + '/Info.5.2.1.json'))
        for key in ('models$maps$aridbasin$aridbasinterrain.prefab.bundle',
                    'models$animals$giganotosaurus$giganotosaurusprefab.prefab.bundle',
                    'models$animals$giganotosaurus$giganotosaurus_framework.asset.bundle'):
            entry = next(e for e in index['FileList'] if e['Name'] == key)
            if entry['Priority'] <= 0:
                raise RuntimeError('Required bundle is not preloaded')
            name = key[:-7] + '.' + entry['Crc'] + '.bundle'
            data = request('/live/' + platform + '/' + urllib.parse.quote(name))
            if hashlib.md5(data).hexdigest() != entry['Crc'] or len(data) != entry['Size']:
                raise RuntimeError('A required bundle is not served correctly: ' + name)
            bundles.append(dict(platform=platform, name=name, bytes=len(data), download_verified=True))
    return dict(terrain_id=TERRAIN, landmark=info['landmarks'], distinct_coastline=True,
                giganotosaurus_model_definition_verified=True, bundles=bundles)


def restart(pid):
    args = original_process(pid)
    environment = process_environment(pid)
    old_status = json.loads(request('/status'))
    stop_gracefully(pid)
    backup, records = backup_saves()
    launch(args, environment, backup, records, pid, old_status)


def launch(args, environment, backup, records, pid=None, old_status=None):
    args += ['--assetbundles-windows', str(ROOT / 'Lost-Horizon-PC/Lost Horizon Game_Data/StreamingAssets/AssetBundles'),
             '--assetbundles-android', str(SERVER / 'assetbundles/android')]
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    stdout_path, stderr_path = ROOT / 'tools/arid-server.stdout.log', ROOT / 'tools/arid-server.stderr.log'
    with stdout_path.open('wb') as out, stderr_path.open('wb') as error:
        child = subprocess.Popen(args, cwd=ROOT, env=environment, stdout=out, stderr=error,
                                 startupinfo=startup, creationflags=subprocess.CREATE_NEW_CONSOLE)
    environment.clear()
    for attempt in range(60):
        if child.poll() is not None:
            raise RuntimeError('Updated local server exited; inspect tools/arid-server.stderr.log')
        try:
            if json.loads(request('/status')).get('ok'):
                break
        except Exception:
            pass
        time.sleep(.5)
    else:
        raise RuntimeError('Updated local server did not become ready')
    report = verify_live()
    report.update(old_pid=pid, new_pid=child.pid, configuration='Debug', backup=str(backup),
                  backup_files_verified=len(records), previous_online=(old_status or {}).get('online'))
    log = stdout_path.read_text(encoding='utf-8-sig', errors='replace')
    report['live_giganotosaurus_spawn_logged'] = 'Giganotosaurus único lv60 em 128,128' in log
    if not report['live_giganotosaurus_spawn_logged']:
        raise RuntimeError('Updated server did not log the unique central Giganotosaurus spawn')
    report['character_files_preserved'] = all(hashlib.sha256((SERVER / 'AppData-nx' / name).read_bytes()).hexdigest() == digest
                                               for name, digest in records.items()
                                               if name.endswith('.player') or name.endswith('accounts.json'))
    RUNTIME.mkdir(parents=True, exist_ok=True)
    (RUNTIME / 'live-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))


def start():
    import socket
    for port in (8190, 8191):
        with socket.socket() as connection:
            connection.settimeout(1)
            if connection.connect_ex(('127.0.0.1', port)) == 0:
                raise RuntimeError(f'Port {port} is already in use; no second server was started')
    backup, records = backup_saves()
    args = [shutil.which('dotnet.exe') or 'dotnet.exe', str(SERVER / 'bin/Debug/net9.0/DurangoServer.dll'),
            '--name', 'Durango Brasil-local', '--storage-key', 'Durango Brasil',
            '--data', str(SERVER / 'data'), '--terrains', str(SERVER / 'data/terrains'),
            '--public-host', '127.0.0.1', '--gateway-port', '8190', '--game-port', '8191', '--max-players', '2']
    launch(args, os.environ.copy(), backup, records)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int)
    parser.add_argument('--verify-only', action='store_true')
    parser.add_argument('--start', action='store_true')
    options = parser.parse_args()
    if options.verify_only:
        print(json.dumps(verify_live(), ensure_ascii=False, indent=2))
    elif options.start:
        start()
    elif options.pid:
        restart(options.pid)
    else:
        parser.error('--pid or --verify-only is required')
