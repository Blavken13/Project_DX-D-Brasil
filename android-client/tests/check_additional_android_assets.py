"""Verify verified extra resources survive preparation and corrupt overlays are rejected."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    with tempfile.TemporaryDirectory(prefix='durango-extra-assets-') as temp:
        base = Path(temp)
        source = base / 'source'
        extra = base / 'extra'
        source.mkdir()
        (extra / 'bundles-android').mkdir(parents=True)
        crc = '1' * 32
        entry = {'Name': 'body.bundle', 'Crc': crc, 'Hash': crc,
                 'Size': 64, 'Priority': 495, 'Dependencies': []}
        index = {'FileList': [entry], 'ItemList': [], 'PreloadCrc': crc, 'PreloadHash': crc}
        index_bytes = json.dumps(index).encode()
        (source / 'Info.5.2.1.json').write_bytes(index_bytes)
        index_sha = hashlib.sha256(index_bytes).hexdigest()
        header = b'UnityFS\0fixture\0' + b'2017.4.34f1\0'
        preload = 'preload.' + crc + '.bundle'
        (source / preload).write_bytes(header)
        inventory = {'summary': {'source_index_sha256': index_sha}, 'bundles': [
            {'server_filename': preload, 'sha256': hashlib.sha256(header).hexdigest()}]}
        (base / 'inventory.json').write_text(json.dumps(inventory))
        name = 'body.' + crc + '.bundle'
        body = extra / 'bundles-android' / name
        body.write_bytes(header)
        extra_inventory = {'source_index_sha256': index_sha, 'bundles': [
            {'server_filename': name, 'sha256': hashlib.sha256(header).hexdigest(),
             'catalog_entry': entry}]}
        manifest = extra / 'inventory.json'
        manifest.write_text(json.dumps(extra_inventory))
        archive = base / 'assets.tar.gz'
        command = [sys.executable, str(ROOT / 'prepare_android_assets.py'),
                   '--source', str(source), '--inventory', str(base / 'inventory.json'),
                   '--additional', str(extra), '--output', str(base / 'prepared'),
                   '--archive', str(archive)]
        result = subprocess.run(command, capture_output=True)
        assert result.returncode == 0, result.stderr
        report = json.loads((base / 'assets.json').read_text())
        assert report['available'] == 2 and report['missing'] == 0
        assert (base / 'prepared' / name).read_bytes() == header
        with tarfile.open(archive) as t:
            assert t.extractfile('assetbundles/android/' + name).read() == header
        body.write_bytes(header + b'tampered')
        result = subprocess.run(command, capture_output=True)
        assert result.returncode != 0 and b'Hash divergente' in result.stderr
        body.write_bytes(header)
        extra_inventory['bundles'][0]['catalog_entry'] = dict(entry, Priority=490)
        manifest.write_text(json.dumps(extra_inventory))
        result = subprocess.run(command, capture_output=True)
        assert result.returncode != 0 and b'Metadados divergentes' in result.stderr
        extra_inventory['source_index_sha256'] = '0' * 64
        manifest.write_text(json.dumps(extra_inventory))
        result = subprocess.run(command, capture_output=True)
        assert result.returncode != 0 and b'outro' in result.stderr
    print('PASS: supplemental bundle included; tampered hash, metadata and index rejected.')


if __name__ == '__main__':
    main()
