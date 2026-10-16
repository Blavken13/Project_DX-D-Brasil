"""Build 50225 on the existing 50224 client with the scoped map guide repair."""
import hashlib
import json
from pathlib import Path
import shutil
import zipfile
import build_apk as shared
import build_diagnostic_compat_apk as compat
from tests.verify_guide_map import run as verify_map

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / 'dist/LostHorizon-conta-precarregada-50224.apk'
SOURCE_HASH = 'e616a8655041267944eb75d581c169855d2286a00f60da4be52e70380523a96a'
WORK = ROOT / 'work/gameplay-50225'
CLIENT = WORK / 'client'
OUTPUT = 'LostHorizon-alfa-gameplay-50225.apk'
GAME = 'lib/arm64-v8a/libil2cpp.so'
STAMP = 'assets/durango-br/diagnostics/build.json'


def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    assert digest(SOURCE) == SOURCE_HASH, '50224 baseline checksum mismatch'
    assert (ROOT / 'durango-br-test.jks').is_file() and (ROOT / 'durango-br-test.pass').is_file(), 'Existing signing key required'
    CLIENT.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(SOURCE) as archive:
        for entry in archive.infolist():
            relative = Path(entry.filename)
            assert not relative.is_absolute() and '..' not in relative.parts
            if entry.is_dir() or compat.signature_file(entry.filename): continue
            target = CLIENT / relative
            assert target.resolve().is_relative_to(CLIENT.resolve())
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as incoming, target.open('wb') as outgoing:
                shutil.copyfileobj(incoming, outgoing)
    raw = (CLIENT / GAME).read_bytes()
    report = verify_map(raw)
    from guide_map_original import patch
    fixed, _ = patch(raw)
    (CLIENT / GAME).write_bytes(fixed)
    build = json.loads((CLIENT / STAMP).read_text('utf-8'))
    build.update(version_code=50225, version_name='5.2.1-losthorizon-alfa-gameplay1',
                 baseline=SOURCE.name, baseline_sha256=SOURCE_HASH, mobile_map_guide=report)
    build.setdefault('test_limitations', []).append('Map click verified in ARM64 emulation; physical mobile tutorial validation pending')
    (CLIENT / STAMP).write_text(json.dumps(build, indent=2) + '\n', 'utf-8')
    compat.WORK, compat.CLIENT = WORK, CLIENT
    compat.VERSION_CODE, compat.VERSION_NAME = 50225, build['version_name']
    compat.manifest()
    shared.APK, shared.OUTPUT_NAME = CLIENT, OUTPUT
    shared.PACKAGE_EXCLUDES = set()
    shared.package()
    output = shared.DIST / OUTPUT
    with zipfile.ZipFile(SOURCE) as source, zipfile.ZipFile(output) as target:
        assert target.testzip() is None
        names = {n for n in source.namelist() if not n.endswith('/') and not compat.signature_file(n)}
        assert names == {n for n in target.namelist() if not n.endswith('/') and not compat.signature_file(n)}
        changed = {n for n in names if source.read(n) != target.read(n)}
        assert changed == {'AndroidManifest.xml', GAME, STAMP}, changed
        assert target.read(GAME) == fixed
        assert all(target.getinfo(n).compress_type == zipfile.ZIP_STORED
                   for n in names if n.startswith('assets/') or n.endswith('.so'))
    report.update(version_code=50225, baseline=SOURCE.name, baseline_sha256=SOURCE_HASH,
                  changed_files=sorted(changed), preserved_files=len(names) - len(changed),
                  apk_sha256=digest(output), signatures_verified=True,
                  physical_device_validation='pending')
    (shared.DIST / (OUTPUT + '.build.json')).write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print(f"PASS: APK 50225 signed and verified; {report['preserved_files']} files preserved; {report['emulation_cases']} ARM64 map cases passed.")


if __name__ == '__main__': main()
