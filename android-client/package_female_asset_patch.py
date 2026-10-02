"""Package only the verified female appearance recovery for additive staging delivery."""
import hashlib
import json
from pathlib import Path
import tarfile

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / 'recovered/upstream-android'
WORK = ROOT / 'work/lost-horizon/female-resources'
SERVER = ROOT.parent / 'Durango-CustomServer/server/assetbundles/android'


def main():
    inventory = json.loads((SOURCE / 'inventory.json').read_text('utf-8'))
    names = set(inventory['female_appearance_dependencies']['recovered_names'])
    entries = [e for e in inventory['bundles'] if e['catalog_entry']['Name'] in names]
    assert len(entries) == len(names) == 321
    WORK.mkdir(parents=True, exist_ok=True)
    archive = WORK / 'female-assets-update.tar.gz'
    with tarfile.open(archive, 'w:gz', compresslevel=1) as package:
        for entry in entries:
            path = SOURCE / 'bundles-android' / entry['server_filename']
            data = path.read_bytes()
            assert len(data) == entry['bytes']
            assert hashlib.sha256(data).hexdigest() == entry['sha256']
            assert (SERVER / entry['server_filename']).read_bytes() == data
            package.add(path, arcname=entry['server_filename'], recursive=False)
    manifest = {'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
                'catalog_sha256': hashlib.sha256((SERVER / 'Info.5.2.1.json').read_bytes()).hexdigest(),
                'bundles': entries}
    (WORK / 'female-assets-update.json').write_text(json.dumps(manifest, indent=2) + '\n', 'utf-8')
    print(f'Female asset patch: {len(entries)} bundles, {archive.stat().st_size} bytes')


if __name__ == '__main__':
    main()
