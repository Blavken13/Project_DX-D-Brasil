"""Fix the estate hook installation rejected on the connected Unity 6 device."""
import hashlib
import json
import re
import shutil
import subprocess
import zipfile

import build_app_tester as b

SOURCE = b.ROOT / 'dist/losthorizon-build-tester-006.apk'
SOURCE_SHA256 = '87a8cd7f0770b133385a3ddd58272cb43cd94bcd8c62ed1439de5417adc497fb'
b.WORK = b.ROOT / 'work/app-tester-007'
b.MAIN = b.WORK / 'main'
b.BASE = SOURCE
b.OUTPUT = b.ROOT / 'dist/losthorizon-build-tester-007.apk'
NATIVE = 'lib/arm64-v8a/libtester.so'


def native_adapter():
    with zipfile.ZipFile(SOURCE) as apk:
        bridge = b.mod.elf(apk.read('lib/arm64-v8a/libdbr.so'))['symbols']
        previous = apk.read(NATIVE)
    (b.WORK / 'app_tester_offsets.h').write_text(
        f'#define DBR_BLOB {bridge["thpt_blob"]["address"]:#x}u\n'
        f'#define DBR_HOOK {bridge["hook_code"]["address"]:#x}u\n', 'ascii')
    flags = ['--target=aarch64-linux-android26', '-shared', '-fPIC', '-O2',
             '-Wall', '-Wextra', '-Werror', '-Wl,-z,max-page-size=16384',
             '-Wl,-z,common-page-size=16384', '-I', b.WORK,
             '-DTESTER_ESTATE_EXPANSION=1']
    baseline = b.WORK / 'libtester-006.so'
    b.run(b.NDK / 'clang.exe', *flags, b.ROOT / 'native/app_tester.c',
          '-o', baseline, '-ldl', '-llog')
    baseline_bytes = baseline.read_bytes()
    old_text = b.mod.elf(previous)['sections']['.text']
    baseline_text = b.mod.elf(baseline_bytes)['sections']['.text']
    assert previous[old_text['offset']:old_text['offset'] + old_text['bytes']] == \
        baseline_bytes[baseline_text['offset']:baseline_text['offset'] + baseline_text['bytes']], \
        'Source no longer reproduces tester-006 native adapter'
    patched = b.WORK / 'libtester.so'
    b.run(b.NDK / 'clang.exe', *flags, '-DTESTER_ESTATE_REPLACE=1',
          b.ROOT / 'native/app_tester.c', '-o', patched, '-ldl', '-llog')
    payload = patched.read_bytes()
    for marker in (b'estate_expansion_direct_entry_ready', b'estate_expansion_click tile=',
                   b'ExpandPersonalEstate', b'OnExpandEstateClick'):
        assert marker in payload, marker
    b.changes[NATIVE] = payload
    return dict(previous_sha256=hashlib.sha256(previous).hexdigest(),
                patched_sha256=hashlib.sha256(payload).hexdigest(),
                baseline_006_machine_code_identical=True,
                fix='Replace full click entry without relocating the rejected CBZ prologue',
                dynamic_page_size=True, point2_abi_checked_at_runtime=True)


def bump_version():
    if not b.MAIN.exists():
        shutil.copytree(b.ROOT / 'work/app-tester-006/main', b.MAIN)
    config = b.MAIN / 'apktool.yml'
    value = config.read_text('utf-8')
    value, code = re.subn(r'versionCode: [67]\b', 'versionCode: 7', value)
    value, name = re.subn(r'versionName: tester-00[67]\b', 'versionName: tester-007', value)
    assert (code, name) == (1, 1)
    config.write_text(value, 'utf-8')
    rebuilt = b.WORK / 'version-patched.apk'
    b.run(b.JAVA / 'java.exe', '-jar', b.TOOL, 'b', '-p', b.WORK / 'framework',
          '-o', rebuilt, b.MAIN)
    with zipfile.ZipFile(rebuilt) as apk:
        b.changes['AndroidManifest.xml'] = apk.read('AndroidManifest.xml')


def verify(native):
    with zipfile.ZipFile(SOURCE) as old, zipfile.ZipFile(b.OUTPUT) as new:
        old_names = {n for n in old.namelist() if not n.startswith('META-INF/')}
        new_names = {n for n in new.namelist() if not n.startswith('META-INF/')}
        assert old_names == new_names
        changed = {n for n in old_names if old.read(n) != new.read(n)}
        assert changed == {NATIVE, 'AndroidManifest.xml'}, changed
        assert new.testzip() is None
    badging = subprocess.check_output(
        [str(b.BT / 'aapt2.exe'), 'dump', 'badging', str(b.OUTPUT)],
        text=True, encoding='utf-8')
    assert "versionCode='7'" in badging and "versionName='tester-007'" in badging
    certs = []
    for apk in (SOURCE, b.OUTPUT):
        output = subprocess.check_output(
            [str(b.JAVA / 'java.exe'), '-jar', str(b.ROOT / 'work/apksigner.jar'),
             'verify', '--print-certs', str(apk)], text=True)
        certs.append(next(line for line in output.splitlines() if 'certificate SHA-256 digest:' in line))
    assert certs[0] == certs[1]
    report = dict(apk=b.OUTPUT.name, source_sha256=SOURCE_SHA256,
                  sha256=hashlib.sha256(b.OUTPUT.read_bytes()).hexdigest(),
                  bytes=b.OUTPUT.stat().st_size, same_signer=True,
                  version_code=7, version_name='tester-007', changed_files=sorted(changed),
                  unchanged_payloads_verified=len(old_names) - len(changed),
                  estate_expansion=native)
    (b.WORK / 'static-validation.json').write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print(json.dumps(report, indent=2), flush=True)


def main():
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == SOURCE_SHA256
    b.WORK.mkdir(exist_ok=True)
    native = native_adapter()
    bump_version()
    b.audit.update(version_code=7, version_name='tester-007', source_sha256=SOURCE_SHA256,
                   change='Install estate click replacement without trampoline relocation',
                   estate_expansion=native)
    b.package()
    verify(native)


if __name__ == '__main__':
    main()
