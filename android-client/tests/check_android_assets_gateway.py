"""Exercise the real gateway with recovered resources and isolated saves (WSL)."""
import hashlib
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
SERVER = ROOT / 'Durango-CustomServer/server'
BUNDLES = SERVER / 'assetbundles/android'
REPORT = ROOT / 'android-client/dist/assetbundles-android-5.2.1.json'
BASE = 'http://127.0.0.1:19790'


def fetch(path):
    with urllib.request.urlopen(BASE + path, timeout=30) as response:
        result = hashlib.sha256()
        size = 0
        while block := response.read(1024 * 1024):
            result.update(block)
            size += len(block)
        assert response.status == 200
        assert int(response.headers['Content-Length']) == size
        if '/android/' in path and path.split('?')[0].endswith('.bundle'):
            assert response.headers['Content-Type'] == 'application/octet-stream'
            assert 'immutable' in response.headers['Cache-Control']
            assert response.headers.get('ETag')
        elif path.split('?')[0].endswith('/Info.5.2.1.json'):
            assert response.headers['Content-Type'].startswith('application/json')
            assert 'no-store' in response.headers['Cache-Control']
        return result.hexdigest()


def main():
    report = json.loads(REPORT.read_text())
    with tempfile.TemporaryDirectory(prefix='durango-android-assets-') as temp:
        base = Path(temp)
        data = base / 'data'
        data.mkdir()
        for path in (SERVER / 'data').iterdir():
            if path.is_dir():
                (data / path.name).symlink_to(path, target_is_directory=True)
            else:
                shutil.copy2(path, data / path.name)
        (base / 'assetbundles').mkdir()
        bundle_dir = base / 'assetbundles/android'
        bundle_dir.mkdir()
        for path in BUNDLES.iterdir():
            # FileInfo.Length no Linux pode medir o link, em vez do payload que FileStream abre.
            # A cópia mantém o fixture fiel aos arquivos reais do volume usado no staging.
            shutil.copy2(path, bundle_dir / path.name)
        (bundle_dir / 'private.txt').write_text('Arquivo de controle; não publicar.')
        (bundle_dir / 'Info.5.2.1.json.tmp').write_text('{}')
        log_path = base / 'gateway.log'
        with log_path.open('w') as log:
            process = subprocess.Popen([
                str(SERVER / 'bin/test-runtime-linux/dotnet'),
                str(SERVER / 'bin/Debug/net9.0/DurangoServer.dll'),
                '--name', 'Android assets QA', '--storage-key', 'android-assets-qa',
                '--data', str(data), '--gateway-port', '19790', '--game-port', '19791',
                '--terrains', str(SERVER / 'data/terrains')],
                cwd=temp, stdout=log, stderr=subprocess.STDOUT)
            try:
                for attempt in range(90):
                    if process.poll() is not None:
                        raise RuntimeError(log_path.read_text()[-2000:])
                    try:
                        with urllib.request.urlopen(BASE + '/status', timeout=1):
                            break
                    except OSError:
                        time.sleep(0.5)
                else:
                    raise RuntimeError('Gateway não iniciou: ' + log_path.read_text()[-2000:])
                with urllib.request.urlopen(BASE + '/knock?platform=Android&version=5.2.1') as response:
                    knock = json.load(response)
                assert knock['assetbundle_index_url'] == BASE + '/live/android/Info.5.2.1.json'
                assert knock['assetbundle_url_root'] == BASE + '/live/android/'
                assert fetch('/live/android/Info.5.2.1.json') == report['index_sha256']
                for count, entry in enumerate(report['bundles'], 1):
                    encoded = urllib.parse.quote(entry['filename'], safe='$')
                    assert fetch('/live/android/' + encoded) == entry['sha256'], entry['filename']
                    if count % 200 == 0:
                        print(f'Verificados por HTTP: {count}/{report["available"]}', flush=True)
                preload = next(entry for entry in report['bundles'] if entry['name'] == 'preload.bundle')
                for prefix in ('/release/android/', '/assetbundles/android/'):
                    assert fetch(prefix + preload['filename']) == preload['sha256']
                assert fetch('/live/android/' + preload['filename'] + '?cache=1') == preload['sha256']
                request = urllib.request.Request(BASE + '/live/android/' + preload['filename'], headers={
                    'If-None-Match': '"' + preload['filename'] + '"'})
                try:
                    urllib.request.urlopen(request)
                except urllib.error.HTTPError as error:
                    assert error.code == 304
                    assert error.read() == b''
                else:
                    raise AssertionError('Cache do bundle não retornou 304.')
                wrong_crc = preload['filename'].rsplit('.', 2)[0] + '.' + '0' * 32 + '.bundle'
                for filename in (wrong_crc, 'private.txt', 'Info.5.2.1.json.tmp'):
                    try:
                        fetch('/live/android/' + filename)
                    except urllib.error.HTTPError as error:
                        assert error.code == 404
                    else:
                        raise AssertionError('Arquivo indevido foi publicado: ' + filename)
                encoded_bundle = next(entry for entry in report['bundles'] if '$' in entry['filename'])
                assert fetch('/live/android/' + urllib.parse.quote(encoded_bundle['filename'], safe='')) == encoded_bundle['sha256']
                for unsafe in ('%2FInfo.5.2.1.json', '%5CInfo.5.2.1.json', '..%2FInfo.5.2.1.json', 'C%3AInfo.5.2.1.json'):
                    try:
                        fetch('/live/android/' + unsafe)
                    except urllib.error.HTTPError as error:
                        assert error.code == 400
                    else:
                        raise AssertionError('Caminho inválido foi aceito: ' + unsafe)
                missing = report['missing_bundles'][0]
                filename = missing['Name'][:-7] + '.' + missing['Crc'] + '.bundle'
                try:
                    fetch('/live/android/' + urllib.parse.quote(filename, safe='$'))
                except urllib.error.HTTPError as error:
                    assert error.code == 404
                else:
                    raise AssertionError('Bundle ausente recebeu resposta de sucesso.')
                with urllib.request.urlopen(BASE + '/knock?platform=Windows&version=5.2.1') as response:
                    assert '/live/windows/' in json.load(response)['assetbundle_url_root']
                with urllib.request.urlopen(BASE + '/knock?version=5.2.1') as response:
                    assert '/live/windows/' in json.load(response)['assetbundle_url_root']
                assets = list((SERVER / 'data/assets').rglob('*.json'))
                for path in assets:
                    route = '/assets/' + path.relative_to(SERVER / 'data/assets').as_posix()[:-5]
                    assert fetch(route) == hashlib.sha256(path.read_bytes()).hexdigest(), route
                for region in ('pe10gr_1', 'grass_company_safehouse_01', 'ri35te'):
                    with urllib.request.urlopen(BASE + '/terrains/' + region) as response:
                        terrain_info = json.load(response)
                    assert terrain_info
                    for route in ('whole_biomes', '0,0', 'ocean/0,0', 'rivers/0,0'):
                        fetch('/terrains/' + region + '/' + route)
                # Downloads móveis simultâneos não podem impedir o handshake do PC.
                with ThreadPoolExecutor(max_workers=4) as pool:
                    downloads = [pool.submit(fetch, '/live/android/' + urllib.parse.quote(entry['filename'], safe='$'))
                                 for entry in sorted(report['bundles'], key=lambda entry: entry['bytes'], reverse=True)[:4]]
                    with urllib.request.urlopen(BASE + '/knock?platform=Windows', timeout=5) as response:
                        assert json.load(response)['compatible'] is True
                    for task, entry in zip(downloads, sorted(report['bundles'], key=lambda entry: entry['bytes'], reverse=True)[:4]):
                        assert task.result() == entry['sha256']
                result = {'available_bundles_verified_over_http': report['available'],
                          'index_unchanged': True, 'automatic_directory_detection': True,
                          'missing_returns_404': True, 'windows_route_preserved': True,
                          'alternate_android_routes_verified': True,
                          'wrong_crc_rejected': True, 'private_files_not_served': True,
                          'bundle_cache_304_verified': True, 'shared_data_tables_verified': len(assets),
                          'terrain_routes_verified': True, 'pc_handshake_during_mobile_downloads': True}
                output = ROOT / 'android-client/work/android-assets-gateway-check.json'
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_text(json.dumps(result, indent=2) + '\n')
                print(json.dumps(result), flush=True)
            finally:
                process.send_signal(signal.SIGINT)
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.terminate()
                    process.wait(timeout=5)
                saved_log = ROOT / 'android-client/work/android-assets-gateway-check.log'
                saved_log.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(log_path, saved_log)


if __name__ == '__main__':
    main()
