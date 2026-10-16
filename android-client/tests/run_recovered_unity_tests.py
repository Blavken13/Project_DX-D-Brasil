"""Load recovered missing assets in an isolated project using the installed Unity editor."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

from analyze_recovered_bundles import CURRENT, RECOVERY, ROOT, read_bytes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path,
        default=Path('C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'))
    parser.add_argument('--editor-version', default='6000.6.0f1')
    args = parser.parse_args()
    if not args.unity.is_file():
        raise FileNotFoundError(args.unity)
    project = ROOT / 'android-client/work/unity-bundle-tests'
    (project / 'Assets/Editor').mkdir(parents=True, exist_ok=True)
    (project / 'ProjectSettings').mkdir(exist_ok=True)
    shutil.copyfile(Path(__file__).with_name('RecoveredBundleTests.cs'),
                    project / 'Assets/Editor/RecoveredBundleTests.cs')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + args.editor_version + '\n')
    report = json.loads((RECOVERY / 'test-results.json').read_text())
    inventory = json.loads((RECOVERY / 'inventory.json').read_text())
    recovered = {b['name']: b for b in inventory['bundles']}
    baseline = json.loads((ROOT / 'android-client/dist/assetbundles-android-5.2.1.json').read_text())
    available = {b['name']: b for b in baseline['bundles']}
    candidates = [b for b in report['bundles'] if b['currently_missing']]
    dependencies = sorted({d for b in candidates for d in b['dependencies']})
    entries = []
    for name in dependencies:
        if name in available:
            bundle = available[name]
            path = CURRENT / bundle['filename']
        else:
            bundle = recovered[name]
            path = RECOVERY / 'bundles-android' / bundle['server_filename']
        if hashlib.sha256(read_bytes(path)).hexdigest() != bundle['sha256']:
            raise ValueError('Dependency checksum mismatch: ' + name)
        entries.append({'name': name, 'path': str(path), 'candidate': False})
    for candidate in candidates:
        bundle = recovered[candidate['name']]
        path = RECOVERY / 'bundles-android' / bundle['server_filename']
        if hashlib.sha256(read_bytes(path)).hexdigest() != bundle['sha256']:
            raise ValueError('Candidate checksum mismatch: ' + candidate['name'])
        entries.append({'name': candidate['name'], 'path': str(path), 'candidate': True})
    result_path = RECOVERY / 'native-unity6-results.json'
    spec = {'entries': entries, 'output': str(result_path)}
    (project / 'test-spec.json').write_text(json.dumps(spec, indent=2))
    command = [str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project),
               '-executeMethod', 'RecoveredBundleTests.Run', '-logFile', str(project / 'native-test.log'), '-quit']
    started = __import__('time').time()
    subprocess.run(command, check=True, timeout=180)
    if not result_path.exists() or result_path.stat().st_mtime < started:
        raise RuntimeError('Unity did not produce a new report; inspect native-test.log')
    result = json.loads(result_path.read_text())
    passed = all(b['loaded'] and not b.get('exception') and not b['missingRendererMaterials']
        and all(m['shader'] for m in b['materials']) for b in result['bundles']) and not result['errors']
    print(json.dumps({'native_load_passed': passed, 'unity_version': result['unityVersion'],
        'bundles_loaded': sum(b['loaded'] for b in result['bundles']), 'graphics_rendering_tested': False}, indent=2))
    if not passed:
        sys.exit(1)


if __name__ == '__main__':
    main()
