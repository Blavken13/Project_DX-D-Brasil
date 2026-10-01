"""WSL fixture: all accounts and saves go into a temporary directory."""
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path('/mnt/c/code/ProjectDX/Project_DX-D-Brasil')
SERVER = ROOT / 'Durango-CustomServer/server'
DATA = SERVER / 'data'
with tempfile.TemporaryDirectory(prefix='durango-apk-auth-') as temp:
    data = Path(temp) / 'data'
    data.mkdir()
    for path in DATA.iterdir():
        if path.is_dir():
            (data / path.name).symlink_to(path, target_is_directory=True)
        else:
            shutil.copy2(path, data / path.name)
    log_path = ROOT / 'android-client/work/gateway-qa.log'
    with log_path.open('w') as log:
        process = subprocess.Popen([
            str(SERVER / 'bin/test-runtime-linux/dotnet'),
            str(SERVER / 'bin/Debug/net9.0/DurangoServer.dll'),
            '--name', 'Android QA', '--storage-key', 'apk-qa',
            '--data', str(data), '--gateway-port', '19690', '--game-port', '19691',
            '--terrains', str(DATA / 'terrains'), '--cluster-mode', 'Online'],
            stdout=log, stderr=subprocess.STDOUT, cwd=temp)
        try:
            for attempt in range(60):
                if process.poll() is not None:
                    raise RuntimeError('QA gateway stopped: ' + log_path.read_text()[-1200:])
                try:
                    with urllib.request.urlopen('http://127.0.0.1:19690/status', timeout=1) as response:
                        if response.status == 200:
                            break
                except OSError:
                    time.sleep(0.5)
            else:
                raise RuntimeError('QA gateway did not start')
            print('READY: isolated gateway http://127.0.0.1:19690', flush=True)
            stop = ROOT / 'android-client/work/stop-gateway-qa'
            stop.unlink(missing_ok=True)
            deadline = time.monotonic() + 300
            while time.monotonic() < deadline and not stop.exists():
                time.sleep(0.5)
        finally:
            process.send_signal(signal.SIGINT)
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.terminate()
                process.wait(timeout=5)
            print('QA gateway stopped; temporary accounts removed.', flush=True)
