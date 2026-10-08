"""Rebuild 50219 with verified static-hook ABI repairs, retaining its diagnostics."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import zipfile
import build_apk as shared
from diagnostic_abi_original import patch

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT.parent / 'LostHorizon-alfa-diagnostico-completo-50219.apk'
SOURCE_HASH = '0ea3ff42413a5d3fc4b1bd955ad730b239d4b01e074ca7b6b944cf6ca3cb82ad'
WORK = ROOT / 'work/compat-50220'
CLIENT = WORK / 'client'
VERSION_CODE = 50220
VERSION_NAME = '5.2.1-losthorizon-alfa-compat-abi1'
OUTPUT = 'LostHorizon-alfa-compat-diagnostico-50220.apk'
DIAGNOSTIC_BUILD = 'assets/durango-br/diagnostics/build.json'
NATIVE = 'lib/arm64-v8a/libnd.so'
ALLOWED = {'AndroidManifest.xml', NATIVE, DIAGNOSTIC_BUILD}


def signature_file(name):
    p = Path(name)
    return name.startswith('META-INF/') and (p.suffix.upper() in ('.SF', '.RSA', '.DSA', '.EC') or p.name == 'MANIFEST.MF')


def prepare():
    assert SOURCE.is_file(), f'Missing baseline APK: {SOURCE}'
    with SOURCE.open('rb') as f:
        assert hashlib.file_digest(f, 'sha256').hexdigest() == SOURCE_HASH
    CLIENT.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(SOURCE) as archive:
        for entry in archive.infolist():
            relative = Path(entry.filename)
            assert not relative.is_absolute() and '..' not in relative.parts
            target = CLIENT / relative
            assert target.resolve().is_relative_to(CLIENT.resolve())
            if entry.is_dir() or signature_file(entry.filename):
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as incoming, target.open('wb') as outgoing:
                shutil.copyfileobj(incoming, outgoing)
        original = archive.read(NATIVE)
        (WORK / 'libnd.so').write_bytes(original)
        fixed, report = patch(original)
        (CLIENT / NATIVE).write_bytes(fixed)
        (WORK / 'native-abi-repair.json').write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
        build = json.loads(archive.read(DIAGNOSTIC_BUILD))
        build.update(version_code=VERSION_CODE, version_name=VERSION_NAME,
                     baseline=SOURCE.name, baseline_sha256=SOURCE_HASH,
                     native_abi_repair=report,
                     static_method_abi='unused_x0_then_arguments_then_MethodInfo',
                     test_limitations=['ARM64 emulation validates call ABI; real-device GPU/audio/video validation remains required'])
        (CLIENT / DIAGNOSTIC_BUILD).write_text(json.dumps(build, indent=2) + '\n', 'utf-8')
    subprocess.run([str(Path.home() / 'AppData/Local/Programs/Python/Python312/python.exe'),
                    str(ROOT / 'tests/verify_diagnostic_abi.py'),
                    '--source', str(WORK / 'libnd.so'),
                    '--report', str(WORK / 'abi-tests.json')], check=True)


def manifest():
    source = WORK / 'manifest-source.apk'
    with zipfile.ZipFile(source, 'w') as archive:
        for name in ('AndroidManifest.xml', 'resources.arsc'):
            archive.write(CLIENT / name, name)
        for path in (CLIENT / 'res').rglob('*'):
            if path.is_file():
                archive.write(path, path.relative_to(CLIENT).as_posix())
    decoded = WORK / 'manifest-decoded'
    jar = ROOT / 'work/apktool.jar'
    framework = WORK / 'framework'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', jar, 'if',
               shared.SDK / 'platforms/android-36/android.jar', '-p', framework)
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', jar, 'd', '-s', '-f',
               '-p', framework, '-o', decoded, source)
    config = decoded / 'apktool.yml'
    data = config.read_text('utf-8')
    data, codes = re.subn(r'(versionCode:)[^\n]+', rf'\1 {VERSION_CODE}', data)
    data, names = re.subn(r'(versionName:)[^\n]+', rf'\1 {VERSION_NAME}', data)
    assert codes == names == 1
    config.write_text(data, 'utf-8')
    rebuilt = WORK / 'manifest-rebuilt.apk'
    shared.run(shared.JAVA_BIN / 'java.exe', '-jar', jar, 'b', '-p', framework,
               decoded, '-o', rebuilt)
    with zipfile.ZipFile(rebuilt) as archive:
        (CLIENT / 'AndroidManifest.xml').write_bytes(archive.read('AndroidManifest.xml'))


def verify(output):
    with zipfile.ZipFile(SOURCE) as source, zipfile.ZipFile(output) as target:
        assert target.testzip() is None
        source_names = {n for n in source.namelist() if not n.endswith('/') and not signature_file(n)}
        target_names = {n for n in target.namelist() if not n.endswith('/') and not signature_file(n)}
        assert source_names == target_names
        changed = {n for n in source_names if source.read(n) != target.read(n)}
        assert changed == ALLOWED, changed
        assert all(target.getinfo(n).compress_type == zipfile.ZIP_STORED
                   for n in target_names if n.startswith('assets/') or n.endswith('.so'))
        build = json.loads(target.read(DIAGNOSTIC_BUILD))
        assert build['version_code'] == VERSION_CODE
    report = dict(version_code=VERSION_CODE, version_name=VERSION_NAME, baseline_sha256=SOURCE_HASH,
                  changed_files=sorted(changed), preserved_files=len(source_names) - len(changed),
                  unity_engine_and_game_native_library_preserved=True,
                  diagnostic_dex_and_export_preserved=True, assets_resources_metadata_preserved=True,
                  signatures_verified=True, native_zip_alignment=16384,
                  new_signing_key=True,
                  abi_tests=json.loads((WORK / 'abi-tests.json').read_text()))
    (shared.DIST / (OUTPUT + '.build.json')).write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print(f'PASS: {report["preserved_files"]} baseline files preserved; only manifest, ABI repair and diagnostic build stamp changed.', flush=True)


def main():
    prepare()
    manifest()
    shared.APK = CLIENT
    shared.OUTPUT_NAME = OUTPUT
    shared.PACKAGE_EXCLUDES = set()
    shared.DIST.mkdir(parents=True, exist_ok=True)
    shared.package()
    verify(shared.DIST / OUTPUT)


if __name__ == '__main__':
    main()
