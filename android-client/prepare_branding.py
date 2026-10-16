"""Import the user's edited Unity textures and splash from DurangoBrasilApk."""
import hashlib
import json
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parent
APK = ROOT.parent / 'DurangoBrasilApk'
FILES = {
    'loading_atlas': 'assets/bin/Data/bcb37f4b3d27e4a4f93a3ec965e256dc',
    'character_logo': 'assets/bin/Data/c6c98a0f398372a41862a640adc466d2',
    'team_splash': 'res/drawable/unity_static_splash.png',
}


def main():
    # UnityPy is only needed to extract the matching PNG for the HTML login.
    sys.path.insert(0, str(ROOT / 'work/python-deps'))
    import UnityPy
    manifest = {'team': 'Vision Force', 'server_label': 'Servidor Brasileiro',
                'display_version': '1.0', 'files': []}
    for role, relative in FILES.items():
        source = APK / relative
        target = ROOT / 'branding/resources' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        manifest['files'].append({'role': role, 'path': relative, 'bytes': target.stat().st_size,
                                 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    env = UnityPy.load(str((APK / FILES['character_logo']).resolve()))
    texture = next(obj for obj in env.objects if obj.type.name == 'Texture2D' and obj.peek_name() == 'logo_eng')
    image = texture.parse_as_object().image
    output = ROOT / 'ui/logo-durango-brasil.png'
    image.save(output)
    (ROOT / 'ui/logo-source.json').write_text(json.dumps({
        'source': 'DurangoBrasilApk/' + FILES['character_logo'], 'texture_name': 'logo_eng',
        'texture_path_id': texture.path_id, 'dimensions': list(image.size),
        'unity_version': texture.assets_file.unity_version, 'extractor': 'UnityPy ' + UnityPy.__version__,
        'png_sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
        'unity_file_sha256': next(entry['sha256'] for entry in manifest['files'] if entry['role'] == 'character_logo')
    }, indent=2) + '\n', encoding='utf-8')
    (ROOT / 'branding/manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'branding_imported': True, 'files': len(manifest['files']), 'logo_size': image.size}), flush=True)


if __name__ == '__main__':
    main()
