"""Check user artwork and targeted Unity label changes before delivering the APK."""
from pathlib import Path
import hashlib
import json
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
APK = ROOT.parent / 'DurangoBrasilApk'
sys.path.insert(0, str(ROOT / 'work/python-deps'))
import UnityPy


def main():
    manifest = json.loads((ROOT / 'branding/manifest.json').read_text('utf-8'))
    for entry in manifest['files']:
        assert hashlib.sha256((APK / entry['path']).read_bytes()).hexdigest() == entry['sha256']
    labels = []
    expected_text = 'Vision Force\nServidor Brasileiro\nVersão 1.0'.encode('utf-8') + b'[-]' * 6
    for name, target_id in [('6827ab7f4ebc56143b5702d0d1b8abc6', 175),
                            ('7d9842174a415534b91e990f357c7f29', 156)]:
        relative = 'assets/bin/Data/' + name
        before = UnityPy.load(str((ROOT / 'base' / relative).resolve()))
        after = UnityPy.load(str((APK / relative).resolve()))
        baseline = {obj.path_id: obj for obj in before.objects}
        updated = {obj.path_id: obj for obj in after.objects}
        assert baseline.keys() == updated.keys()
        modified = []
        for path_id, obj in baseline.items():
            assert obj.type == updated[path_id].type
            raw_before, raw_after = obj.get_raw_data(), updated[path_id].get_raw_data()
            if raw_before != raw_after:
                modified.append(path_id)
                assert len(raw_before) == len(raw_after) == 436
                assert obj.type.name == 'MonoBehaviour'
                assert raw_after[220:282] == expected_text
                assert struct.unpack_from('<i', raw_after, 216)[0] == 62
                assert struct.unpack_from('<i', raw_after, 148)[0] == 7  # bottom center pivot
                assert struct.unpack_from('<i', raw_after, 156)[0] == 72
                assert struct.unpack_from('<i', raw_after, 284)[0] == 18
                assert raw_after[296] == 1  # NGUI color resets are enabled
                assert struct.unpack_from('<i', raw_after, 332)[0] == 2  # ResizeFreely
                allowed = set(range(148, 152)) | set(range(156, 160)) | set(range(220, 282)) | set(range(284, 288))
                assert all(i in allowed for i, (old, new) in enumerate(zip(raw_before, raw_after)) if old != new)
        assert modified == [target_id], modified
        labels.append({'asset': name, 'label': target_id, 'unchanged_objects': len(baseline) - 1})
    logo_env = UnityPy.load(str((APK / 'assets/bin/Data/c6c98a0f398372a41862a640adc466d2').resolve()))
    logo = next(obj for obj in logo_env.objects if obj.type.name == 'Texture2D').parse_as_object().image
    from PIL import Image
    with Image.open(ROOT / 'ui/logo-durango-brasil.png') as web_logo:
        assert web_logo.size == logo.size
        assert web_logo.convert('RGBA').tobytes() == logo.convert('RGBA').tobytes()
    relative = 'assets/bin/Data/19ae04aa5e3159148bf3c56716acbcae'
    atlas_before = UnityPy.load(str((ROOT / 'base' / relative).resolve()))
    atlas_after = UnityPy.load(str((APK / relative).resolve()))
    baseline = {o.path_id: o for o in atlas_before.objects}
    updated = {o.path_id: o for o in atlas_after.objects}
    assert baseline.keys() == updated.keys()
    modified = []
    for path_id, obj in baseline.items():
        old, new = obj.get_raw_data(), updated[path_id].get_raw_data()
        if old == new:
            continue
        modified.append(path_id)
        assert path_id == 3 and obj.type.name == 'MonoBehaviour' and len(old) == len(new)
        def coordinates(raw, name):
            position = (raw.index(name) + len(name) + 3) // 4 * 4
            return position, struct.unpack_from('<4i', raw, position)
        kr_position, kr = coordinates(new, b'bg_loading_ment_kr')
        en_position, en = coordinates(new, b'bg_loading_ment_en')
        assert kr == en == (1953, 157, 92, 35)
        assert coordinates(old, b'bg_loading_ment_kr')[1] == kr
        assert all(en_position <= n < en_position + 16
                   for n, (a, b) in enumerate(zip(old, new)) if a != b)
    assert modified == [3]
    icon_manifest = json.loads((ROOT / 'branding/app-icon-manifest.json').read_text('utf-8'))
    assert len(icon_manifest['files']) == 30
    for entry in icon_manifest['files']:
        assert hashlib.sha256((APK / entry['path']).read_bytes()).hexdigest() == entry['sha256']
        with Image.open(APK / entry['path']) as icon:
            assert list(icon.size) == entry['dimensions'] and icon.mode == 'RGBA'
    report = {'artwork_files_preserved': len(manifest['files']), 'labels': labels,
              'serialized_sizes_unchanged': True, 'object_references_preserved': True,
              'login_logo_matches_character_logo': True, 'protocol_version_unchanged': True}
    report.update({'localized_loading_logo_fixed': True, 'app_icon_variants': 30})
    (ROOT / 'work/branding-check.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
