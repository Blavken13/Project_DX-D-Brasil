"""Check the original Android female wardrobe and its complete dependency closure."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'android-client/work/python-deps'))
import UnityPy

server = ROOT / 'Durango-CustomServer/server/assetbundles/android'
inventory = json.loads((ROOT / 'android-client/recovered/upstream-android/inventory.json').read_text())
index = json.loads((server / 'Info.5.2.1.json').read_text())
catalog = {e['Name']: e for e in index['FileList']}
manifest = inventory['female_appearance_dependencies']
recorded = {e['catalog_entry']['Name']: e for e in inventory['bundles']}
roots = sorted(n for n in catalog if n.startswith('models$pc$female$'))
assert roots == manifest['roots'] and len(roots) == 366
pending, closure = list(roots), set()
while pending:
    name = pending.pop()
    if name in closure:
        continue
    closure.add(name)
    pending.extend(catalog[name]['Dependencies'])
assert sorted(closure) == manifest['bundles'] and len(closure) == 376

objects, references, builtin, textures = {}, [], set(), 0
# Models reference common shaders and lookup textures in the original preload.
preload_name = 'preload.' + index['PreloadCrc'] + '.bundle'
preload_data = (server / preload_name).read_bytes()
assert preload_data == (ROOT / 'android-client/recovered/android-cache-2026-09-30/bundles-android' / preload_name).read_bytes()
preload = UnityPy.load(preload_data)
for obj in preload.objects:
    assert obj.assets_file.unity_version == '2017.4.34f1'
    assert int(obj.assets_file.target_platform) == 13
    objects[(obj.assets_file.name.lower(), obj.path_id)] = obj.get_raw_data()
for name in sorted(closure):
    entry = catalog[name]
    path = server / (name[:-7] + '.' + entry['Crc'] + '.bundle')
    assert path.is_file(), 'Missing female dependency: ' + name
    data = path.read_bytes()
    if name in recorded:
        expected = recorded[name]
        assert expected['catalog_entry'] == entry
        assert len(data) == expected['bytes']
        assert hashlib.sha256(data).hexdigest() == expected['sha256']
    env = UnityPy.load(data)
    for obj in env.objects:
        assert obj.assets_file.unity_version == '2017.4.34f1'
        assert int(obj.assets_file.target_platform) == 13
        key = (obj.assets_file.name.lower(), obj.path_id)
        if key in objects:
            assert objects[key] == obj.get_raw_data(), 'Conflicting Unity object: ' + str(key)
        else:
            objects[key] = obj.get_raw_data()
        if obj.type.name == 'Texture2D':
            texture = obj.read()
            assert texture.image.size == (texture.m_Width, texture.m_Height)
            textures += 1

        def walk(value):
            if isinstance(value, dict):
                if 'm_FileID' in value and 'm_PathID' in value:
                    index, path_id = value['m_FileID'], value['m_PathID']
                    if path_id == 0:
                        return
                    assert 0 <= index <= len(obj.assets_file.externals)
                    cab = (obj.assets_file.name if index == 0 else
                           obj.assets_file.externals[index - 1].path.rsplit('/', 1)[-1]).lower()
                    ref = (cab, path_id)
                    if cab in ('unity_builtin_extra', 'unity default resources'):
                        builtin.add(ref)
                    else:
                        references.append((name, ref))
                else:
                    for child in value.values():
                        walk(child)
            elif isinstance(value, list):
                for child in value:
                    walk(child)
        walk(obj.read_typetree())

unresolved = sorted({(name, ref) for name, ref in references if ref not in objects})
assert not unresolved, 'Unresolved female references: ' + str(unresolved[:20])
original_data = ROOT / 'Durango original/assets/bin/Data'
engine_objects = set()
for name in ('unity_builtin_extra', 'unity default resources'):
    # The resource path is unchanged in the APK. Unity builtins are verified by ID.
    path = original_data / name
    if not path.is_file():
        path = original_data / 'Resources' / name
    if path.is_file():
        env = UnityPy.load(str(path))
        engine_objects.update((name, obj.path_id) for obj in env.objects)
assert builtin <= engine_objects, 'Missing engine builtin references: ' + str(builtin - engine_objects)
assert textures >= 333
print(f'PASS: {len(roots)} female appearances, {len(closure)} bundles plus original preload, '
      f'{textures} decoded textures, {len(references)} resolved references and {len(builtin)} engine references')
