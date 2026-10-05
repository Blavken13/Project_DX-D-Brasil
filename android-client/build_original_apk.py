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
import branding_original as branding
import presentation_original as presentation
import runtime_settings_original as runtime_settings

ROOT, PROJECT = shared.ROOT, shared.PROJECT
SOURCE = PROJECT / 'Durango original'
WORK = ROOT / 'work/original-nexon'
CLIENT = WORK / 'client'
DECODED = WORK / 'decoded'
VERSION_CODE = 50216
VERSION_NAME = '5.2.1-losthorizon-alfa-tutorial-stability4'
OUTPUT = os.environ.get('LH_APK_OUTPUT', 'LostHorizon-alfa-tutorial-stability4-50216.apk')
if Path(OUTPUT).name != OUTPUT or not OUTPUT.endswith('.apk'):
    raise ValueError('LH_APK_OUTPUT must be an APK filename')
ANDROID_JAR = shared.SDK / 'platforms/android-36/android.jar'
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
               'if', ANDROID_JAR, '-p', WORK / 'framework')
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar',
               'd', '-s', '-f', '-p', WORK / 'framework', '-o', DECODED, source)

def authentication():
    classes = WORK / 'compat-classes'
    if classes.exists():
        assert classes.resolve().is_relative_to(WORK.resolve()), 'Compile output must stay inside work'
        shutil.rmtree(classes)
    classes.mkdir()
    stub = WORK / 'compile-stubs/com/unity3d/player/UnityPlayer.java'
    stub.parent.mkdir(parents=True, exist_ok=True)
    # Compile-only declaration. The actual class is retained in original classes.dex.
    stub.write_text('''package com.unity3d.player;
public class UnityPlayer extends android.widget.FrameLayout {
 public UnityPlayer(android.content.Context c){super(c);}
 public void quit(){} public void pause(){} public void resume(){}
 public void start(){} public void stop(){} public void lowMemory(){}
 public void configurationChanged(android.content.res.Configuration c){}
 public void windowFocusChanged(boolean f){}
 public boolean injectEvent(android.view.InputEvent e){return false;}
}''', 'utf-8')
    shared.run(shared.JAVA_BIN / 'javac.exe', '--release', '8', '-encoding', 'UTF-8',
               '-cp', ANDROID_JAR, '-d', classes, stub,
               *(ROOT / 'src/com/newdawn/launcher' / name for name in
                 ['NewDawnApi.java', 'OriginalAuthActivity.java', 'NativeRuntime.java',
                  'CompatGameActivity.java', 'DiagnosticApplication.java',
                  'CrashDiagnostics.java', 'ReportProvider.java', 'TombstoneSummary.java',
                  'MobileReports.java', 'MobileReportQueue.java', 'MobileReportTransport.java',
                  'LegacyServicePolicy.java', 'AnrSummary.java']))
    compile_api = WORK / 'unity-compile-api.jar'
    with zipfile.ZipFile(compile_api, 'w') as archive:
        path = classes / 'com/unity3d/player/UnityPlayer.class'
        archive.write(path, 'com/unity3d/player/UnityPlayer.class')
    dex = WORK / 'auth-dex'; dex.mkdir(exist_ok=True)
    shared.run(shared.JAVA_BIN / 'java.exe', '-cp', ROOT / 'work/d8.jar',
               'com.android.tools.r8.D8', '--min-api', '21', '--lib', ANDROID_JAR,
               '--classpath', compile_api,
               '--output', dex, *sorted((classes / 'com/newdawn').rglob('*.class')))
    shutil.copy2(dex / 'classes.dex', CLIENT / 'classes2.dex')
    login = CLIENT / 'assets/durango-br/launcher/web'
    login.mkdir(parents=True, exist_ok=True)
    for name in ['index.html', 'mobile.js', 'logo-durango-brasil.png']:
        source = PROJECT / 'logo.png' if name == 'logo-durango-brasil.png' else ROOT / 'ui' / name
        shutil.copy2(source, login / name)
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
    game.set(key('label'), 'Lost Horizon')
    app.set(key('label'), 'Lost Horizon')
    app.set(key('allowBackup'), 'false')  # The new gateway token stays private.
    app.set(key('name'), 'com.newdawn.launcher.DiagnosticApplication')
    app.set(key('pageSizeCompat'), 'enabled')
    endpoint = os.environ.get('LH_DIAGNOSTICS_ENDPOINT', 'https://179.197.72.129/client-reports/mobile')
    if endpoint:
        from urllib.parse import urlparse
        import ipaddress
        url = urlparse(endpoint)
        local = os.environ.get('LH_DIAGNOSTICS_LOCAL', '') == '1'
        if url.scheme not in ['http', 'https'] or (url.scheme != 'https' and not local) or url.username or url.password or url.query or url.fragment:
            raise ValueError('Use HTTPS, or LH_DIAGNOSTICS_LOCAL=1 for explicit local HTTP testing')
        if url.scheme == 'http':
            address = ipaddress.ip_address(url.hostname)
            if not (address.is_private or address.is_loopback):
                raise ValueError('Local HTTP diagnostics requires a private/loopback IP')
        ET.SubElement(app, 'meta-data', {key('name'): 'lh.diagnostics.endpoint', key('value'): endpoint})
        ET.SubElement(app, 'meta-data', {key('name'): 'lh.diagnostics.local', key('value'): str(local).lower()})
    import copy
    compatible_game = copy.deepcopy(game)
    compatible_game.set(key('name'), 'com.newdawn.launcher.CompatGameActivity')
    app.append(compatible_game)
    ET.SubElement(app, 'provider', {
        key('name'): 'com.newdawn.launcher.ReportProvider',
        key('authorities'): 'com.nexon.durango.global.lh.reports',
        key('exported'): 'false', key('grantUriPermissions'): 'true'})
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
    text = re.sub(r'versionCode: \d+', f'versionCode: {VERSION_CODE}', text)
    text = re.sub(r'versionName: [^\n]+', f'versionName: {VERSION_NAME}', text)
    text = re.sub(r'minSdkVersion: [^\n]+', "minSdkVersion: '21'", text)
    yaml.write_text(text, 'utf-8')
    rebuilt = WORK / 'resources-rebuilt.apk'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'b', '-p', WORK / 'framework', DECODED, '-o', rebuilt)
    with zipfile.ZipFile(rebuilt) as archive:
        # Retain the original resource table and IDs; branding replaces PNG payloads later.
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
        ROOT / 'native/original/runtime_compat.c', '-Wall', '-Wextra', '-Werror',
        '-Wno-unused-parameter', '-Wl,-soname,libnd.so', '-Wl,-z,max-page-size=16384',
        '-Wl,-z,common-page-size=16384', '-o', CLIENT / 'lib/arm64-v8a/libnd.so',
        '-llog', '-ldl', '-pthread')
    shared.run(compiler, '--target=aarch64-linux-android21', '-shared', '-fPIC', '-O2',
        ROOT / 'native/original/tutorial_bundles.c', ROOT / 'native/original/tutorial_raft_k.c',
        '-Wall', '-Wextra', '-Werror', '-Wno-unused-parameter', '-o', CLIENT / 'lib/arm64-v8a/libbr.so',
        '-L' + str(CLIENT / 'lib/arm64-v8a'), '-Wl,--no-as-needed', '-l:libnd.so',
        '-llog', '-ldl', '-pthread', '-Wl,-z,max-page-size=16384', '-Wl,-z,common-page-size=16384')

def verify():
    changed = []
    allowed = {'AndroidManifest.xml', 'lib/arm64-v8a/libil2cpp.so',
               presentation.METADATA.as_posix(),
               runtime_settings.SETTINGS.as_posix(),
               'assets/bin/Data/b7b0096f71e8a0640be95cc8b863f74d'} | branding.changed_files(SOURCE)
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
              'branding': branding.verify(SOURCE, CLIENT),
              'presentation': presentation.verify(SOURCE, CLIENT),
              'unity_shaders_preserved': True, 'original_folder_modified': False,
              'runtime_settings': runtime_settings.verify(SOURCE, CLIENT),
              'login_video': 'assets/Movie/Mobile/title.mp4', 'login_requires_player_action': True,
              'compatibility': {'own_source_auth': True, 'patch_before_unity_player': True,
                                'title_video_enabled_by_default': True, 'title_video_independent_option': True,
                                'runtime_page_size': True, 'reports_private': True,
                                'legacy_unity_16kb_requires_android_compat_mode': True},
              'stability': {'version_code': VERSION_CODE, 'version_name': VERSION_NAME,
                            'tutorial_gameplay_breadcrumbs': True,
                            'raft_restoration_scoped_to_active_objective': True,
                            'compatibility_max_resolution_edge': 960,
                            'service_bind_logcat_markers': True,
                            'startup_auth_operation_breadcrumbs': True,
                            'auth_diagnostic_request_limit': 4,
                            'legacy_google_ad_id_bind_disabled': True,
                            'auth_connect_timeout_seconds': 10, 'auth_request_timeout_seconds': 20,
                            'auth_automatic_retry_disabled': True, 'auth_callback_breadcrumbs': True,
                            'anr_main_thread_java_frames_only': True},
              'tutorial_dependencies': BUNDLE_MANIFEST['bundles'],
              'raft_k': {'entity_id': '502', 'todo': 'talk_npc_raft_ancora.meet_chief',
                         'creates_missing_npc_at_tutorial_boat': True, 'automatic_todo_completion': False}}
    (WORK / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print('Verified: Lost Horizon branding; Unity, shaders and unrelated game resources retained.', flush=True)

def main():
    prepare(); authentication(); networking(); tutorial_bundles()
    presentation.apply(SOURCE, CLIENT)
    runtime_settings.apply(SOURCE, CLIENT)
    branding.apply(SOURCE, CLIENT, WORK); verify()
    shared.APK = CLIENT; shared.OUTPUT_NAME = OUTPUT; shared.PACKAGE_EXCLUDES = set()
    shared.DIST.mkdir(exist_ok=True)
    shared.package()
    with zipfile.ZipFile(shared.DIST / OUTPUT) as archive:
        assert archive.getinfo(presentation.MOVIE.as_posix()).compress_type == zipfile.ZIP_STORED, 'Movie must support AssetManager.openFd'
        for path in CLIENT.rglob('*'):
            if not path.is_file(): continue
            name = path.relative_to(CLIENT).as_posix()
            if name.startswith('META-INF/') and (path.suffix.upper() in ['.SF', '.RSA', '.DSA', '.EC'] or path.name == 'MANIFEST.MF'):
                continue
            assert hashlib.sha256(archive.read(name)).hexdigest() == digest(path), 'Packaged file changed: ' + name
    print('Verified: signed APK contains the validated client files.', flush=True)

if __name__ == '__main__': main()
