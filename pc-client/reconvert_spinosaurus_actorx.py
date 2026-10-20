"""Rebuild Spino from matching ActorX mesh/animations in original centimeter units.

Run in Blender: blender -b --python pc-client/reconvert_spinosaurus_actorx.py
The previous FBX is retained under Converted/before-actorx-repair.
"""
import json
from pathlib import Path
import re
import shutil
import sys

import bpy
from mathutils import Vector, Quaternion
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SPINO = ROOT / 'dino/Spinosaurus'
SOURCE = SPINO / 'Converted/ActorX/PrimalEarth/Dinos/Spino/Spino_new'
PSA = SPINO / 'Converted/UEViewer/PrimalEarth/Dinos/Spino/Animations'
ASSETS = SPINO / 'UnityProject/Assets/Models/Animals/Spinosaurus'
OUTPUT = SPINO / 'Converted/ActorXRepair'
sys.path[:0] = [str(SPINO / '_source/addons/io_scene_psk_psa-master'),
               str(SPINO / '_source/addons/deps')]

import io_scene_psk_psa
from io_scene_psk_psa.psk.importer import PskImportOptions, import_psk
from io_scene_psk_psa.psa.importer import PsaImportOptions, import_psa
from io_scene_psk_psa.psa import importer as psa_importer
from io_scene_psk_psa.psa.config import read_psa_config, REMOVE_TRACK_LOCATION, REMOVE_TRACK_ROTATION
from psk_psa_py.psk.reader import read_psk_from_file
from psk_psa_py.psa.reader import PsaReader


def reconstruct_additive_clips(psk):
    """Bake Unreal local/mesh-space deltas into complete poses before PSA import."""
    original_reader = psa_importer._read_sequence_data_matrix
    tracks, metadata, names_by_clip = {}, {}, {}
    rest_by_name = {bone.name.decode('utf-8'): bone for bone in psk.bones}
    for path in sorted(PSA.glob('*.psa')):
        props = path.with_suffix('.props.txt').read_text()
        match = re.search(r'AdditiveAnimType = \w+ \((\d+)\)', props)
        kind = int(match.group(1)) if match else 0
        reference = re.search(r"RefPoseSeq = AnimSequence4'[^']*\.([^'.]+)'", props)
        frame = re.search(r'RefFrameIndex = (\d+)', props)
        with PsaReader.from_path(path) as reader:
            config = read_psa_config(list(reader.sequences), str(path.with_suffix('.config')))
            names = [bone.name.decode('utf-8') for bone in reader.bones]
            for name in reader.sequences:
                data = original_reader(reader, name)
                # UEViewer emits identity placeholders for absent tracks. Its
                # RemoveTracks sidecar identifies these; absolute poses need
                # the target bind pose, while additive tracks need zero deltas.
                if not kind:
                    for index, flags in config.sequence_bone_flags.get(name, {}).items():
                        rest = rest_by_name[names[index]]
                        if flags & REMOVE_TRACK_LOCATION:
                            data[:, index, 4:7] = tuple(rest.location)
                        if flags & REMOVE_TRACK_ROTATION:
                            data[:, index, :4] = tuple(rest.rotation)
                tracks[name] = data
                names_by_clip[name] = names
                metadata[name] = dict(kind=kind, reference=reference.group(1) if reference else None,
                                      reference_frame=int(frame.group(1)) if frame else 0)
    baked = {}
    report = []
    for name, data in tracks.items():
        info = metadata[name]
        if not info['kind']:
            baked[name] = data.copy()
            continue
        reference = info['reference']
        if reference == name or reference not in tracks or metadata[reference]['kind']:
            reference = 'Spino-Biped-Idle' if 'Biped' in name else 'Spino-Idle'
        source_names = names_by_clip[name]
        indices = {bone: i for i, bone in enumerate(names_by_clip[reference])}
        base = tracks[reference][min(info['reference_frame'], len(tracks[reference]) - 1)]
        base = base[[indices[bone] for bone in source_names]]
        result = data.copy()
        result[:, :, 4:7] += base[None, :, 4:7]
        result[:, :, 7:] = 1
        parent_names = {bone.name.decode('utf-8'): psk.bones[bone.parent_index].name.decode('utf-8')
                        for i, bone in enumerate(psk.bones) if i != 0}
        parents = [source_names.index(parent_names[bone]) if bone in parent_names else -1 for bone in source_names]
        # ActorX stores non-root local rotations conjugated. Convert to physical
        # rotations before accumulating deltas, then convert back for the addon.
        base_local = [Quaternion(tuple(row[:4])) for row in base]
        base_local = [q if parents[i] < 0 else q.conjugated() for i, q in enumerate(base_local)]
        base_global = [None] * len(source_names)
        def world_rotation(index):
            if base_global[index] is None:
                parent = parents[index]
                base_global[index] = base_local[index] if parent < 0 else world_rotation(parent) @ base_local[index]
            return base_global[index]
        for index in range(len(source_names)):
            world_rotation(index)
        for frame_index in range(len(data)):
            deltas = [Quaternion(tuple(row[:4])) for row in data[frame_index]]
            deltas = [q if parents[i] < 0 else q.conjugated() for i, q in enumerate(deltas)]
            if info['kind'] == 2:
                globals_ = [delta @ pose for delta, pose in zip(deltas, base_global)]
                locals_ = [pose if parents[i] < 0 else globals_[parents[i]].inverted() @ pose
                           for i, pose in enumerate(globals_)]
            else:
                locals_ = [delta @ pose for delta, pose in zip(deltas, base_local)]
            for index, rotation in enumerate(locals_):
                rotation.normalize()
                result[frame_index, index, :4] = tuple(rotation if parents[index] < 0 else rotation.conjugated())
        baked[name] = result
        report.append(dict(clip=name, additive_type=info['kind'], baked_on=reference))
    psa_importer._read_sequence_data_matrix = lambda reader, name: baked[name].copy()
    return report


def pixels(path):
    image = bpy.data.images.load(str(path), check_existing=False)
    image.colorspace_settings.name = 'Non-Color'
    values = np.empty(len(image.pixels), dtype=np.float32)
    image.pixels.foreach_get(values)
    return image, values.reshape(image.size[1], image.size[0], 4)


def bake_albedo():
    diffuse, rgba = pixels(SOURCE / 'Spino_colorized_D.png')
    mask_image, mask = pixels(SOURCE / 'Spino_colorized_M.png')
    if mask_image.size[:] != diffuse.size[:]:
        mask_image.scale(*diffuse.size)
        values = np.empty(len(mask_image.pixels), dtype=np.float32)
        mask_image.pixels.foreach_get(values)
        mask = values.reshape(diffuse.size[1], diffuse.size[0], 4)
    r, g, b = (mask[:, :, i] for i in range(3))
    # Six chromatic regions: red/green/blue/cyan/magenta/yellow. Black and
    # neutral pixels retain their original diffuse (eyes, claws, mouth, etc.).
    weights = np.stack((np.maximum(r - np.maximum(g, b), 0),
                        np.maximum(g - np.maximum(r, b), 0),
                        np.maximum(b - np.maximum(r, g), 0),
                        np.maximum(np.minimum(g, b) - r, 0),
                        np.maximum(np.minimum(r, b) - g, 0),
                        np.maximum(np.minimum(r, g) - b, 0)), axis=-1)
    text = (SOURCE / 'Spino_colorized_new_MIC.props.txt').read_text()
    palette = np.ones((6, 3), dtype=np.float32)
    for index, red, green, blue in re.findall(
            r'Name=Color([0-5])\s*}\s*ParameterValue = \{ R=([\d.]+), G=([\d.]+), B=([\d.]+)', text):
        palette[int(index)] = [float(red), float(green), float(blue)]
    tint = np.maximum(1 - weights.sum(axis=-1, keepdims=True), 0)
    tint = tint + np.einsum('hwc,cd->hwd', weights, palette)
    rgb = rgba[:, :, :3]
    linear = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    colored = np.clip(linear * tint, 0, 1)
    encoded = np.where(colored <= .0031308, colored * 12.92, 1.055 * colored ** (1 / 2.4) - .055)
    result = bpy.data.images.new('Spino_Albedo_Baked', width=diffuse.size[0], height=diffuse.size[1], alpha=True)
    result.colorspace_settings.name = 'Non-Color'
    output = np.concatenate((encoded, np.ones_like(encoded[:, :, :1])), axis=-1)
    result.pixels.foreach_set(output.astype(np.float32).ravel())
    target = ASSETS / 'Textures/BodyNew/Spino_Albedo_Baked.png'
    result.filepath_raw = str(target)
    result.file_format = 'PNG'
    result.save()
    # Reload with sRGB interpretation for Blender preview and Unity import.
    baked = bpy.data.images.load(str(target), check_existing=False)
    baked.colorspace_settings.name = 'sRGB'
    return baked, dict(source='Spino_colorized_D.png + Spino_colorized_M.png',
                       palette_linear=palette.tolist(), size=list(diffuse.size),
                       baked=str(target.relative_to(ROOT)))


def evaluate(mesh):
    bpy.context.view_layer.update()
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    snapshot = evaluated.to_mesh()
    values = np.empty(len(snapshot.vertices) * 3, dtype=np.float64)
    snapshot.vertices.foreach_get('co', values)
    values = values.reshape(-1, 3)
    evaluated.to_mesh_clear()
    return values


def reset_pose(armature):
    for bone in armature.pose.bones:
        bone.location = (0, 0, 0)
        bone.rotation_mode = 'QUATERNION'
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.scale = (1, 1, 1)


def render_pose(mesh, armature, action, output):
    reset_pose(armature)
    armature.animation_data.action = action
    if action:
        armature.animation_data.action_slot = action.slots[0]
        bpy.context.scene.frame_set(int(sum(action.frame_range) * .5))
    else:
        bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    bpy.context.scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    io_scene_psk_psa.register()
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = 'NONE'
    bpy.context.scene.unit_settings.scale_length = 1
    psk = read_psk_from_file(str(SOURCE / 'Spino_new.psk'))
    options = PskImportOptions()
    options.should_import_materials = False
    result = import_psk(psk, bpy.context, 'SpinosaurusRig', options)
    armature, mesh = result.armature_object, result.mesh_object
    armature.name = 'SpinosaurusRig'
    mesh.name = 'SpinosaurusBody'
    mesh.data.name = 'SpinosaurusBody'
    assert tuple(armature.scale) == (1, 1, 1) and tuple(mesh.scale) == (1, 1, 1)
    weighted_bones = {g.name for g in mesh.vertex_groups}
    additive_report = reconstruct_additive_clips(psk)
    imported = []
    for path in sorted(PSA.glob('*.psa')):
        with PsaReader.from_path(str(path)) as reader:
            source_names = {b.name.decode('utf-8') for b in reader.bones}
            assert not weighted_bones - source_names, (path.name, weighted_bones - source_names)
            names = list(reader.sequences)
            result = import_psa(bpy.context, reader, armature, PsaImportOptions(
                sequence_names=names, should_stash=False, should_use_fake_user=True,
                should_use_config_file=False, should_write_scale_keys=False,
                translation_scale=1.0))
            assert not result.warnings, (path.name, result.warnings)
            imported.extend(names)
    assert len(imported) == len(set(imported)) == 49
    for name in imported:
        action = bpy.data.actions[name]
        if action.frame_range[0] == action.frame_range[1]:
            action.use_frame_range = True
            action.frame_start = 0
            action.frame_end = 1
    albedo, color_report = bake_albedo()
    material = bpy.data.materials.new('SpinosaurusBody')
    material.use_nodes = True
    material.node_tree.nodes.clear()
    shader = material.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output = material.node_tree.nodes.new('ShaderNodeOutputMaterial')
    material.node_tree.links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    shader.inputs['Roughness'].default_value = .8
    texture = material.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image = albedo
    material.node_tree.links.new(texture.outputs['Color'], shader.inputs['Base Color'])
    mesh.data.materials.clear()
    mesh.data.materials.append(material)
    armature.animation_data_create()
    armature.animation_data.action = None
    reset_pose(armature)
    bpy.context.scene.frame_set(0)
    rest = evaluate(mesh)
    edges = np.array([tuple(edge.vertices) for edge in mesh.data.edges], dtype=np.int32)
    rest_lengths = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
    useful = rest_lengths > np.quantile(rest_lengths, .25)
    metrics = []
    for name in imported:
        action = bpy.data.actions[name]
        armature.animation_data.action = action
        armature.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        worst = 0.0
        worst_p99 = 0.0
        stretched = 0
        for fraction in np.linspace(0, 1, 9):
            reset_pose(armature)
            bpy.context.scene.frame_set(int(start + fraction * (end - start)))
            posed = evaluate(mesh)
            lengths = np.linalg.norm(posed[edges[:, 0]] - posed[edges[:, 1]], axis=1)
            ratio = float(np.max(lengths[useful] / rest_lengths[useful]))
            worst = max(worst, ratio)
            ratios = lengths[useful] / rest_lengths[useful]
            worst_p99 = max(worst_p99, float(np.quantile(ratios, .99)))
            stretched = max(stretched, int(np.count_nonzero(ratios > 4)))
            assert np.isfinite(posed).all(), (name, fraction, ratio)
        metrics.append(dict(clip=name, maximum_edge_stretch=round(worst, 4),
                            p99_edge_stretch=round(worst_p99, 4), edges_stretched_over_4=stretched))
    invalid = [row for row in metrics if row['p99_edge_stretch'] > 2.2 or row['maximum_edge_stretch'] > 10]
    assert not invalid, ('Invalid ActorX skin deformation', invalid)
    # Export a clean bind pose, not the last sampled frame. Mesh and PSA stay in
    # the same centimeter basis from import through FBX; no post-import rescaling.
    armature.animation_data.action = None
    reset_pose(armature)
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    original = ASSETS / 'Meshes/SpinosaurusBody.fbx'
    target = OUTPUT / 'SpinosaurusBody.fbx'
    backup = SPINO / 'Converted/before-actorx-repair'
    backup.mkdir(parents=True, exist_ok=True)
    if original.exists() and not (backup / original.name).exists():
        shutil.copy2(original, backup / original.name)
    bpy.ops.object.select_all(action='DESELECT')
    armature.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.fbx(filepath=str(target), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
        apply_unit_scale=False, global_scale=1, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        path_mode='RELATIVE', bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0)
    report = dict(pipeline='ActorX PSK + PSA, source centimeters, no glTF',
        vertices=len(mesh.data.vertices), faces=len(mesh.data.polygons),
        bones=len(armature.data.bones), clips=len(imported),
        rest_min=rest.min(axis=0).tolist(), rest_max=rest.max(axis=0).tolist(),
        material=color_report, geometry_validated=True,
        additive_clips_reconstructed=additive_report,
        pose_validation=metrics)
    (OUTPUT / 'conversion-report.json').write_text(json.dumps(report, indent=2) + '\n')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT / 'Spinosaurus_Repaired.blend'))
    # Render source poses as a visual check before the Unity bundle is built.
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 16
    scene.render.resolution_x = 960
    scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = 'AgX'
    scene.world.color = (.2, .2, .2)
    minimum, maximum = rest.min(axis=0), rest.max(axis=0)
    center = Vector(tuple((minimum + maximum) / 2))
    span = float(max(maximum - minimum))
    bpy.ops.object.camera_add(location=center + Vector((span * .8, -span * 1.1, span * .55)))
    camera = bpy.context.object
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = span * 1.25
    camera.data.clip_end = span * 20
    scene.camera = camera
    for offset, energy in [((.4, -.5, 1), 7e6), ((-.6, .4, .5), 4e6)]:
        bpy.ops.object.light_add(type='AREA', location=center + Vector(offset) * span)
        light = bpy.context.object
        light.data.energy = energy
        light.data.shape = 'DISK'
        light.data.size = span
        light.rotation_euler = (center - light.location).to_track_quat('-Z', 'Y').to_euler()
    for pose in ['REST', 'Spino-Idle', 'Spino-Move-Fwd', 'Spino-Attack-Bite']:
        render_pose(mesh, armature, None if pose == 'REST' else bpy.data.actions[pose], OUTPUT / (pose + '.png'))
    print('ACTORX_REPAIR_COMPLETE', json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
