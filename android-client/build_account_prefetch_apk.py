"""Build 50224: account summaries load on the login screen, then feed Unity."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile
import build_apk as shared
import build_poco_f7_apk as baseline
from build_diagnostic_compat_apk import signature_file

ROOT = shared.ROOT
SOURCE = ROOT / 'dist/LostHorizon-poco-f7-teste-sem-observadores-50223.apk'
SOURCE_HASH = '7b68daf23fc4e4277dc0166b4208015273711a45c8eec21934bfa67ded4cec5b'
WORK = ROOT / 'work/account-prefetch-50224'
CLIENT = WORK / 'client'
DECODED = WORK / 'decoded'
VERSION_CODE = 50224
VERSION_NAME = '5.2.1-losthorizon-conta1'
OUTPUT = 'LostHorizon-conta-precarregada-50224.apk'
STAMP = 'assets/durango-br/diagnostics/build.json'
NATIVE = 'lib/arm64-v8a/liblhaccount.so'

def replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError('Unexpected baseline integration point: ' + old[:80])
    return text.replace(old, new)

def compile_helpers():
    stub = WORK / 'stubs/com/newdawn/launcher'; stub.mkdir(parents=True)
    (stub / 'NewDawnApi.java').write_text('''package com.newdawn.launcher;
class NewDawnApi {
 static final String DEFAULT_GATEWAY="http://179.197.72.129:8190";
 static class Session { String accessToken,userId; Session(String a,String b,String c,String d,long e){} boolean expiringSoon(){return false;} }
 static class ApiException extends java.io.IOException { ApiException(int id,boolean lost){} }
}''', 'utf-8')
    (stub / 'OriginalAuthActivity.java').write_text('''package com.newdawn.launcher;
public class OriginalAuthActivity extends android.app.Activity {
 public void accountReady(NewDawnApi.Session session){}
 public void accountFailed(Exception failure){}
}''', 'utf-8')
    (stub / 'CrashDiagnostics.java').write_text('''package com.newdawn.launcher;
class CrashDiagnostics { static void stage(android.content.Context context,String name){} }''', 'utf-8')
    classes = WORK / 'classes'; classes.mkdir()
    android = shared.SDK / 'platforms/android-36/android.jar'
    shared.run(shared.JAVA_BIN / 'javac.exe', '--release', '8', '-encoding', 'UTF-8', '-cp', android,
               '-d', classes, *sorted(stub.glob('*.java')),
               ROOT / 'src/com/newdawn/launcher/AccountPreloader.java',
               ROOT / 'src/com/newdawn/launcher/AccountSnapshot.java')
    # Stubs are compile-time references only; shipping them would overwrite the launcher.
    stubs = WORK / 'stubs.jar'
    with zipfile.ZipFile(stubs, 'w') as archive:
        for cls in classes.rglob('*.class'):
            if not cls.name.startswith(('AccountPreloader', 'AccountSnapshot')):
                archive.write(cls, cls.relative_to(classes).as_posix())
    dex = WORK / 'helpers-dex'; dex.mkdir()
    helpers = [p for p in classes.rglob('*.class') if p.name.startswith(('AccountPreloader', 'AccountSnapshot'))]
    shared.run(shared.JAVA_BIN / 'java.exe', '-cp', shared.TOOLS / 'lib/d8.jar',
               'com.android.tools.r8.D8', '--min-api', '21', '--lib', android,
               '--classpath', stubs, '--output', dex, *helpers)
    mini = WORK / 'helpers.apk'
    with zipfile.ZipFile(mini, 'w') as archive:
        archive.write(dex / 'classes.dex', 'classes.dex')
        archive.write(CLIENT / 'AndroidManifest.xml', 'AndroidManifest.xml')
        archive.write(CLIENT / 'resources.arsc', 'resources.arsc')
    helpers_decoded = WORK / 'helpers-decoded'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'd', '-r', '-f',
               '-o', helpers_decoded, mini)
    dest = DECODED / 'smali_classes2/com/newdawn/launcher'
    for path in (helpers_decoded / 'smali/com/newdawn/launcher').glob('*.smali'):
        if not path.name.startswith(('AccountPreloader', 'AccountSnapshot')):
            raise ValueError('Compile-time stub leaked into helpers DEX')
        shutil.copy2(path, dest / path.name)

def integrate():
    auth = DECODED / 'smali_classes2/com/newdawn/launcher/OriginalAuthActivity.smali'
    original = auth.read_text('utf-8')
    text = replace_once(original, '.method private launch(Lcom/newdawn/launcher/NewDawnApi$Session;)V',
                       '.method private launchPrepared(Lcom/newdawn/launcher/NewDawnApi$Session;)V')
    text = replace_once(text, 'Lcom/newdawn/launcher/NewDawnApi;->restore(Ljava/lang/String;)Lcom/newdawn/launcher/NewDawnApi$Session;',
                        'Lcom/newdawn/launcher/AccountPreloader;->restore(Ljava/lang/String;)Lcom/newdawn/launcher/NewDawnApi$Session;')
    text += '''
.method private launch(Lcom/newdawn/launcher/NewDawnApi$Session;)V
    .locals 1
    invoke-direct {p0}, Lcom/newdawn/launcher/OriginalAuthActivity;->active()Z
    move-result v0
    if-eqz v0, :done
    iget-boolean v0, p0, Lcom/newdawn/launcher/OriginalAuthActivity;->opening:Z
    if-nez v0, :done
    const/4 v0, 0x1
    iput-boolean v0, p0, Lcom/newdawn/launcher/OriginalAuthActivity;->opening:Z
    invoke-direct {p0, v0}, Lcom/newdawn/launcher/OriginalAuthActivity;->setBusy(Z)V
    invoke-static {p0, p1}, Lcom/newdawn/launcher/AccountPreloader;->start(Lcom/newdawn/launcher/OriginalAuthActivity;Lcom/newdawn/launcher/NewDawnApi$Session;)V
    :done
    return-void
.end method

.method public accountReady(Lcom/newdawn/launcher/NewDawnApi$Session;)V
    .locals 1
    const/4 v0, 0x0
    iput-boolean v0, p0, Lcom/newdawn/launcher/OriginalAuthActivity;->opening:Z
    invoke-direct {p0, p1}, Lcom/newdawn/launcher/OriginalAuthActivity;->launchPrepared(Lcom/newdawn/launcher/NewDawnApi$Session;)V
    return-void
.end method

.method public accountFailed(Ljava/lang/Exception;)V
    .locals 1
    const/4 v0, 0x0
    iput-boolean v0, p0, Lcom/newdawn/launcher/OriginalAuthActivity;->opening:Z
    const/4 v0, 0x1
    invoke-direct {p0, p1, v0}, Lcom/newdawn/launcher/OriginalAuthActivity;->failed(Ljava/lang/Exception;Z)V
    return-void
.end method
'''
    auth.write_text(text, 'utf-8')
    game = DECODED / 'smali_classes2/com/newdawn/launcher/CompatGameActivity.smali'
    text = game.read_text('utf-8')
    text = replace_once(text, ':cond_0\n    nop\n\n    .line 54', ''':cond_0
    invoke-static {p0}, Lcom/newdawn/launcher/AccountPreloader;->install(Landroid/app/Activity;)Z
    move-result p1
    if-nez p1, :account_cache_ready
    invoke-direct {p0}, Lcom/newdawn/launcher/CompatGameActivity;->failed()V
    return-void
    :account_cache_ready

    .line 54''')
    game.write_text(text, 'utf-8')
    ns = 'http://schemas.android.com/apk/res/android'
    ET.register_namespace('android', ns)
    manifest = ET.parse(DECODED / 'AndroidManifest.xml')
    manifest.getroot().find('application').set('{' + ns + '}label', 'Lost Horizon Conta Teste')
    manifest.write(DECODED / 'AndroidManifest.xml', encoding='utf-8', xml_declaration=True)
    config = DECODED / 'apktool.yml'
    text = config.read_text('utf-8')
    text, a = re.subn(r'(versionCode:)[^\n]+', rf'\1 {VERSION_CODE}', text)
    text, b = re.subn(r'(versionName:)[^\n]+', rf'\1 {VERSION_NAME}', text)
    if a != 1 or b != 1: raise ValueError('Version configuration missing')
    config.write_text(text, 'utf-8')
    output = WORK / 'rebuilt.apk'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'b',
               '-p', WORK / 'framework', DECODED, '-o', output)
    with zipfile.ZipFile(output) as archive:
        for name in ('classes2.dex', 'AndroidManifest.xml'):
            (CLIENT / name).write_bytes(archive.read(name))

def compile_native():
    ndk = shared.SDK / 'ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin'
    shared.run(ndk / 'clang.exe', '--target=aarch64-linux-android21', '-shared', '-fPIC', '-O2',
               '-Wall', '-Wextra', '-Werror', '-Wl,-z,max-page-size=16384', '-Wl,-z,common-page-size=16384',
               '-Wl,-soname,liblhaccount.so', '-o', CLIENT / NATIVE,
               ROOT / 'native/original/account_handoff.c', '-ldl', '-llog')

def verify():
    if baseline.sha256(SOURCE) != SOURCE_HASH: raise ValueError('Baseline checksum mismatch')
    output = shared.DIST / OUTPUT
    with zipfile.ZipFile(SOURCE) as source, zipfile.ZipFile(output) as target:
        if target.testzip() is not None: raise ValueError('Invalid APK archive')
        original = {n for n in source.namelist() if not n.endswith('/') and not signature_file(n)}
        current = {n for n in target.namelist() if not n.endswith('/') and not signature_file(n)}
        changed = {n for n in original if source.read(n) != target.read(n)}
        if current - original != {NATIVE} or original - current or changed != {'classes2.dex', 'AndroidManifest.xml', STAMP}:
            raise ValueError('Unexpected APK mutation')
        for name in current:
            if (name.startswith('assets/') or name.endswith('.so')) and target.getinfo(name).compress_type != zipfile.ZIP_STORED:
                raise ValueError('Native/assets compression changed')
    certs = []
    for apk in (SOURCE, output):
        text = subprocess.check_output([str(shared.JAVA_BIN / 'java.exe'), '-jar', str(ROOT / 'work/apksigner.jar'),
                                       'verify', '--print-certs', str(apk)], text=True)
        certs.append(re.findall(r'Signer #\d+ certificate SHA-256 digest: (\w+)', text))
    if not certs[0] or certs[0] != certs[1]: raise ValueError('Signing key changed')
    badging = subprocess.check_output([str(shared.TOOLS / 'aapt.exe'), 'dump', 'badging', str(output)], text=True)
    if f"versionCode='{VERSION_CODE}'" not in badging: raise ValueError('Incorrect encoded version')
    # Inspect the assembled launcher, preserving every unrelated original class.
    qa_apk = WORK / 'qa-launcher.apk'
    with zipfile.ZipFile(output) as target, zipfile.ZipFile(qa_apk, 'w') as qa:
        for name in ('AndroidManifest.xml', 'resources.arsc'): qa.writestr(name, target.read(name))
        qa.writestr('classes.dex', target.read('classes2.dex'))
    qa_decoded = WORK / 'qa-decoded'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'd', '-r', '-f',
               '-o', qa_decoded, qa_apk)
    reference_apk = WORK / 'qa-reference.apk'
    with zipfile.ZipFile(SOURCE) as source, zipfile.ZipFile(reference_apk, 'w') as reference:
        for name in ('AndroidManifest.xml', 'resources.arsc'): reference.writestr(name, source.read(name))
        reference.writestr('classes.dex', source.read('classes2.dex'))
    reference_decoded = WORK / 'qa-reference'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar', 'd', '-r', '-f',
               '-o', reference_decoded, reference_apk)
    original_classes = reference_decoded / 'smali'
    assembled = qa_decoded / 'smali'
    original_names = {p.relative_to(original_classes) for p in original_classes.rglob('*.smali')}
    current_names = {p.relative_to(assembled) for p in assembled.rglob('*.smali')}
    modified = {Path('com/newdawn/launcher/OriginalAuthActivity.smali'), Path('com/newdawn/launcher/CompatGameActivity.smali')}
    if original_names-current_names or any(not name.name.startswith(('AccountPreloader','AccountSnapshot')) for name in current_names-original_names):
        raise ValueError('Unexpected assembled launcher class list')
    for name in original_names-modified:
        if (original_classes/name).read_bytes() != (assembled/name).read_bytes():
            raise ValueError('Unrelated launcher class changed: '+str(name))
    auth = (assembled/'com/newdawn/launcher/OriginalAuthActivity.smali').read_text('utf-8')
    game = (assembled/'com/newdawn/launcher/CompatGameActivity.smali').read_text('utf-8')
    if 'Lcom/newdawn/launcher/NewDawnApi;->restore(' in auth or 'Lcom/newdawn/launcher/AccountPreloader;->restore(' not in auth:
        raise ValueError('Saved session still downloads account before loading bar')
    if game.index('Lcom/newdawn/launcher/AccountPreloader;->install(') > game.index('new-instance p1, Lcom/unity3d/player/UnityPlayer;'):
        raise ValueError('Account bridge installed too late')
    report = dict(version_code=VERSION_CODE, version_name=VERSION_NAME, baseline_sha256=SOURCE_HASH,
                  apk_sha256=baseline.sha256(output), changed_files=sorted(changed), added_files=[NATIVE],
                  preserved_files=len(original)-len(changed), signing_certificate_matches=True,
                  unrelated_launcher_classes_preserved=len(original_names)-len(modified),
                  original_dex_unity_il2cpp_libnd_and_game_assets_preserved=True,
                  account_preload='validated in launcher, one-use loopback response to original BestHTTP parser',
                  persistent_account_cache=False, physical_validation='pending USB test; Poco F7 unavailable')
    validation_file = ROOT / 'docs/validacao-conta-50224-usb.json'
    if validation_file.is_file():
        validation = json.loads(validation_file.read_text('utf-8'))
        if validation.get('apk_sha256') == report['apk_sha256'] and validation.get('ordering_verified') and validation.get('prefetch_consumed'):
            report['physical_validation'] = dict(device_model=validation['device_model'],
                continue_prefetch_and_consumption_verified=True,
                world_visible_reviewed=validation.get('manual_review', {}).get('world_visible', False),
                characters_validated=validation['characters_validated'], poco_f7_tested=False,
                fresh_password_login_separately_tested=False)
    (shared.DIST / (OUTPUT + '.build.json')).write_text(json.dumps(report, indent=2)+'\n', 'utf-8')
    print('PASS: account prefetch APK package verified', flush=True)

def main():
    if not (ROOT / 'durango-br-test.jks').is_file(): raise ValueError('Existing key required')
    baseline.SOURCE, baseline.SOURCE_HASH = SOURCE, SOURCE_HASH
    baseline.WORK, baseline.CLIENT, baseline.DECODED = WORK, CLIENT, DECODED
    baseline.prepare()
    compile_helpers(); integrate(); compile_native()
    build = json.loads((CLIENT / STAMP).read_text('utf-8'))
    build.update(version_code=VERSION_CODE, version_name=VERSION_NAME,
                 account_prefetch=dict(before_unity=True, characters_validated=True, cache='memory-only; one use; 120 seconds'),
                 liblhaccount_sha256=baseline.sha256(CLIENT / NATIVE))
    (CLIENT / STAMP).write_text(json.dumps(build, indent=2)+'\n', 'utf-8')
    shared.APK, shared.OUTPUT_NAME = CLIENT, OUTPUT
    shared.PACKAGE_EXCLUDES = set(); shared.package(); verify()

if __name__ == '__main__':
    import sys
    if '--verify-only' in sys.argv: verify()
    else: main()
