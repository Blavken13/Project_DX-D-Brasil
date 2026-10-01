"""Compare quarantined bundles with current assets; never alter the client/catalog."""
import collections
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
RECOVERY = ROOT / 'android-client/recovered/offserver-cache-2026-10-01'
CURRENT = ROOT / 'Durango-CustomServer/server/assetbundles/android'
sys.path.insert(0, str(ROOT / 'android-client/work/python-deps'))
import UnityPy


def native(path):
    return '\\\\?\\' + str(path.resolve()) if os.name == 'nt' else str(path)


def read_bytes(path):
    with open(native(path), 'rb') as stream:
        return stream.read()


def cab(path):
    return path.replace('\\', '/').rsplit('/', 1)[-1].lower()


def pointers(tree, trail=''):
    if isinstance(tree, dict):
        if 'm_FileID' in tree and 'm_PathID' in tree:
            if tree['m_PathID']:
                yield trail, int(tree['m_FileID']), int(tree['m_PathID'])
        else:
            for key, value in tree.items():
                yield from pointers(value, trail + '/' + key)
    elif isinstance(tree, (list, tuple)):
        for index, value in enumerate(tree):
            yield from pointers(value, trail + '/' + str(index))


def describe(data, detailed=False):
    env = UnityPy.load(data)
    objects = list(env.objects)
    assets = {cab(obj.assets_file.name): obj.assets_file for obj in objects}
    files = {}
    for name, file in assets.items():
        files[name] = {'unity_version': file.unity_version, 'format': file.header.version,
            'platform': int(file.target_platform), 'externals': [cab(x.path) for x in file.externals],
            'objects': {str(o.path_id): {'type': o.type.name,
                'raw_sha256': hashlib.sha256(o.get_raw_data()).hexdigest()} for o in file.objects.values()}}
    bundle_objects = [o for o in objects if o.type.name == 'AssetBundle']
    metadata = [o.read_typetree() for o in bundle_objects]
    description = {'files': files, 'asset_bundle_metadata': metadata,
        'containers': sorted({key for meta in metadata for key, value in meta.get('m_Container', [])}),
        'types': dict(collections.Counter(o.type.name for o in objects))}
    if detailed:
        description['textures'] = []
        description['parse_errors'] = []
        description['pointer_references'] = []
        for obj in objects:
            file = obj.assets_file
            try:
                tree = obj.read_typetree()
                for trail, file_id, path_id in pointers(tree):
                    if file_id == 0:
                        target = cab(file.name)
                    elif 0 < file_id <= len(file.externals):
                        target = cab(file.externals[file_id - 1].path)
                    else:
                        target = 'invalid-file-id:' + str(file_id)
                    description['pointer_references'].append({'source_cab': cab(file.name),
                        'source_path_id': obj.path_id, 'source_type': obj.type.name,
                        'field': trail, 'target_cab': target, 'target_path_id': path_id})
                if obj.type.name == 'Texture2D':
                    texture = obj.parse_as_object()
                    image = texture.image
                    pixels = image.convert('RGBA').tobytes()
                    if image.size != (texture.m_Width, texture.m_Height) or not pixels:
                        raise ValueError('Texture dimensions/data mismatch')
                    description['textures'].append({'name': texture.m_Name, 'path_id': obj.path_id,
                        'width': image.width, 'height': image.height,
                        'format': int(texture.m_TextureFormat),
                        'rgba_sha256': hashlib.sha256(pixels).hexdigest(),
                        'channel_extrema': image.convert('RGBA').getextrema()})
            except Exception as err:
                description['parse_errors'].append({'path_id': obj.path_id,
                    'type': obj.type.name, 'error': str(err)})
    return description


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    inventory_path = RECOVERY / 'inventory.json'
    inventory_bytes = inventory_path.read_bytes()
    inventory = json.loads(inventory_bytes)
    baseline_path = ROOT / 'android-client/dist/assetbundles-android-5.2.1.json'
    baseline_bytes = baseline_path.read_bytes()
    baseline = json.loads(baseline_bytes)
    catalog_bytes = (CURRENT / 'Info.5.2.1.json').read_bytes()
    catalog = json.loads(catalog_bytes)
    declared = {e['Name']: e for e in catalog['FileList']}
    declared['preload.bundle'] = {'Name': 'preload.bundle', 'Dependencies': []}
    available = {e['name']: e for e in baseline['bundles']}
    results = []
    candidate_registry = {}
    control_counts = collections.Counter()
    for index, source in enumerate(inventory['bundles'], 1):
        data = read_bytes(RECOVERY / 'bundles-android' / source['server_filename'])
        if hashlib.sha256(data).hexdigest() != source['sha256']:
            raise ValueError('Recovered payload changed: ' + source['name'])
        missing = source['name'] not in available
        description = describe(data, detailed=missing)
        bundle_names = {meta.get('m_AssetBundleName', meta.get('m_Name'))
                        for meta in description['asset_bundle_metadata']}
        dependencies = sorted({dep for meta in description['asset_bundle_metadata']
            for dep in meta.get('m_Dependencies', [])})
        result = {'name': source['name'], 'source_sha256': source['sha256'],
            'currently_missing': missing, 'unity_version': source['unity_version'],
            'catalog_crc_matches': source['crc_matches_current_catalog'],
            'catalog_hash_matches': source['hash_matches_current_catalog'],
            'bundle_name_matches': bundle_names == {source['name']},
            'dependencies': dependencies,
            'catalog_dependencies': sorted(declared[source['name']].get('Dependencies', [])),
            **description}
        if missing:
            expected_path = 'assets/' + source['name'][:-7].replace('$', '/')
            result['expected_asset_path'] = expected_path
            result['expected_asset_present'] = expected_path in description['containers']
            for name, info in description['files'].items():
                candidate_registry[name] = info
        else:
            current = available[source['name']]
            current_bytes = read_bytes(CURRENT / current['filename'])
            if hashlib.sha256(current_bytes).hexdigest() != current['sha256']:
                raise ValueError('Current payload changed: ' + source['name'])
            current_description = describe(current_bytes)
            result['control_byte_identical'] = data == current_bytes
            result['control_cab_names_identical'] = set(description['files']) == set(current_description['files'])
            result['control_asset_paths_identical'] = description['containers'] == current_description['containers']
            result['control_metadata_identical'] = description['asset_bundle_metadata'] == current_description['asset_bundle_metadata']
            result['control_object_differences'] = []
            for name in set(description['files']) | set(current_description['files']):
                left = description['files'].get(name, {}).get('objects', {})
                right = current_description['files'].get(name, {}).get('objects', {})
                for path_id in set(left) | set(right):
                    if left.get(path_id) != right.get(path_id):
                        result['control_object_differences'].append({'cab': name, 'path_id': path_id,
                            'recovered': left.get(path_id), 'current': right.get(path_id)})
            result['control_objects_identical'] = not result['control_object_differences']
            for key in ['byte', 'cab_names', 'asset_paths', 'metadata', 'objects']:
                control_counts[key + '_identical'] += result['control_' + key + '_identical']
        results.append(result)
        if index % 50 == 0:
            print('Recovered/control bundles tested:', index, flush=True)

    # Build a registry from all verified current payloads, then inspect consumers of the candidates.
    current_registry = {}
    consumers = []
    for index, entry in enumerate(baseline['bundles'], 1):
        data = read_bytes(CURRENT / entry['filename'])
        if hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise ValueError('Current payload changed: ' + entry['name'])
        description = describe(data)
        touches_candidates = False
        for name, info in description['files'].items():
            current_registry[name] = info
            if set(info['externals']) & set(candidate_registry):
                touches_candidates = True
        if touches_candidates:
            consumers.append(entry)
        if index % 100 == 0:
            print('Current bundle registry verified:', index, flush=True)
    print('Consumers referencing candidate CABs:', len(consumers), flush=True)
    combined_registry = {**current_registry, **candidate_registry}
    incoming = collections.defaultdict(list)
    consumer_errors = []
    for index, entry in enumerate(consumers, 1):
        env = UnityPy.load(read_bytes(CURRENT / entry['filename']))
        for obj in env.objects:
            file = obj.assets_file
            if not (set(cab(x.path) for x in file.externals) & set(candidate_registry)):
                continue
            try:
                tree = obj.read_typetree()
                for trail, file_id, path_id in pointers(tree):
                    if not (0 < file_id <= len(file.externals)):
                        continue
                    target_cab = cab(file.externals[file_id - 1].path)
                    if target_cab not in candidate_registry:
                        continue
                    target = candidate_registry[target_cab]['objects'].get(str(path_id))
                    incoming[target_cab].append({'consumer_bundle': entry['name'],
                        'source_type': obj.type.name, 'source_path_id': obj.path_id, 'field': trail,
                        'target_path_id': path_id, 'resolved': target is not None,
                        'target_type': target['type'] if target else None})
            except Exception as err:
                consumer_errors.append({'bundle': entry['name'], 'path_id': obj.path_id,
                    'type': obj.type.name, 'error': str(err)})
        if index % 50 == 0:
            print('Incoming-reference consumers tested:', index, flush=True)
    for result in results:
        if not result['currently_missing']:
            continue
        refs = [ref for name in result['files'] for ref in incoming[name]]
        result['incoming_references'] = refs
        result['incoming_reference_count'] = len(refs)
        result['incoming_unresolved_count'] = sum(not x['resolved'] for x in refs)
        result['outgoing_unresolved'] = []
        for ref in result['pointer_references']:
            target = combined_registry.get(ref['target_cab'], {}).get('objects', {}).get(str(ref['target_path_id']))
            ref['resolved'] = target is not None
            ref['target_type'] = target['type'] if target else None
            if target is None:
                result['outgoing_unresolved'].append(ref)
        result['dependencies_missing_from_test_set'] = [dep for dep in result['dependencies']
            if dep not in available and not any(x['name'] == dep for x in results)]
        result['static_candidate_pass'] = (result['bundle_name_matches'] and
            result['expected_asset_present'] and not result['parse_errors'] and
            not result['incoming_unresolved_count'] and not result['outgoing_unresolved'] and
            not result['dependencies_missing_from_test_set'] and len(refs) > 0)
    missing = [r for r in results if r['currently_missing']]
    summary = {'recovered_tested': len(results), 'controls_tested': len(results) - len(missing),
        'control_comparison': dict(control_counts),
        'missing_candidates_tested': len(missing),
        'missing_bundle_names_match': sum(r['bundle_name_matches'] for r in missing),
        'missing_expected_asset_paths_present': sum(r['expected_asset_present'] for r in missing),
        'missing_textures_decoded': sum(len(r['textures']) for r in missing),
        'missing_parse_errors': sum(len(r['parse_errors']) for r in missing),
        'missing_incoming_references': sum(r['incoming_reference_count'] for r in missing),
        'missing_incoming_unresolved': sum(r['incoming_unresolved_count'] for r in missing),
        'missing_outgoing_unresolved': sum(len(r['outgoing_unresolved']) for r in missing),
        'missing_static_pass': sum(r['static_candidate_pass'] for r in missing),
        'missing_static_pass_by_version': dict(collections.Counter(r['unity_version'] for r in missing if r['static_candidate_pass'])),
        'current_payloads_verified': len(baseline['bundles']), 'consumer_bundles_tested': len(consumers),
        'consumer_parse_errors': len(consumer_errors),
        'runtime_rendering_tested': False, 'catalog_changed': False, 'staging_changed': False}
    # Keep candidate evidence in full, but avoid repeating megabytes of preload metadata for controls.
    for result in results:
        if result['currently_missing']:
            continue
        result['control_object_difference_count'] = len(result['control_object_differences'])
        result['control_object_differences'] = result['control_object_differences'][:10]
        for info in result['files'].values():
            objects = info.pop('objects')
            info['object_count'] = len(objects)
            info['objects_sha256'] = hashlib.sha256(json.dumps(objects, sort_keys=True).encode()).hexdigest()
        metadata = result.pop('asset_bundle_metadata')
        result['asset_bundle_metadata_sha256'] = hashlib.sha256(json.dumps(metadata, sort_keys=True).encode()).hexdigest()
    report = {'tested_at': datetime.now(timezone.utc).isoformat(),
        'inventory_sha256': hashlib.sha256(inventory_bytes).hexdigest(),
        'baseline_report_sha256': hashlib.sha256(baseline_bytes).hexdigest(),
        'catalog_sha256': hashlib.sha256(catalog_bytes).hexdigest(),
        'summary': summary, 'consumer_errors': consumer_errors, 'bundles': results}
    (RECOVERY / 'test-results.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    if (CURRENT / 'Info.5.2.1.json').read_bytes() != catalog_bytes or baseline_path.read_bytes() != baseline_bytes:
        raise ValueError('Reference catalog/report changed during tests')
    print(json.dumps(summary, ensure_ascii=False, indent=2), flush=True)


if __name__ == '__main__':
    main()
