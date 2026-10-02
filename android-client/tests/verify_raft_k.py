"""Check original tutorial interaction, K prefab dependencies and native hook compatibility."""
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'android-client/work/python-deps'))
import UnityPy

assets = ROOT / 'Durango-CustomServer/server/assetbundles/android'
catalog = {item['Name']: item for item in json.loads((assets / 'Info.5.2.1.json').read_text())['FileList']}

def payload(name):
    return assets / (name[:-7] + '.' + catalog[name]['Crc'] + '.bundle')

env = UnityPy.load(str(payload('models$ancora$npc.bundle')))
interactions = [obj.read_typetree() for obj in env.objects if obj.type.name == 'MonoBehaviour']
leaders = [item for item in interactions if item.get('_entityId') == '502']
assert len(leaders) == 1, 'Expected one original raft leader interaction'
assert any(action['ToDo'] == 'talk_npc_raft_ancora.meet_chief' for action in leaders[0]['_actionList'])
assert any(action['ToDo'] == 'talk_npc_raft_ancora.meet_chief' and action['Action'] == 0
           for action in leaders[0]['_actionList'])
assert leaders[0]['_interactableDistance'] > 0

pending = ['models$npc$f_npc_k_story.prefab.bundle']; checked = set()
while pending:
    name = pending.pop()
    if name in checked:
        continue
    checked.add(name)
    assert payload(name).is_file(), 'Missing K dependency: ' + name
    pending.extend(catalog[name]['Dependencies'])
env = UnityPy.load(str(payload('models$npc$f_npc_k_story.prefab.bundle')))
assert any(obj.type.name == 'GameObject' and obj.read().m_Name == 'F_NPC_K_Story' for obj in env.objects)

native = (ROOT / 'Durango original/lib/arm64-v8a/libil2cpp.so').read_bytes()
assert native[0x16f88ec:0x16f88ec + 16].hex() == 'f657bda9f44f01a9fd7b02a9fd830091'
metadata = (ROOT / 'Durango original/assets/bin/Data/Managed/Metadata/global-metadata.dat').read_bytes()
header = struct.unpack_from('<68I', metadata)
def name(offset):
    start = header[6] + offset
    return metadata[start:metadata.index(b'\0', start)].decode()
methods = [struct.unpack_from('<12I4H', metadata, header[12] + i * 56) for i in range(header[13] // 56)]
required = {
    'Object': {'FindObjectsOfType', 'Instantiate', 'Destroy', 'DestroyImmediate', 'op_Implicit'},
    'GameObject': {'GetComponentsInChildren', 'get_transform', 'AddComponent'},
    'Transform': {'Find', 'SetParent', 'get_parent', 'set_localPosition', 'set_localEulerAngles', 'set_position', 'set_eulerAngles'},
    'AssetBundleManager': {'TryLoadBundleFile'},
    'AssetBundle': {'LoadAssetAsync'},
    'AssetBundleRequest': {'get_asset'},
    'AsyncOperation': {'get_isDone'},
    'Renderer': {'set_enabled'},
    'ImmovableBase': {'get_WorldTile'},
    'Util': {'TilePositionToClientPosition'},
    'ToDoListSystem': {'FindToDo'},
    'ToDoBase': {'get_IsCompleted'},
}
found = set()
client_action = None
for i in range(header[41] // 104):
    record = struct.unpack_from('<20I8H2I', metadata, header[40] + i * 104)
    typ = name(record[0])
    if typ == 'Interaction' and name(record[1]) == 'InteractionData':
        for field_index in range(record[12], record[12] + record[22]):
            definition = struct.unpack_from('<4I', metadata, header[24] + field_index * 16)
            if name(definition[0]) != 'ClientSidePropAction':
                continue
            for offset in range(header[16], header[16] + header[17], 12):
                index, _, data_index = struct.unpack_from('<3i', metadata, offset)
                if index == field_index:
                    client_action = struct.unpack_from('<i', metadata, header[18] + data_index)[0]
    expected_ns = ('' if typ in {'AssetBundleManager', 'ImmovableBase', 'ToDoListSystem'} else
                   'Durango.Terrain' if typ == 'Util' else
                   'Durango.Logic.PlayGuide' if typ == 'ToDoBase' else 'UnityEngine')
    if typ not in required or name(record[1]) != expected_ns:
        continue
    present = {name(method[0]) for method in methods[record[13]:record[13] + record[20]]}
    assert required[typ] <= present, (typ, required[typ] - present)
    found.add(typ)
assert found == set(required)
assert client_action == 10250, 'ClientSidePropAction must match the original APK enum'
print(f'PASS: original NPC 502 ToDo, K prefab, {len(checked)} dependencies and original IL2CPP APIs/prologue')
