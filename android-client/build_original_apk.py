"""Build the original Unity client with Brazilian authentication and a local login UI."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import xml.etree.ElementTree as ET
import zipfile
import build_apk as shared

ROOT, PROJECT = shared.ROOT, shared.PROJECT
SOURCE = PROJECT / 'Durango original'
WORK = ROOT / 'work/original-nexon'
CLIENT = WORK / 'client'
DECODED = WORK / 'decoded'
OUTPUT = 'DurangoBrasil-original-alfa-3.apk'
LOGIN_FILES = {'assets/durango-br/launcher/web/' + name for name in
               ['index.html', 'mobile.js', 'logo-durango-brasil.png']}
BUNDLED = ROOT / 'bundled/tutorial'
BUNDLE_MANIFEST = json.loads((BUNDLED / 'manifest.json').read_text('utf-8'))
BUNDLE_FILES = {'assets/durango-br/tutorial/' + entry['name']
                for entry in BUNDLE_MANIFEST['bundles']} | {'assets/durango-br/tutorial/manifest.json'}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def prepare():
    WORK.mkdir(parents=True, exist_ok=True)
    # The user's original folder is read-only input. Rebuild into a disposable copy.
    shutil.copytree(SOURCE, CLIENT, dirs_exist_ok=True)
    source = WORK / 'resources-source.apk'
    with zipfile.ZipFile(source, 'w') as archive:
        for name in ['AndroidManifest.xml', 'resources.arsc', 'classes.dex']:
            archive.write(SOURCE / name, name)
        for path in (SOURCE / 'res').rglob('*'):
            if path.is_file(): archive.write(path, path.relative_to(SOURCE).as_posix())
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar',
               'd', '-s', '-f', '-o', DECODED, source)

def authentication():
    classes = WORK / 'auth-classes'; classes.mkdir(exist_ok=True)
    shared.run(shared.JAVA_BIN / 'javac.exe', '--release', '8', '-encoding', 'UTF-8',
               '-cp', ROOT / 'work/android.jar', '-d', classes,
               ROOT / 'src/com/newdawn/launcher/NewDawnApi.java',
               ROOT / 'src/com/newdawn/launcher/OriginalAuthActivity.java')
    dex = WORK / 'auth-dex'; dex.mkdir(exist_ok=True)
    shared.run(shared.JAVA_BIN / 'java.exe', '-cp', ROOT / 'work/d8.jar',
               'com.android.tools.r8.D8', '--min-api', '21', '--lib', ROOT / 'work/android.jar',
               '--output', dex, *sorted(classes.rglob('*.class')))
    shutil.copy2(dex / 'classes.dex', CLIENT / 'classes2.dex')
    login = CLIENT / 'assets/durango-br/launcher/web'
    login.mkdir(parents=True, exist_ok=True)
    for name in ['index.html', 'mobile.js', 'logo-durango-brasil.png']:
        shutil.copy2(ROOT / 'ui' / name, login / name)
    ns = 'http://schemas.android.com/apk/res/android'
    ET.register_namespace('android', ns)
    key = lambda name: '{' + ns + '}' + name
    manifest = DECODED / 'AndroidManifest.xml'
    tree = ET.parse(manifest); root = tree.getroot(); app = root.find('application')
    assert root.get('package') == 'com.nexon.durango.global'
    game = next(node for node in app.findall('activity') if node.get(key('name')) == 'com.unity3d.player.UnityPlayerActivity')
    for child in list(game):
        if child.tag == 'intent-filter': game.remove(child)
    game.set(key('exported'), 'false')
    app.set(key('allowBackup'), 'false')  # The new gateway token stays private.
    auth = ET.SubElement(app, 'activity', {
        key('name'): 'com.newdawn.launcher.OriginalAuthActivity', key('exported'): 'true',
        key('hardwareAccelerated'): 'true',
        key('screenOrientation'): 'sensorLandscape', key('theme'): '@android:style/Theme.Material.NoActionBar.Fullscreen',
        key('configChanges'): game.get(key('configChanges'))})
    intent = ET.SubElement(auth, 'intent-filter')
    ET.SubElement(intent, 'action', {key('name'): 'android.intent.action.MAIN'})
    ET.SubElement(intent, 'category', {key('name'): 'android.intent.category.LAUNCHER'})
    tree.write(manifest, encoding='utf-8', xml_declaration=True)
    yaml = DECODED / 'apktool.yml'
    text = yaml.read_text('utf-8')
    text = re.sub(r'versionCode: \d+', 'versionCode: 50204', text)
    text = re.sub(r'versionName: [^\n]+', 'versionName: 5.2.1-br-alfa3', text)
    yaml.write_text(text, 'utf-8')
    rebuilt = WORK / 'resources-rebuilt.apk'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'b', DECODED, '-o', rebuilt)
    with zipfile.ZipFile(rebuilt) as archive:
        # Retain the original resource table and every original image/XML resource.
        (CLIENT / 'AndroidManifest.xml').write_bytes(archive.read('AndroidManifest.xml'))

def networking():
    raw = bytearray((SOURCE / 'lib/arm64-v8a/libil2cpp.so').read_bytes())
    assert hashlib.sha256(raw).hexdigest() == '247b21587fb661946775bc3cba5dfb3d1f0f543ce5182ec61daae71cece25eae'
    assert raw[0x960a:0x9618] == b'libstdc++.so\x00li'[:14]
    # Load the existing ARM64 request bridge and bypass the discontinued NPA login.
    raw[0x960a:0x9616] = b'libbr.so\0'.ljust(12, b'\0')
    assert struct.unpack_from('<I', raw, 0x137b240)[0] == 0x320003e0
    struct.pack_into('<I', raw, 0x137b240, 0x2a1f03e0)
    assert struct.unpack_from('<I', raw, 0x137b748)[0] == 0x97fd4c73
    struct.pack_into('<I', raw, 0x137b748, 0x14000048)
    (CLIENT / 'lib/arm64-v8a/libil2cpp.so').write_bytes(raw)
    bridge = bytearray((ROOT / 'native/original/libnd-auth.so').read_bytes())
    assert hashlib.sha256(bridge).hexdigest() == 'fe2673ea8e450887bf6fa62b8d79d568cafa404cb83f239af200ac610a1f7c32', 'Authentication bridge changed; review before building'
    assert b'AddField\0' in bridge and b'token\0' in bridge
    # Reused bridge logs must never print full or partial credentials.
    for previous, replacement in [
        (b'ticket anexado: %s\0', b'autenticado\0'),
        (b'ticket lido do Intent (%.4s...), origens: %s\0', b'credencial recebida\0')]:
        assert bridge.count(previous) == 1
        bridge = bridge.replace(previous, replacement.ljust(len(previous), b'\0'))
    (CLIENT / 'lib/arm64-v8a/libnd.so').write_bytes(bridge)
    relative = Path('assets/bin/Data/b7b0096f71e8a0640be95cc8b863f74d')
    raw = bytearray((SOURCE / relative).read_bytes())
    start = raw.index(b'{\n'); size = struct.unpack_from('<I', raw, start - 4)[0]
    config = {'offline': False, 'clusters': {'durango_brasil': {
        'gateway_url_root': shared.GATEWAY, 'name': {'en_US': 'Durango Brasil', 'pt_BR': 'Durango Brasil'}}}}
    payload = json.dumps(config, separators=(',', ':')).encode('utf-8')
    assert len(payload) <= size
    raw[start:start + size] = payload.ljust(size, b' ')
    (CLIENT / relative).write_bytes(raw)

def tutorial_bundles():
    assert len(BUNDLE_MANIFEST['bundles']) == 4
    destination = CLIENT / 'assets/durango-br/tutorial'
    destination.mkdir(parents=True, exist_ok=True)
    for entry in BUNDLE_MANIFEST['bundles']:
        source = BUNDLED / entry['name']
        assert digest(source) == entry['sha256'] and source.stat().st_size == entry['bytes']
        shutil.copy2(source, destination / entry['name'])
    shutil.copy2(BUNDLED / 'manifest.json', destination / 'manifest.json')
    compiler = Path(os.environ.get('ANDROID_NDK_HOME',
        Path(os.environ['LOCALAPPDATA']) / 'Android/Sdk/ndk/28.2.13676358')) / 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
    shared.run(compiler, '--target=aarch64-linux-android21', '-shared', '-fPIC', '-O2',
        ROOT / 'native/original/tutorial_bundles.c', '-o', CLIENT / 'lib/arm64-v8a/libbr.so',
        '-L' + str(ROOT / 'native/original'), '-Wl,--no-as-needed', '-l:libnd-auth.so',
        '-llog', '-ldl', '-pthread', '-Wl,-z,max-page-size=16384')

def verify():
    changed = []
    allowed = {'AndroidManifest.xml', 'lib/arm64-v8a/libil2cpp.so',
               'assets/bin/Data/b7b0096f71e8a0640be95cc8b863f74d'}
    preserved = 0
    for original in SOURCE.rglob('*'):
        if not original.is_file(): continue
        name = original.relative_to(SOURCE).as_posix()
        if digest(original) != digest(CLIENT / name):
            assert name in allowed, 'Unexpected original file changed: ' + name
            changed.append(name)
        else: preserved += 1
    assert set(changed) == allowed
    added = {p.relative_to(CLIENT).as_posix() for p in CLIENT.rglob('*') if p.is_file() and not (SOURCE / p.relative_to(CLIENT)).exists()}
    assert added == {'classes2.dex', 'lib/arm64-v8a/libnd.so', 'lib/arm64-v8a/libbr.so'} | LOGIN_FILES | BUNDLE_FILES
    report = {'source': str(SOURCE), 'unity': '2017.4.34f1', 'gateway': shared.GATEWAY,
              'preserved_files': preserved, 'changed_files': sorted(changed), 'added_files': sorted(added),
              'game_visual_assets_unchanged': True, 'original_folder_modified': False,
              'login_video': 'assets/Movie/Mobile/title.mp4', 'login_requires_player_action': True,
              'tutorial_dependencies': BUNDLE_MANIFEST['bundles']}
    (WORK / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print('Verified: game resources and Unity retained; gateway/authentication and local login UI updated.', flush=True)

def main():
    prepare(); authentication(); networking(); tutorial_bundles(); verify()
    shared.APK = CLIENT; shared.OUTPUT_NAME = OUTPUT; shared.PACKAGE_EXCLUDES = set()
    shared.DIST.mkdir(exist_ok=True)
    shared.package()
    with zipfile.ZipFile(shared.DIST / OUTPUT) as archive:
        for path in CLIENT.rglob('*'):
            if not path.is_file(): continue
            name = path.relative_to(CLIENT).as_posix()
            if name.startswith('META-INF/') and (path.suffix.upper() in ['.SF', '.RSA', '.DSA', '.EC'] or path.name == 'MANIFEST.MF'):
                continue
            assert hashlib.sha256(archive.read(name)).hexdigest() == digest(path), 'Packaged file changed: ' + name
    print('Verified: signed APK contains the validated client files.', flush=True)

if __name__ == '__main__': main()
