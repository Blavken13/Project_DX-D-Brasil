"""Build a reversible POCO F7 diagnostic profile from the verified 50221 APK.

This is an experiment, not a confirmed repair: physical ADB validation is required.
Keep Unity, IL2CPP, the repaired diagnostic hooks, assets and original DEX intact.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

import build_apk as shared
from build_diagnostic_compat_apk import signature_file

ROOT = shared.ROOT
SOURCE = ROOT / 'dist/LostHorizon-alfa-ancora-50221.apk'
SOURCE_HASH = 'b907863d1a9b66226d9dd3542fa68afb533950a792da04d5056b3742762a7bb9'
WORK = ROOT / 'work/poco-f7-50222'
CLIENT = WORK / 'client'
DECODED = WORK / 'decoded'
VERSION_CODE = 50222
VERSION_NAME = '5.2.1-losthorizon-poco-f7-teste1'
OUTPUT = 'LostHorizon-poco-f7-teste-50222.apk'
STAMP = 'assets/durango-br/diagnostics/build.json'
ALLOWED = {'AndroidManifest.xml', 'classes2.dex', STAMP}
PROFILE = {
    'experimental': True,
    'target_device': 'POCO F7 (reported by user; device properties pending)',
    'compatibility_default': True,
    'disable_title_video_default': True,
    'options_remain_user_adjustable': True,
    'preferences': 'lh-poco-f7-test',
    'adb_run_as_enabled': True,
    'physical_validation': 'pending ADB pairing; no crash cause established',
    'title_video_note': 'Selection video is initially disabled; selection may have a black background. Re-enable in Diagnostics for A/B comparison.',
}


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def method(text, signature, body):
    pattern = r'(^\.method[^\n]* ' + re.escape(signature) + r'\n)(.*?)(^\.end method)'
    changed, count = re.subn(pattern, lambda m: m[1] + body + '\n' + m[3], text,
                             flags=re.M | re.S)
    if count != 1:
        raise ValueError(f'Expected one method: {signature}; found {count}')
    return changed


def prepare():
    if sha256(SOURCE) != SOURCE_HASH:
        raise ValueError('50221 baseline checksum mismatch')
    # Fresh outputs prevent stale files from entering the next package.
    if WORK.exists():
        if not WORK.resolve().is_relative_to((ROOT / 'work').resolve()):
            raise ValueError('Build work must stay inside android-client/work')
        shutil.rmtree(WORK)
    CLIENT.mkdir(parents=True)
    with zipfile.ZipFile(SOURCE) as archive:
        for entry in archive.infolist():
            relative = Path(entry.filename)
            if relative.is_absolute() or '..' in relative.parts:
                raise ValueError('Unsafe baseline archive path')
            if entry.is_dir() or signature_file(entry.filename):
                continue
            target = CLIENT / relative
            if not target.resolve().is_relative_to(CLIENT.resolve()):
                raise ValueError('Archive path escapes client')
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as incoming, target.open('wb') as outgoing:
                shutil.copyfileobj(incoming, outgoing)
    mini = WORK / 'launcher-source.apk'
    with zipfile.ZipFile(mini, 'w') as archive:
        for path in CLIENT.rglob('*'):
            if path.is_file():
                name = path.relative_to(CLIENT).as_posix()
                if name in ('AndroidManifest.xml', 'resources.arsc', 'classes.dex', 'classes2.dex') or name.startswith('res/'):
                    archive.write(path, name)
    framework = WORK / 'framework'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar',
               'if', shared.SDK / 'platforms/android-36/android.jar', '-p', framework)
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar',
               'd', '-f', '-p', framework, '-o', DECODED, mini)


def adapt():
    diagnostic = DECODED / 'smali_classes2/com/newdawn/launcher/CrashDiagnostics.smali'
    before = diagnostic.read_text('utf-8')
    text = before
    # Separate only the two reversible profile controls from older saved choices.
    # Both getters and setters use the same namespace; the original preferences
    # remain available if the user returns to a standard build.
    for signature, key in (
            ('compatible(Landroid/content/Context;)Z', 'enabled'),
            ('disableTitleVideo(Landroid/content/Context;)Z', 'disable-title-video')):
        text = method(text, signature, f'''    .locals 3
    const-string v0, "lh-poco-f7-test"
    const/4 v1, 0x0
    invoke-virtual {{p0, v0, v1}}, Landroid/content/Context;->getSharedPreferences(Ljava/lang/String;I)Landroid/content/SharedPreferences;
    move-result-object v0
    const-string v1, "{key}"
    const/4 v2, 0x1
    invoke-interface {{v0, v1, v2}}, Landroid/content/SharedPreferences;->getBoolean(Ljava/lang/String;Z)Z
    move-result v0
    return v0''')
    for signature in ('compatible(Landroid/content/Context;Z)V',
                      'disableTitleVideo(Landroid/content/Context;Z)V'):
        pattern = r'(^\.method[^\n]* ' + re.escape(signature) + r'\n)(.*?)(^\.end method)'
        def change(match):
            body = match[2]
            if body.count('"lh-compatibility"') != 1:
                raise ValueError('Unexpected profile setter')
            return match[1] + body.replace('"lh-compatibility"', '"lh-poco-f7-test"') + match[3]
        text, count = re.subn(pattern, change, text, flags=re.M | re.S)
        if count != 1:
            raise ValueError('Missing profile setter')
    diagnostic.write_text(text, 'utf-8')
    (WORK / 'profile-before.smali').write_text(before, 'utf-8')
    (WORK / 'profile-after.smali').write_text(text, 'utf-8')
    ns = 'http://schemas.android.com/apk/res/android'
    ET.register_namespace('android', ns)
    tree = ET.parse(DECODED / 'AndroidManifest.xml')
    app = tree.getroot().find('application')
    app.set('{' + ns + '}label', 'Lost Horizon POCO F7 Teste')
    app.set('{' + ns + '}debuggable', 'true')
    app.set('{' + ns + '}pageSizeCompat', 'enabled')
    tree.write(DECODED / 'AndroidManifest.xml', encoding='utf-8', xml_declaration=True)
    config = DECODED / 'apktool.yml'
    text = config.read_text('utf-8')
    text, codes = re.subn(r'(versionCode:)[^\n]+', rf'\1 {VERSION_CODE}', text)
    text, names = re.subn(r'(versionName:)[^\n]+', rf'\1 {VERSION_NAME}', text)
    if codes != 1 or names != 1:
        raise ValueError('Unexpected version configuration')
    config.write_text(text, 'utf-8')
    rebuilt = WORK / 'launcher-rebuilt.apk'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar',
               'b', '-p', WORK / 'framework', DECODED, '-o', rebuilt)
    with zipfile.ZipFile(rebuilt) as archive:
        for name in ('AndroidManifest.xml', 'classes2.dex'):
            (CLIENT / name).write_bytes(archive.read(name))
    build = json.loads((CLIENT / STAMP).read_text('utf-8'))
    build.update(version_code=VERSION_CODE, version_name=VERSION_NAME,
                 poco_f7_profile=PROFILE, profile_baseline_sha256=SOURCE_HASH)
    build.setdefault('test_limitations', []).append('POCO F7 crash not reproduced: ADB session requires authorization/pairing.')
    (CLIENT / STAMP).write_text(json.dumps(build, indent=2) + '\n', 'utf-8')


def verify(output):
    with zipfile.ZipFile(SOURCE) as source, zipfile.ZipFile(output) as target:
        if target.testzip() is not None:
            raise ValueError('Invalid output ZIP')
        names = {n for n in source.namelist() if not n.endswith('/') and not signature_file(n)}
        if names != {n for n in target.namelist() if not n.endswith('/') and not signature_file(n)}:
            raise ValueError('Baseline file list changed')
        changed = {n for n in names if source.read(n) != target.read(n)}
        if changed != ALLOWED:
            raise ValueError(f'Unexpected changes: {changed}')
        for name in names:
            if name.startswith('assets/') or name.endswith('.so'):
                if target.getinfo(name).compress_type != zipfile.ZIP_STORED:
                    raise ValueError(f'Asset/native file compressed: {name}')
        report = dict(version_code=VERSION_CODE, version_name=VERSION_NAME,
                      baseline_sha256=SOURCE_HASH, apk_sha256=sha256(output),
                      changed_files=sorted(changed), preserved_files=len(names) - len(changed),
                      all_native_libraries_assets_and_original_dex_preserved=True,
                      diagnostic_abi_repair_and_mobile_anchor_preserved=True,
                      native_zip_alignment=16384, signatures_verified=True,
                      same_existing_signing_key=True, profile=PROFILE)
    # Read the assembled DEX back, rather than trusting only edited source.
    qa_apk = WORK / 'qa-launcher.apk'
    with zipfile.ZipFile(output) as target, zipfile.ZipFile(qa_apk, 'w') as qa:
        for name in ('AndroidManifest.xml', 'resources.arsc'):
            qa.writestr(name, target.read(name))
        qa.writestr('classes.dex', target.read('classes2.dex'))
    qa_decoded = WORK / 'qa-decoded'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', ROOT / 'work/apktool.jar',
               'd', '-r', '-f', '-p', WORK / 'framework', '-o', qa_decoded, qa_apk)
    baseline = DECODED / 'smali_classes2'
    assembled = qa_decoded / 'smali'
    names = {p.relative_to(baseline) for p in baseline.rglob('*.smali')}
    if names != {p.relative_to(assembled) for p in assembled.rglob('*.smali')}:
        raise ValueError('Launcher class list changed')
    modified = Path('com/newdawn/launcher/CrashDiagnostics.smali')
    for name in names - {modified}:
        if (baseline / name).read_bytes() != (assembled / name).read_bytes():
            raise ValueError(f'Unrelated launcher class changed: {name}')
    before = (WORK / 'profile-before.smali').read_text('utf-8')
    after = (assembled / modified).read_text('utf-8')
    for signature in ('compatible(Landroid/content/Context;)Z',
                      'compatible(Landroid/content/Context;Z)V',
                      'disableTitleVideo(Landroid/content/Context;)Z',
                      'disableTitleVideo(Landroid/content/Context;Z)V'):
        pattern = r'(^\.method[^\n]* ' + re.escape(signature) + r'\n)(.*?)(^\.end method)'
        found = re.search(pattern, after, flags=re.M | re.S)
        if found is None or '"lh-poco-f7-test"' not in found[2]:
            raise ValueError(f'Assembled profile method invalid: {signature}')
        if signature.endswith(';)Z') and 'const/4 v2, 0x1' not in found[2]:
            raise ValueError('Assembled default is not enabled')
        before = method(before, signature, '    # profile method')
        after = method(after, signature, '    # profile method')
    if before != after:
        raise ValueError('Diagnostics code changed outside four profile methods')
    badging = subprocess.check_output([str(shared.TOOLS / 'aapt.exe'), 'dump', 'badging', str(output)], text=True)
    if f"versionCode='{VERSION_CODE}'" not in badging or f"versionName='{VERSION_NAME}'" not in badging or 'application-debuggable' not in badging:
        raise ValueError('Assembled manifest version/debug settings invalid')
    certs = []
    for apk in (SOURCE, output):
        result = subprocess.check_output([str(shared.JAVA_BIN / 'java.exe'), '-jar',
            str(ROOT / 'work/apksigner.jar'), 'verify', '--print-certs', str(apk)], text=True)
        certs.append(re.findall(r'Signer #\d+ certificate SHA-256 digest: (\w+)', result))
    if not certs[0] or certs[0] != certs[1]:
        raise ValueError('Signing certificate differs; refuse to claim update compatibility')
    report.update(assembled_launcher_classes_verified=len(names),
                  diagnostics_changed_only_in_four_reversible_profile_methods=True,
                  encoded_manifest_version_and_debuggable_verified=True,
                  certificate_matches_50221=True)
    (ROOT / 'dist' / (OUTPUT + '.build.json')).write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print(f'PASS: {report["preserved_files"]} baseline files unchanged; POCO F7 profile packaged; physical test pending.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify-only', action='store_true', help='Verify existing output without rebuilding')
    args = parser.parse_args()
    if args.verify_only:
        verify(shared.DIST / OUTPUT)
        return
    if not (ROOT / 'durango-br-test.jks').is_file() or not (ROOT / 'durango-br-test.pass').is_file():
        raise ValueError('Existing 50221 signing key required; do not generate a replacement')
    prepare()
    adapt()
    shared.APK = CLIENT
    shared.OUTPUT_NAME = OUTPUT
    shared.PACKAGE_EXCLUDES = set()
    shared.DIST.mkdir(parents=True, exist_ok=True)
    shared.package()
    verify(shared.DIST / OUTPUT)


if __name__ == '__main__':
    main()
