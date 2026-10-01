"""Check recovered type trees against schemas already present in the current bundles."""
import collections
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from analyze_recovered_bundles import CURRENT, RECOVERY, ROOT, UnityPy, read_bytes


def schemas(data):
    env = UnityPy.load(data)
    files = {obj.assets_file.name: obj.assets_file for obj in env.objects}
    result = []
    for name, asset_file in files.items():
        for kind in asset_file.types:
            fields = [[node.m_Level, node.m_Type, node.m_Name, node.m_ByteSize,
                node.m_Version, node.m_TypeFlags, node.m_MetaFlag]
                for node in kind.node.traverse()] if kind.node else None
            result.append({'cab': name, 'type_id': kind.class_id,
                'tree_preserved': fields is not None,
                'schema_sha256': hashlib.sha256(json.dumps(fields).encode()).hexdigest() if fields else None,
                'format': asset_file.header.version, 'platform': int(asset_file.target_platform)})
    return result


def main():
    inventory = json.loads((RECOVERY / 'inventory.json').read_text())
    baseline = json.loads((ROOT / 'android-client/dist/assetbundles-android-5.2.1.json').read_text())
    known = {b['name']: b for b in baseline['bundles']}
    current_schemas = collections.defaultdict(set)
    for bundle in inventory['bundles']:
        if bundle['name'] not in known:
            continue
        for tree in schemas(read_bytes(CURRENT / known[bundle['name']]['filename'])):
            current_schemas[tree['type_id']].add(tree['schema_sha256'])
    results = []
    for bundle in inventory['bundles']:
        if bundle['name'] in known:
            continue
        items = schemas(read_bytes(RECOVERY / 'bundles-android' / bundle['server_filename']))
        for item in items:
            item['schema_matches_current_control'] = item['schema_sha256'] in current_schemas[item['type_id']]
        results.append({'name': bundle['name'], 'unity_version': bundle['unity_version'],
            'type_trees': items, 'all_type_trees_preserved': all(t['tree_preserved'] for t in items),
            'all_schemas_match_current_controls': all(t['schema_matches_current_control'] for t in items)})
    summary = {'missing_candidates': len(results),
        'candidates_with_all_type_trees_preserved': sum(b['all_type_trees_preserved'] for b in results),
        'candidates_with_all_schemas_matching': sum(b['all_schemas_match_current_controls'] for b in results),
        'tested_type_trees': sum(len(b['type_trees']) for b in results),
        'unmatched_type_trees': sum(not t['schema_matches_current_control'] for b in results for t in b['type_trees'])}
    (RECOVERY / 'schema-tests.json').write_text(json.dumps({'summary': summary, 'bundles': results}, indent=2) + '\n')
    print(json.dumps(summary, indent=2), flush=True)


if __name__ == '__main__':
    main()
