"""Install a verified, additive Android resource patch into the active asset bind.

Run on staging with archive and manifest paths; never update a catalog, container
or account volume. Existing payloads must be identical, otherwise abort.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tarfile
import urllib.parse
import urllib.request


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text())
    assert digest(args.archive.read_bytes()) == manifest['archive_sha256']
    inspect = lambda: json.loads(subprocess.check_output(['docker', 'inspect', 'durango-brasil-staging']))[0]
    before = inspect()
    mount = next(m for m in before['Mounts'] if m['Destination'] == '/app/assetbundles/android')
    assert mount['Type'] == 'bind' and mount['RW'] is False
    assets = Path(mount['Source']).resolve()
    assert assets.is_relative_to('/opt/durango/releases')
    index = assets / 'Info.5.2.1.json'
    assert digest(index.read_bytes()) == manifest['catalog_sha256']
    catalog = {e['Name']: e for e in json.loads(index.read_text())['FileList']}
    expected = {e['server_filename']: e for e in manifest['bundles']}
    assert len(expected) == len(manifest['bundles']) > 0
    payloads = {}
    with tarfile.open(args.archive) as package:
        for member in package.getmembers():
            assert member.isfile() and member.name in expected
            assert Path(member.name).name == member.name
            entry = expected[member.name]
            assert catalog[entry['catalog_entry']['Name']] == entry['catalog_entry']
            assert member.size == entry['bytes']
            data = package.extractfile(member).read()
            assert digest(data) == entry['sha256']
            assert data.startswith(b'UnityFS\0') and b'2017.4.34f1\0' in data[:96]
            target = assets / member.name
            if target.exists():
                assert digest(target.read_bytes()) == entry['sha256'], 'Existing payload differs'
            payloads[member.name] = data
    assert payloads.keys() == expected.keys()
    inserted = 0
    for name, data in payloads.items():
        target = assets / name
        if target.exists():
            continue
        temp = assets / (name + '.tmp')
        temp.write_bytes(data)
        temp.chmod(0o644)
        os.replace(temp, target)
        inserted += 1
    assert digest(index.read_bytes()) == manifest['catalog_sha256']
    for name, entry in expected.items():
        url = 'http://127.0.0.1:8190/live/android/' + urllib.parse.quote(name, safe='$')
        with urllib.request.urlopen(url, timeout=15) as response:
            assert response.status == 200
            assert digest(response.read()) == entry['sha256']
    after = inspect()
    assert after['State']['Pid'] == before['State']['Pid']
    assert after['Mounts'] == before['Mounts']
    print(json.dumps({'result': 'PASS', 'inserted': inserted, 'http_verified': len(expected),
          'catalog_unchanged': True, 'account_volume_untouched': True, 'server_restarted': False}))


if __name__ == '__main__':
    main()
