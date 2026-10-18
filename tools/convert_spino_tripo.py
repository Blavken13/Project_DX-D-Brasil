"""Repair the Tripo GLB primitive mode, then create an optimized FBX in Blender."""
import hashlib
import json
from pathlib import Path
import struct
import zipfile
import bpy

ROOT = Path(__file__).resolve().parents[1]
SPINO = ROOT / 'dino/Spinosaurus'
WORK = SPINO / 'Converted/Tripo'
SOURCE = WORK / 'spino_rigged.glb'
REPAIRED = WORK / 'spino_rigged_surface.glb'
REPORT = WORK / 'source-analysis.json'


def ensure_source():
    if SOURCE.is_file():
        return
    archive = SPINO / 'spino_rigged.zip'
    with zipfile.ZipFile(archive) as source:
        source.extract('spino_rigged.glb', WORK)


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def repair():
    ensure_source()
    with SOURCE.open('rb') as stream:
        header = stream.read(20)
        assert header[:4] == b'glTF' and struct.unpack_from('<I', header, 4)[0] == 2
        json_size = struct.unpack_from('<I', header, 12)[0]
        json_chunk_type = struct.unpack_from('<I', header, 16)[0]
        assert json_chunk_type == 0x4E4F534A
        json_bytes = stream.read(json_size)
    document = json.loads(json_bytes)
    primitive = document['meshes'][0]['primitives'][0]
    vertex_count = document['accessors'][primitive['attributes']['POSITION']]['count']
    assert primitive.get('mode', 4) == 1, primitive.get('mode')
    assert vertex_count % 3 == 0, vertex_count
    assert not document.get('animations'), 'Unexpected source animations'
    assert len(document['skins'][0]['joints']) == 28
    source_text = json_bytes.decode('utf-8').rstrip(' ')
    old = '"mode":1'
    new = '"mode":4'
    assert source_text.count(old) == 1, 'Unexpected GLB JSON serialization'
    repaired_text = source_text.replace(old, new)
    repaired_json = repaired_text.encode('utf-8')
    padding = (-len(repaired_json)) % 4
    repaired_json += b' ' * padding
    # Keep the JSON chunk size unchanged so the embedded binary chunk offsets
    # and every vertex, normal, UV, joint and weight accessor remain valid.
    assert len(repaired_json) == json_size
    with REPAIRED.open('wb') as stream:
        stream.write(header[:12])
        stream.write(struct.pack('<I', len(repaired_json)))
        stream.write(header[16:20])
        stream.write(repaired_json)
        with SOURCE.open('rb') as source:
            source.seek(20 + json_size)
            while block := source.read(1024 * 1024):
                stream.write(block)
    result = dict(source=SOURCE.name, repaired=REPAIRED.name,
                  source_sha256=sha256(SOURCE), repaired_sha256=sha256(REPAIRED),
                  source_primitive_mode='LINES', repaired_primitive_mode='TRIANGLES',
                  vertices=vertex_count, faces=vertex_count // 3,
                  joints=len(document['skins'][0]['joints']),
                  animations=len(document.get('animations', [])),
                  images=len(document.get('images', [])),
                  source_zip_preserved=True,
                  repair='change glTF primitive mode 1 to 4; payload and skin data unchanged')
    REPORT.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))


repair()
bpy.ops.import_scene.gltf(filepath=str(REPAIRED), merge_vertices=True)
root_objects = list(bpy.context.selected_objects)
meshes = [obj for obj in root_objects if obj.type == 'MESH']
armatures = [obj for obj in root_objects if obj.type == 'ARMATURE']
assert len(meshes) == len(armatures) == 1, (meshes, armatures)
mesh, armature = meshes[0], armatures[0]
mesh.name = 'SpinosaurusTripoBody'
armature.name = 'SpinosaurusTripoRig'
mesh.parent = armature
mesh.parent_type = 'OBJECT'
# Reduce the generated 1.94 million triangle source to a practical preview
# mesh while keeping its UV map, skin groups and PBR material.
modifier = mesh.modifiers.new('GameMeshDecimation', 'DECIMATE')
modifier.ratio = 0.04
modifier.use_collapse_triangulate = True
bpy.context.view_layer.objects.active = mesh
while mesh.modifiers.find(modifier.name) > 0:
    bpy.ops.object.modifier_move_up(modifier=modifier.name)
bpy.ops.object.modifier_apply(modifier=modifier.name)
mesh.data.name = 'SpinosaurusTripoBodyMesh'
assert len(mesh.data.polygons) < 120000, len(mesh.data.polygons)
report = json.loads(REPORT.read_text(encoding='utf-8'))
report.update(imported_meshes=len(meshes), imported_armatures=len(armatures),
              bones=len(armature.data.bones),
              decimated_vertices=len(mesh.data.vertices),
              decimated_faces=len(mesh.data.polygons),
              material_names=[mat.name for mat in mesh.data.materials if mat],
              animation_compatibility='requires retargeting; GLB has no clips and a 28-bone Tripo rig')
mesh.select_set(True)
armature.select_set(True)
bpy.context.view_layer.objects.active = armature
output = WORK / 'SpinosaurusTripoPreview.fbx'
bpy.ops.export_scene.fbx(filepath=str(output), use_selection=True,
                         object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         apply_unit_scale=True, path_mode='COPY', embed_textures=True,
                         bake_anim=False)
report['fbx'] = output.name
report['fbx_bytes'] = output.stat().st_size
REPORT.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('SPINO_TRIPO_FBX', json.dumps(report, indent=2))
