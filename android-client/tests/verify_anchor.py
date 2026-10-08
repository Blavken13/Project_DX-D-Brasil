"""Execute the patched ARM64 instructions with real array/stack layouts."""
import json
from pathlib import Path
import struct
import sys
import zipfile
ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT), str(ROOT / 'work/python-deps')]
from anchor_original import patch, RISKY_HOOK, SAFE_HOOK, URBAN_HOOK, CONTINUE, NULL_LIST
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_X20, UC_ARM64_REG_X21, UC_ARM64_REG_X29, UC_ARM64_REG_SP


def run(raw):
    fixed, report = patch(raw)
    cases = []
    for name, hook, count, should_shift in (
        ('hunting', RISKY_HOOK, 5, True),
        ('outpost', RISKY_HOOK, 4, False),
        ('safehouse', SAFE_HOOK, 2, True),
        ('urban_personal_rural', URBAN_HOOK, 5, False),
    ):
        for valid_list in (True, False):
            u = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
            for address in (0x13d0000, 0x4df0000):
                u.mem_map(address, 0x10000)
                u.mem_write(address, fixed[address:address + 0x10000])
            u.mem_map(0x10000000, 0x10000)
            u.mem_map(0x20000000, 0x10000)
            array, fp, sp = 0x10000100, 0x20001000, 0x20000800
            original = [bytes([i + 1]) * 32 for i in range(count - (1 if should_shift else 0))]
            anchor = bytes([0xa9]) * 32
            if hook == URBAN_HOOK:
                original[1] = anchor
            u.mem_write(array + 24, struct.pack('<Q', count))
            u.mem_write(array + 32, b''.join(original) + bytes(32 if should_shift else 0))
            u.mem_write(array + 32 + count * 32, b'Z' * 32)
            u.mem_write(fp - 0xd0, anchor)
            for reg, value in ((UC_ARM64_REG_X20, array), (UC_ARM64_REG_X21, 1 if valid_list else 0),
                               (UC_ARM64_REG_X29, fp), (UC_ARM64_REG_SP, sp)):
                u.reg_write(reg, value)
            stopped = []
            def stop(emu, address, size, data):
                if address in (CONTINUE, NULL_LIST):
                    stopped.append(address)
                    emu.emu_stop()
            u.hook_add(UC_HOOK_CODE, stop)
            u.emu_start(hook, 0, count=100)
            expected = ([anchor] + original) if should_shift else original
            if hook == URBAN_HOOK:
                expected = [original[1], original[0]] + original[2:]
            assert bytes(u.mem_read(array + 32, count * 32)) == b''.join(expected), name
            assert bytes(u.mem_read(array + 32 + count * 32, 32)) == b'Z' * 32
            assert stopped == [CONTINUE if valid_list else NULL_LIST]
            assert u.reg_read(UC_ARM64_REG_SP) == sp and u.reg_read(UC_ARM64_REG_X29) == fp
            cases.append(dict(role=name, valid_list=valid_list, passed=True))
    permitted = bytearray(len(raw))
    for change in report['changes']:
        permitted[change['offset']:change['offset'] + change['length']] = b'\1' * change['length']
    assert all(a == b or permitted[i] for i, (a, b) in enumerate(zip(raw, fixed)))
    report['emulation'] = cases
    return report


if __name__ == '__main__':
    with zipfile.ZipFile(ROOT.parent / 'LostHorizon-alfa-diagnostico-completo-50219.apk') as archive:
        raw = archive.read('lib/arm64-v8a/libil2cpp.so')
    report = run(raw)
    (ROOT / 'work/anchor-tests.json').write_text(json.dumps(report, indent=2), 'utf-8')
    print('PASS: 8 ARM64 anchor cases; original branches and allocation bounds preserved.')
