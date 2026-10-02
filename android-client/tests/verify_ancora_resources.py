"""Validate the reproducible upstream files and the tutorial's full dependency closure."""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'android-client/work/python-deps'))
import UnityPy

folder = ROOT / 'android-client/recovered/upstream-android'
inventory = json.loads((folder / 'inventory.json').read_text())
server = ROOT / 'Durango-CustomServer/server/assetbundles/android'
catalog = {item['Name']: item for item in json.loads((server / 'Info.5.2.1.json').read_text())['FileList']}
textures = 0
for item in inventory['bundles']:
    data = (folder / 'bundles-android' / item['server_filename']).read_bytes()
    assert len(data) == item['bytes']
    assert hashlib.sha256(data).hexdigest() == item['sha256'], item['server_filename']
    assert catalog[item['catalog_entry']['Name']] == item['catalog_entry']
    assert (server / item['server_filename']).read_bytes() == data
    env = UnityPy.load(data)
    for obj in env.objects:
        assert int(obj.assets_file.target_platform) == 13
        assert obj.assets_file.unity_version == '2017.4.34f1'
        if obj.type.name == 'Texture2D':
            texture = obj.read()
            assert texture.image.size == (texture.m_Width, texture.m_Height)
            textures += 1

checked = set()
pending = list(inventory['tutorial_scene_dependencies']['roots'])
while pending:
    name = pending.pop()
    if name in checked:
        continue
    checked.add(name)
    entry = catalog[name]
    assert (server / (name[:-7] + '.' + entry['Crc'] + '.bundle')).is_file(), name
    pending.extend(entry['Dependencies'])
assert len(checked) == 198
assert textures >= 155
print(f'PASS: {len(inventory["bundles"])} upstream payloads, catalog/SHA-256, {textures} textures and {len(checked)} tutorial dependencies')
