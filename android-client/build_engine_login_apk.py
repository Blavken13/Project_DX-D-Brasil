"""Build the Unity login modal on the verified gameplay 50225 baseline."""
import argparse
import hashlib
import json
import re
import shutil
import struct
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET
import build_apk as shared
import build_poco_f7_apk as baseline
from build_diagnostic_compat_apk import signature_file
from engine_login_original import offsets_header, metadata_types

ROOT = shared.ROOT
WORK = ROOT / 'work/engine-login-50226'
CLIENT = WORK / 'client'
DECODED = WORK / 'decoded'
SOURCE = ROOT / 'dist/LostHorizon-alfa-gameplay-50225.apk'
SOURCE_HASH = 'c3efe2a01b36bbda1cff269d77d13f245f81f9641a4c9822e8833307eb42238c'
OUTPUT = 'LostHorizon-login-no-jogo-50226.apk'
STAMP = 'assets/durango-br/diagnostics/build.json'
NATIVE = 'lib/arm64-v8a/libenginelogin.so'


def prepare():
    if baseline.sha256(SOURCE) != SOURCE_HASH: raise ValueError('Baseline checksum mismatch')
    if WORK.exists(): raise ValueError('Build work exists; use --resume or a new isolated workspace')
    baseline.WORK, baseline.CLIENT, baseline.DECODED = WORK, CLIENT, DECODED
    baseline.SOURCE, baseline.SOURCE_HASH = SOURCE, SOURCE_HASH
    baseline.prepare()


def helpers():
    stub = WORK / 'stubs/com/newdawn/launcher'; stub.mkdir(parents=True, exist_ok=True)
    sources = {
        'NewDawnApi': '''class NewDawnApi {
 static class Session { String accessToken,refreshToken,userId,email; }
 static class ApiException extends java.io.IOException { int messageId; boolean sessionLost; }
 static Session authenticate(String u,String p,boolean r) throws java.io.IOException{return null;}
 static Session restore(String s) throws java.io.IOException{return null;}
}''',
        'NativeRuntime': '''class NativeRuntime { static void activity(android.app.Activity a){}
 static boolean initialize(String t,String d,boolean c,boolean v){return false;} }''',
        'CrashDiagnostics': '''class CrashDiagnostics {
 static void stage(android.content.Context c,String s){}
 static java.io.File directory(android.content.Context c){return null;}
 static java.io.File latest(android.content.Context c){return null;}
 static boolean compatible(android.content.Context c){return false;}
 static boolean disableTitleVideo(android.content.Context c){return false;}
 static void compatible(android.content.Context c,boolean b){}
 static void disableTitleVideo(android.content.Context c,boolean b){}
}'''}
    for name, body in sources.items(): (stub / (name + '.java')).write_text('package com.newdawn.launcher;\n' + body, 'utf-8')
    classes = WORK / 'classes'; classes.mkdir(exist_ok=True)
    android = shared.SDK / 'platforms/android-36/android.jar'
    shared.run(shared.JAVA_BIN / 'javac.exe', '--release', '8', '-encoding', 'UTF-8', '-cp', android,
               '-d', classes, *sorted(stub.glob('*.java')), ROOT / 'src/com/newdawn/launcher/EngineLogin.java')
    stubs = WORK / 'stubs.jar'
    with zipfile.ZipFile(stubs, 'w') as z:
        for p in classes.rglob('*.class'):
            if not p.name.startswith('EngineLogin'): z.write(p, p.relative_to(classes).as_posix())
    dex = WORK / 'helpers-dex'; dex.mkdir(exist_ok=True)
    shared.run(shared.JAVA_BIN / 'java.exe', '-cp', shared.TOOLS / 'lib/d8.jar', 'com.android.tools.r8.D8',
               '--min-api', '21', '--lib', android, '--classpath', stubs, '--output', dex,
               *sorted(p for p in classes.rglob('*.class') if p.name.startswith('EngineLogin')))
    mini = WORK / 'helpers.apk'
    with zipfile.ZipFile(mini, 'w') as z:
        z.write(dex / 'classes.dex', 'classes.dex')
        for name in ('AndroidManifest.xml', 'resources.arsc'): z.write(CLIENT / name, name)
    dest = WORK / 'helpers-decoded'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'd', '-r', '-f', '-o', dest, mini)
    for p in (dest / 'smali/com/newdawn/launcher').glob('*.smali'):
        if not p.name.startswith('EngineLogin'): raise ValueError('Stub leaked into shipping code')
        shutil.copy2(p, DECODED / 'smali_classes2/com/newdawn/launcher' / p.name)


def integrate():
    game = DECODED / 'smali_classes2/com/newdawn/launcher/CompatGameActivity.smali'
    backup = WORK / 'game-original.smali'
    if not backup.exists(): backup.write_text(game.read_text('utf-8'), 'utf-8')
    text = baseline.method(backup.read_text('utf-8'), 'onCreate(Landroid/os/Bundle;)V', '''    .locals 2
    const/4 v0, 0x1
    invoke-virtual {p0, v0}, Lcom/newdawn/launcher/CompatGameActivity;->requestWindowFeature(I)Z
    invoke-super {p0, p1}, Landroid/app/Activity;->onCreate(Landroid/os/Bundle;)V
    invoke-static {}, Lcom/newdawn/launcher/MobileReports;->stopForGame()V
    :try_start
    invoke-static {p0}, Lcom/newdawn/launcher/EngineLogin;->prepareGame(Landroid/app/Activity;)Z
    move-result v0
    if-eqz v0, :failed
    const-string v0, "UNITY_STARTING"
    invoke-static {p0, v0}, Lcom/newdawn/launcher/CrashDiagnostics;->stage(Landroid/content/Context;Ljava/lang/String;)V
    new-instance v0, Lcom/unity3d/player/UnityPlayer;
    invoke-direct {v0, p0}, Lcom/unity3d/player/UnityPlayer;-><init>(Landroid/content/Context;)V
    iput-object v0, p0, Lcom/newdawn/launcher/CompatGameActivity;->player:Lcom/unity3d/player/UnityPlayer;
    invoke-virtual {p0, v0}, Lcom/newdawn/launcher/CompatGameActivity;->setContentView(Landroid/view/View;)V
    invoke-virtual {v0}, Lcom/unity3d/player/UnityPlayer;->requestFocus()Z
    :try_end
    return-void
    .catch Ljava/lang/LinkageError; {:try_start .. :try_end} :error
    .catch Ljava/lang/RuntimeException; {:try_start .. :try_end} :error
    :error
    move-exception v0
    const-string v1, "ENGINE_LOGIN_START_FAILED"
    invoke-static {p0, v1, v0}, Lcom/newdawn/launcher/CrashDiagnostics;->failure(Landroid/content/Context;Ljava/lang/String;Ljava/lang/Throwable;)V
    :failed
    invoke-direct {p0}, Lcom/newdawn/launcher/CompatGameActivity;->failed()V
    return-void''')
    text = baseline.method(text, 'failed()V', '''    .locals 0
    invoke-static {p0}, Lcom/newdawn/launcher/EngineLogin;->startFailure(Landroid/app/Activity;)V
    return-void''')
    game.write_text(text, 'utf-8')
    ns = 'http://schemas.android.com/apk/res/android'; ET.register_namespace('android', ns)
    a = lambda name: '{' + ns + '}' + name
    path = DECODED / 'AndroidManifest.xml'; tree = ET.parse(path); app = tree.getroot().find('application')
    app.set(a('label'), 'Lost Horizon')
    activities = {e.get(a('name')): e for e in app.findall('activity')}
    old = activities['com.newdawn.launcher.OriginalAuthActivity']; current = activities['com.newdawn.launcher.CompatGameActivity']
    for f in list(old.findall('intent-filter')): old.remove(f); current.append(f)
    old.set(a('exported'), 'false'); current.set(a('exported'), 'true')
    current.set(a('theme'), '@android:style/Theme.Material.NoActionBar.Fullscreen')
    tree.write(path, encoding='utf-8', xml_declaration=True)
    config = DECODED / 'apktool.yml'; text = config.read_text('utf-8')
    text = re.sub(r'(versionCode:)[^\n]+', r'\1 50226', text)
    text = re.sub(r'(versionName:)[^\n]+', r'\1 5.2.1-losthorizon-engine-login1', text)
    config.write_text(text, 'utf-8')
    rebuilt = WORK / 'rebuilt.apk'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'b', '-p', WORK / 'framework', DECODED, '-o', rebuilt)
    with zipfile.ZipFile(rebuilt) as z:
        for name in ('classes2.dex', 'AndroidManifest.xml'): (CLIENT / name).write_bytes(z.read(name))


def native():
    offsets = offsets_header((CLIENT / 'lib/arm64-v8a/libnd.so').read_bytes(),
                             (CLIENT / 'lib/arm64-v8a/libil2cpp.so').read_bytes(), WORK / 'engine_login_offsets.h')
    ndk = shared.SDK / 'ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin'
    shared.run(ndk / 'clang.exe', '--target=aarch64-linux-android21', '-shared', '-fPIC', '-O2',
               '-Wall', '-Wextra', '-Werror', '-Wl,-z,max-page-size=16384', '-Wl,-z,common-page-size=16384',
               '-Wl,-soname,libenginelogin.so', '-I', WORK, '-L', CLIENT / 'lib/arm64-v8a',
               '-o', CLIENT / NATIVE, ROOT / 'native/original/engine_login.c', '-lnd', '-ldl', '-llog')
    (WORK / 'offsets.json').write_text(json.dumps(offsets, indent=2), 'utf-8')


def package():
    build = json.loads((CLIENT / STAMP).read_text('utf-8'))
    build.update(version_code=50226, version_name='5.2.1-losthorizon-engine-login1',
                 engine_login={'ui': 'Unity modal in character selection', 'baseline_sha256': SOURCE_HASH,
                               'activity_handoff_after_auth': False, 'physical_validation': 'pending'})
    (CLIENT / STAMP).write_text(json.dumps(build, indent=2) + '\n', 'utf-8')
    for name in ('zipalign.exe', 'apksigner.jar'): shutil.copy2(ROOT / 'work' / name, WORK / name)
    shared.APK, shared.WORK, shared.OUTPUT_NAME = CLIENT, WORK, OUTPUT
    shared.package()


def verify():
    output = ROOT / 'dist' / OUTPUT
    allowed = {'AndroidManifest.xml', 'classes2.dex', STAMP, NATIVE}
    with zipfile.ZipFile(SOURCE) as old, zipfile.ZipFile(output) as new:
        names = lambda z: {n for n in z.namelist() if not n.endswith('/') and not signature_file(n)}
        before, after = names(old), names(new)
        assert before - after == set() and after - before == {NATIVE}
        changed = {n for n in before & after if old.read(n) != new.read(n)}
        assert changed == allowed - {NATIVE}, changed
        raw = new.read(NATIVE)
        phoff = struct.unpack_from('<Q', raw, 32)[0]
        phsize, phnum = struct.unpack_from('<HH', raw, 54)
        segments = [struct.unpack_from('<IIQQQQQQ', raw, phoff + i * phsize) for i in range(phnum)]
        loads = [s for s in segments if s[0] == 1]
        assert loads and all(s[7] >= 16384 and s[2] % 16384 == s[3] % 16384 for s in loads)
    certs = []
    for p in (SOURCE, output):
        report = subprocess.check_output([str(shared.JAVA_BIN / 'java.exe'), '-jar', str(ROOT / 'work/apksigner.jar'),
                                          'verify', '--print-certs', str(p)], text=True)
        certs.append(next(l for l in report.splitlines() if 'certificate SHA-256 digest:' in l))
    assert certs[0] == certs[1]
    evidence = {'apk': output.name, 'sha256': baseline.sha256(output), 'version_code': 50226,
                'changed': sorted(changed), 'added': [NATIVE], 'same_signer': True,
                'physical_validation': 'pending', 'offsets': json.loads((WORK / 'offsets.json').read_text())}
    physical = WORK / 'device/physical-validation.json'
    if physical.exists():
        device = json.loads(physical.read_text('utf-8'))
        if device['sha256'] == evidence['sha256'] and device['installed_sha256'] == evidence['sha256']:
            evidence['physical_validation'] = device
    evidence['new_library_load_alignment'] = [s[7] for s in loads]
    (ROOT / 'dist/engine-login-50226-validation.json').write_text(json.dumps(evidence, indent=2) + '\n', 'utf-8')
    print(json.dumps(evidence, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--resume', action='store_true')
    parser.add_argument('--verify-only', action='store_true'); args = parser.parse_args()
    if args.verify_only:
        verify()
        sys.path.insert(0, str(ROOT / 'tests'))
        from verify_engine_login_native import verify as native_tests
        print(json.dumps(native_tests(), indent=2)); exit()
    if not args.resume: prepare()
    helpers(); integrate(); native(); package(); verify()
