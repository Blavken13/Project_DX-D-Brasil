"""Build 50221 from the supplied 50219, retaining the verified 50220 ABI repair."""
import hashlib
import json
import zipfile
import build_apk as shared
import build_diagnostic_compat_apk as compat
from anchor_original import patch
from tests.verify_anchor import run as verify_anchor

OUTPUT = 'LostHorizon-alfa-ancora-50221.apk'
GAME = 'lib/arm64-v8a/libil2cpp.so'


def main():
    compat.WORK = compat.ROOT / 'work/anchor-50221'
    compat.CLIENT = compat.WORK / 'client'
    compat.VERSION_CODE = 50221
    compat.VERSION_NAME = '5.2.1-losthorizon-alfa-ancora1'
    compat.prepare()
    path = compat.CLIENT / GAME
    original = path.read_bytes()
    report = verify_anchor(original)
    fixed, _ = patch(original)
    path.write_bytes(fixed)
    stamp = compat.CLIENT / compat.DIAGNOSTIC_BUILD
    build = json.loads(stamp.read_text('utf-8'))
    build['mobile_anchor'] = report
    build['test_limitations'].append('Anchor rows verified in ARM64 emulation; physical Android UI remains to be tested')
    stamp.write_text(json.dumps(build, indent=2) + '\n', 'utf-8')
    compat.manifest()
    shared.APK = compat.CLIENT
    shared.OUTPUT_NAME = OUTPUT
    shared.PACKAGE_EXCLUDES = set()
    shared.DIST.mkdir(parents=True, exist_ok=True)
    shared.package()
    output = shared.DIST / OUTPUT
    with zipfile.ZipFile(compat.SOURCE) as source, zipfile.ZipFile(output) as target:
        assert target.testzip() is None
        names = {n for n in source.namelist() if not n.endswith('/') and not compat.signature_file(n)}
        assert names == {n for n in target.namelist() if not n.endswith('/') and not compat.signature_file(n)}
        changed = {n for n in names if source.read(n) != target.read(n)}
        assert changed == compat.ALLOWED | {GAME}, changed
        assert target.read(GAME) == fixed
        assert target.read(compat.NATIVE) == (compat.CLIENT / compat.NATIVE).read_bytes()
        assert all(target.getinfo(n).compress_type == zipfile.ZIP_STORED
                   for n in names if n.startswith('assets/') or n.endswith('.so'))
    with output.open('rb') as stream:
        apk_sha256 = hashlib.file_digest(stream, 'sha256').hexdigest()
    report.update(version_code=50221, baseline_sha256=compat.SOURCE_HASH,
                  changed_files=sorted(changed), preserved_files=len(names) - len(changed),
                  diagnostic_dex_and_export_preserved=True, native_abi_50220_preserved=True,
                  signatures_verified=True, native_zip_alignment=16384, signing_key='existing 50220 key',
                  apk_sha256=apk_sha256)
    (shared.DIST / (OUTPUT + '.build.json')).write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print(f'PASS: APK 50221 verified; {report["preserved_files"]} baseline files unchanged.')


if __name__ == '__main__':
    main()
