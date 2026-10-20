"""Install the compiled Spino combat fix and gracefully restart the known local server."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('local_restart', ROOT / 'tools/restart-arid-basin-local.py')
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--pid', type=int, required=True)
parser.add_argument('--stop-only', action='store_true')
options = parser.parse_args()
args = helper.original_process(options.pid)
if options.stop_only:
    helper.stop_gracefully(options.pid)
    backup, records = helper.backup_saves()
    print(json.dumps({'stopped_pid': options.pid, 'save_backup': str(backup), 'save_files': len(records)}, indent=2))
    raise SystemExit(0)
environment = helper.process_environment(options.pid)
source = ROOT / 'pc-client/work/spino-server-build'
destination = ROOT / 'Durango-CustomServer/server/bin/Debug/net9.0'
for filename in ('DurangoServer.dll', 'DurangoServer.pdb'):
    if not (source / filename).is_file():
        raise RuntimeError('Build the Spino server before restarting it')
helper.stop_gracefully(options.pid)
backup, records = helper.backup_saves()
runtime_backup = ROOT / ('pc-client/work/spino-server-before-' + time.strftime('%Y%m%d-%H%M%S'))
runtime_backup.mkdir(parents=True)
for filename in ('DurangoServer.dll', 'DurangoServer.pdb'):
    shutil.copy2(destination / filename, runtime_backup / filename)
    shutil.copy2(source / filename, destination / filename)
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
with (ROOT / 'pc-client/work/spino-server.stdout.log').open('wb') as out, \
     (ROOT / 'pc-client/work/spino-server.stderr.log').open('wb') as err:
    child = subprocess.Popen(args, cwd=ROOT, env=environment, stdout=out, stderr=err,
        startupinfo=startup, creationflags=subprocess.CREATE_NEW_CONSOLE)
environment.clear()
for attempt in range(60):
    if child.poll() is not None:
        raise RuntimeError('Updated server exited; inspect pc-client/work/spino-server.stderr.log')
    try:
        status = json.loads(helper.request('/status'))
        shapes = json.loads(helper.request('/assets/animal_body_shapes'))
        if status.get('ok') and len(shapes['2214']['circles']) == 24:
            break
    except Exception:
        pass
    time.sleep(.5)
else:
    raise RuntimeError('Server did not become ready with the updated body shape')
print(json.dumps({'old_pid': options.pid, 'new_pid': child.pid, 'save_backup': str(backup),
    'save_files': len(records), 'runtime_backup': str(runtime_backup), 'body_sections': 24,
    'attack_padding': shapes['2214']['attack_padding']}, indent=2))
