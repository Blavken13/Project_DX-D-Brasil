"""50223 A/B test: keep the 50222 profile, disable optional native observers."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

import build_apk as shared
import build_diagnostic_compat_apk as compat
from tests.verify_diagnostic_observers import verify as verify_native

ROOT = shared.ROOT
SOURCE = ROOT / 'dist/LostHorizon-poco-f7-teste-50222.apk'
SOURCE_HASH = 'e919d34151281771b50b2617ad81075d5e15bcef3f276101aeadc7c69e2fdf3b'
WORK = ROOT / 'work/poco-f7-no-observers-50223'
CLIENT = WORK / 'client'
OUTPUT = 'LostHorizon-poco-f7-teste-sem-observadores-50223.apk'
VERSION_CODE = 50223
VERSION_NAME = '5.2.1-losthorizon-poco-f7-teste2'
NATIVE = 'lib/arm64-v8a/libnd.so'
STAMP = 'assets/durango-br/diagnostics/build.json'
ALLOWED = {'AndroidManifest.xml', NATIVE, STAMP}


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    if digest(SOURCE) != SOURCE_HASH:
        raise ValueError('50222 baseline checksum mismatch')
    if not (ROOT / 'durango-br-test.jks').is_file() or not (ROOT / 'durango-br-test.pass').is_file():
        raise ValueError('Existing signing key required')
    CLIENT.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(SOURCE) as archive:
        for entry in archive.infolist():
            relative = Path(entry.filename)
            if relative.is_absolute() or '..' in relative.parts:
                raise ValueError('Unsafe archive entry')
            if entry.is_dir() or compat.signature_file(entry.filename):
                continue
            target = CLIENT / relative
            if not target.resolve().is_relative_to(CLIENT.resolve()):
                raise ValueError('Archive path escapes client')
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as incoming, target.open('wb') as outgoing:
                shutil.copyfileobj(incoming, outgoing)
    fixed, native_report = verify_native((CLIENT / NATIVE).read_bytes())
    (CLIENT / NATIVE).write_bytes(fixed)
    build = json.loads((CLIENT / STAMP).read_text('utf-8'))
    build.update(version_code=VERSION_CODE, version_name=VERSION_NAME,
                 libnd_sha256=hashlib.sha256(fixed).hexdigest(),
                 libil2cpp_sha256=digest(CLIENT / 'lib/arm64-v8a/libil2cpp.so'),
                 diagnostic_observer_isolation=native_report,
                 profile_baseline_sha256=SOURCE_HASH)
    build['poco_f7_profile'].update(
        experimental=True, optional_native_observers_enabled=False,
        physical_validation='50222 SIGSEGV confirmed; 50223 A/B test pending')
    build.setdefault('test_limitations', []).append(
        'Optional native observers intentionally disabled; coverage may report prologue mismatch/unavailable. This is not proof of the original crash cause.')
    (CLIENT / STAMP).write_text(json.dumps(build, indent=2) + '\n', 'utf-8')
    compat.WORK, compat.CLIENT = WORK, CLIENT
    compat.VERSION_CODE, compat.VERSION_NAME = VERSION_CODE, VERSION_NAME
    compat.manifest()
    shared.APK, shared.OUTPUT_NAME = CLIENT, OUTPUT
    shared.PACKAGE_EXCLUDES = set()
    shared.package()
    output = shared.DIST / OUTPUT
    with zipfile.ZipFile(SOURCE) as source, zipfile.ZipFile(output) as target:
        if target.testzip() is not None:
            raise ValueError('Output ZIP invalid')
        names = {n for n in source.namelist() if not n.endswith('/') and not compat.signature_file(n)}
        if names != {n for n in target.namelist() if not n.endswith('/') and not compat.signature_file(n)}:
            raise ValueError('APK file list changed')
        changed = {n for n in names if source.read(n) != target.read(n)}
        if changed != ALLOWED or target.read(NATIVE) != fixed:
            raise ValueError(f'Unexpected APK changes: {changed}')
        if any(target.getinfo(n).compress_type != zipfile.ZIP_STORED for n in names if n.startswith('assets/') or n.endswith('.so')):
            raise ValueError('Stored assets or libraries became compressed')
    certificates = []
    for apk in (SOURCE, output):
        text = subprocess.check_output([str(shared.JAVA_BIN / 'java.exe'), '-jar',
            str(ROOT / 'work/apksigner.jar'), 'verify', '--print-certs', str(apk)], text=True)
        certificates.append(re.findall(r'Signer #\d+ certificate SHA-256 digest: (\w+)', text))
    if not certificates[0] or certificates[0] != certificates[1]:
        raise ValueError('Certificate differs from 50222')
    badging = subprocess.check_output([str(shared.TOOLS / 'aapt.exe'), 'dump', 'badging', str(output)], text=True)
    if f"versionCode='{VERSION_CODE}'" not in badging or f"versionName='{VERSION_NAME}'" not in badging:
        raise ValueError('Encoded version is incorrect')
    report = dict(version_code=VERSION_CODE, version_name=VERSION_NAME,
                  baseline_sha256=SOURCE_HASH, apk_sha256=digest(output),
                  changed_files=sorted(changed), preserved_files=len(names) - len(changed),
                  unity_il2cpp_assets_and_both_dex_files_preserved=True,
                  diagnostic_zip_export_and_profile_controls_preserved=True,
                  certificate_matches_50222=True, native_zip_alignment=16384,
                  experimental=True, crash_cause='unknown: original export had no native backtrace',
                  native_observer_isolation=native_report)
    (shared.DIST / (OUTPUT + '.build.json')).write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print(f'PASS: 50223 A/B APK verified; {report["preserved_files"]} baseline files unchanged; physical validation pending.', flush=True)


if __name__ == '__main__':
    main()
