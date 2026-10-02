"""Recover the missing original Android female appearance bundles and dependencies.

Do not substitute models or edit characters. Match the existing source catalog;
validate Android/Unity compatibility before updating the reproducible inventory.
"""
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
sys.path.insert(0, str(ROOT / 'work/python-deps'))
import UnityPy

UPSTREAM = 'http://api.durangonewdawn.com:8190/assetbundles/android/'
SERVER = PROJECT / 'Durango-CustomServer/server/assetbundles/android'
RECOVERY = ROOT / 'recovered/upstream-android'
INDEX = ROOT / 'recovered/android-cache-2026-09-30/bundles-android/Info.5.2.1.json'
REPORT = ROOT / 'work/lost-horizon/female-resources/recovery.json'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def filename(entry):
    return entry['Name'][:-7] + '.' + entry['Crc'] + '.bundle'


def closure(catalog):
    roots = sorted(name for name in catalog if name.startswith('models$pc$female$'))
    assert len(roots) == 366
    pending = list(roots)
    checked = set()
    while pending:
        name = pending.pop()
        if name in checked:
            continue
        checked.add(name)
        pending.extend(catalog[name]['Dependencies'])
    return roots, sorted(checked)


def download(entry):
    name = filename(entry)
    assert Path(name).name == name and '\\' not in name and '/' not in name
    target = RECOVERY / 'bundles-android' / name
    url = UPSTREAM + urllib.parse.quote(name, safe='$')
    if target.is_file():
        data = target.read_bytes()
    else:
        with urllib.request.urlopen(url, timeout=30) as response:
            data = response.read(32 * 1024 * 1024 + 1)
    assert 0 < len(data) <= 32 * 1024 * 1024
    assert data.startswith(b'UnityFS\0') and b'2017.4.34f1\0' in data[:96], name
    env = UnityPy.load(data)
    textures = 0
    for obj in env.objects:
        assert obj.assets_file.unity_version == '2017.4.34f1', name
        assert int(obj.assets_file.target_platform) == 13, name
        if obj.type.name == 'Texture2D':
            texture = obj.read()
            assert texture.image.size == (texture.m_Width, texture.m_Height), name
            textures += 1
    assert env.objects, 'Empty bundle: ' + name
    target.parent.mkdir(exist_ok=True)
    temp = target.with_suffix('.bundle.tmp')
    temp.write_bytes(data)
    temp.replace(target)
    return {'server_filename': name, 'catalog_entry': entry,
            'bytes': len(data), 'sha256': sha(data), 'download_url': url,
            'unity_version': '2017.4.34f1', 'texture_count': textures,
            'validation': 'Android target 13, original catalog metadata, Unity objects and texture decoding'}


def main():
    original = INDEX.read_bytes()
    inventory_path = RECOVERY / 'inventory.json'
    inventory = json.loads(inventory_path.read_text('utf-8'))
    assert sha(original) == inventory['source_index_sha256']
    with urllib.request.urlopen(UPSTREAM + 'Info.5.2.1.json', timeout=30) as response:
        assert sha(response.read()) == sha(original), 'Upstream catalog differs'
    catalog = {entry['Name']: entry for entry in json.loads(original)['FileList']}
    local = {entry['Name']: entry for entry in json.loads((SERVER / 'Info.5.2.1.json').read_text())['FileList']}
    roots, names = closure(catalog)
    # Preserve the four verified tutorial revisions, including the glow material
    # also referenced by cosmetic headgear. Never restore their older payloads.
    tutorial = json.loads((ROOT / 'bundled/tutorial/manifest.json').read_text())['bundles']
    tutorial = {entry['name']: entry for entry in tutorial}
    for name in names:
        if catalog[name] == local[name]:
            continue
        assert name in tutorial
        assert local[name]['Crc'] == tutorial[name]['crc']
        assert local[name]['Hash'] == tutorial[name]['cache_hash']
        assert local[name]['Dependencies'] == catalog[name]['Dependencies']
    missing = [catalog[name] for name in names if not (SERVER / filename(local[name])).is_file()]
    assert all(entry == local[entry['Name']] for entry in missing)
    print(f'Female appearance: {len(roots)} roots, {len(names)} dependencies, {len(missing)} missing', flush=True)
    if not missing:
        print('Female appearance resources are already complete; inventory preserved', flush=True)
        return
    records, errors = [], []
    with ThreadPoolExecutor(max_workers=4) as pool:
        tasks = {pool.submit(download, entry): entry['Name'] for entry in missing}
        for task in as_completed(tasks):
            try:
                records.append(task.result())
                if len(records) % 25 == 0:
                    print(f'Validated {len(records)}/{len(missing)} resources', flush=True)
            except Exception as error:
                errors.append({'name': tasks[task], 'error': str(error)})
    if errors:
        REPORT.parent.mkdir(parents=True, exist_ok=True)
        REPORT.write_text(json.dumps({'errors': errors}, indent=2) + '\n', 'utf-8')
        raise RuntimeError(f'{len(errors)} resources failed verification; inventory not modified')
    known = {entry['server_filename']: entry for entry in inventory['bundles']}
    for entry in records:
        if entry['server_filename'] in known:
            previous = known[entry['server_filename']]
            assert previous['sha256'] == entry['sha256'] and previous['catalog_entry'] == entry['catalog_entry']
        else:
            inventory['bundles'].append(entry)
    recovered_names = set(inventory.get('female_appearance_dependencies', {}).get('recovered_names', []))
    recovered_names.update(entry['catalog_entry']['Name'] for entry in records)
    inventory['female_appearance_dependencies'] = {
        'retrieved_at': datetime.now(timezone.utc).isoformat(),
        'roots': roots, 'dependency_count': len(names), 'bundles': names,
        'recovered_names': sorted(recovered_names),
        'cause': '404 for female clothing/hair leaves the client appearance placeholder black',
        'observed_failures': ['models$pc$female$body$f_body_hoody.fbx.bundle',
                              'models$pc$female$hair$f_hair_longwave_01.prefab.bundle']}
    temp = inventory_path.with_suffix('.json.tmp')
    temp.write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + '\n', 'utf-8')
    temp.replace(inventory_path)
    report = {'source_index_sha256': sha(original), 'roots': len(roots),
              'dependency_count': len(names), 'recovered': len(records),
              'decoded_textures': sum(entry['texture_count'] for entry in records),
              'payload_bytes': sum(entry['bytes'] for entry in records),
              'bundles': sorted(records, key=lambda entry: entry['server_filename'])}
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', 'utf-8')
    print(json.dumps({key: value for key, value in report.items() if key != 'bundles'}), flush=True)


if __name__ == '__main__':
    main()
