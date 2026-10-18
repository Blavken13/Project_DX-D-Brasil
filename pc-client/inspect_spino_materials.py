"""Inspect the active client shader and Spino texture bindings without edits."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / 'pc-client/work/spino-python-deps'))
import UnityPy

client = root / 'Lost-Horizon-PC/Lost Horizon Game_Data/StreamingAssets/AssetBundles'
index = json.loads((client / 'Info.5.2.1.json').read_text('utf-8-sig'))

def bundle(name):
    entry = next(item for item in index['FileList'] if item['Name'] == name)
    filename = name[:-7] + '.' + entry['Crc'] + '.bundle'
    return UnityPy.load(str(client / filename))

template = bundle('models$animals$trex$trexprefab.prefab.bundle')
prefab = bundle('models$animals$spinosaurus$spinosaurusprefab.prefab.bundle')
records = []
for env in (template, prefab):
    for obj in env.objects:
        if obj.type.name != 'Material':
            continue
        name = obj.peek_name()
        if name not in {'TRex-texture1', 'SpinosaurusClassic',
                        'SpinosaurusSaddle', 'SpinosaurusBody'}:
            continue
        tree = obj.read_typetree()
        saved = tree['m_SavedProperties']
        textures = {}
        for key, value in saved.get('m_TexEnvs', []):
            pointer = value.get('m_Texture', {})
            try:
                textures[key] = pointer['m_PathID'] and obj.assets_file.objects[pointer['m_PathID']].peek_name()
            except Exception:
                textures[key] = pointer
        records.append(dict(name=name, shader=tree.get('m_Shader'),
                            textures=textures, floats=dict(saved.get('m_Floats', [])),
                            colors=dict(saved.get('m_Colors', [])),
                            keywords=tree.get('m_ValidKeywords')))
print(json.dumps(records, indent=2, default=str))
