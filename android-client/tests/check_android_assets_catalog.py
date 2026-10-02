"""Validate the server's Android readiness audit without creating accounts or saves (WSL)."""
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SERVER = ROOT / 'Durango-CustomServer/server'
COMMAND = [str(SERVER / 'bin/test-runtime-linux/dotnet'),
           str(SERVER / 'bin/Debug/net9.0/DurangoServer.dll'), '--android-assets-check']


def audit(directory):
    result = subprocess.run(COMMAND + ['--assetbundles-android', str(directory)],
                            capture_output=True, text=True, encoding='utf-8', timeout=30)
    report, _ = json.JSONDecoder().raw_decode(result.stdout[result.stdout.index('{'):])
    return result.returncode, report


def main():
    code, recovered = audit(SERVER / 'assetbundles/android')
    assert code == 3 and recovered['complete'] is False
    prepared = json.loads((ROOT / 'android-client/dist/assetbundles-android-5.2.1.json').read_text())
    assert recovered['available'] == prepared['available']
    assert recovered['missing'] == prepared['missing']
    assert recovered['prerequisites_ready'] is False
    assert recovered['prerequisite_dependencies_missing']
    tutorial = json.loads((ROOT / 'android-client/bundled/tutorial/manifest.json').read_text())['bundles']
    assert not ({entry['name'] for entry in tutorial} &
                set(recovered['prerequisite_dependencies_missing']))
    assert recovered['invalid_bundle_headers'] == []
    assert recovered['undeclared_dependencies'] == []
    with tempfile.TemporaryDirectory(prefix='durango-android-catalog-') as temp:
        directory = Path(temp)
        crc = '1' * 32
        entries = [
            {'Name': 'required.bundle', 'Crc': crc, 'Hash': crc, 'Priority': 999,
             'Dependencies': ['material.bundle']},
            {'Name': 'material.bundle', 'Crc': crc, 'Hash': crc, 'Priority': 490,
             'Dependencies': ['texture.bundle']},
            {'Name': 'texture.bundle', 'Crc': crc, 'Hash': crc, 'Priority': 490,
             'Dependencies': []}]
        index = {'FileList': entries, 'ItemList': [], 'PreloadCrc': crc, 'PreloadHash': crc}
        index_path = directory / 'Info.5.2.1.json'
        index_path.write_text(json.dumps(index))
        # Minimal header fixtures: this audit checks headers; the HTTP test verifies the real payloads.
        for name in ('required', 'material', 'texture', 'preload'):
            (directory / (name + '.' + crc + '.bundle')).write_bytes(b'UnityFS\0fixture\0' + b'2017.4.34f1\0')
        code, report = audit(directory)
        assert code == 0 and report['complete'] is True and report['prerequisites_ready'] is True
        texture = directory / ('texture.' + crc + '.bundle')
        texture.unlink()
        code, report = audit(directory)
        assert code == 3 and report['prerequisite_dependencies_missing'] == ['texture.bundle']
        texture.write_bytes(b'UnityFS\0fixture\0' + b'2017.4.7f1\0')
        code, report = audit(directory)
        assert code == 0 and report['invalid_bundle_headers'] == []
        texture.write_bytes(b'not a Unity bundle')
        code, report = audit(directory)
        assert code == 3 and report['invalid_bundle_headers'] == [texture.name]
        index_path.write_text('{broken json')
        code, report = audit(directory)
        assert code == 2 and report['complete'] is False and 'error' in report
        index['FileList'] = [None]
        index_path.write_text(json.dumps(index))
        code, report = audit(directory)
        assert code == 2 and report['complete'] is False and 'error' in report
    output = ROOT / 'android-client/work/android-assets-catalog-check.json'
    output.write_text(json.dumps(recovered, indent=2) + '\n')
    print(json.dumps({'catalog_audit_passed': True, 'available': recovered['available'],
                      'missing': recovered['missing'], 'prerequisite_dependencies_missing':
                      len(recovered['prerequisite_dependencies_missing']),
                      'transitive_dependencies_verified': True, 'invalid_headers_rejected': True,
                      'malformed_index_rejected': True}), flush=True)


if __name__ == '__main__':
    main()
