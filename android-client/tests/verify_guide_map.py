"""Exercise ARM64 minimap dispatch and both guide event subscriptions."""
from pathlib import Path
import struct
import sys
import zipfile
ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT), str(ROOT / 'work/python-deps')]
from guide_map_original import patch, MINIMAP, MENU_OPEN, ROUTES
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import *


def run(raw):
    fixed, report = patch(raw)
    cases = []
    def machine():
        emu = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
        for address in {MINIMAP & ~0xffff, 0x17e0000, 0x1310000, report['shim_address'] & ~0xffff}:
            emu.mem_map(address, 0x10000)
            emu.mem_write(address, fixed[address:address + 0x10000])
        emu.mem_map(0x10000000, 0x10000)
        return emu
    for capture, tail, clicked, opened in ROUTES:
        for menu in (0, 1, 2, 3, 26, 37):
            emu = machine()
            todo, stack, receiver, delegate = 0x10000100, 0x10001000, 0x10000300, 0x10000400
            emu.mem_write(todo + 0x40, struct.pack('<I', menu))
            emu.mem_write(stack, struct.pack('<6Q', 22, 21, 20, 19, 29, 30))
            for register, value in ((UC_ARM64_REG_X19, todo), (UC_ARM64_REG_X20, receiver),
                                    (UC_ARM64_REG_X21, delegate), (UC_ARM64_REG_SP, stack)):
                emu.reg_write(register, value)
            reached = []
            def stop(uc, address, size, data):
                if address in (clicked, opened): reached.append(address); uc.emu_stop()
            emu.hook_add(UC_HOOK_CODE, stop)
            emu.emu_start(capture, 0, count=40)
            assert reached == [opened if menu == 26 else clicked], (menu, reached)
            assert emu.reg_read(UC_ARM64_REG_SP) == stack + 48
            assert emu.reg_read(UC_ARM64_REG_X0) == receiver
            assert emu.reg_read(UC_ARM64_REG_X1) == delegate
            assert emu.reg_read(UC_ARM64_REG_X2) == 0
            assert emu.reg_read(UC_ARM64_REG_X19) == 19 and emu.reg_read(UC_ARM64_REG_X30) == 30
            cases.append(dict(menu=menu, subscription='add' if capture == ROUTES[0][0] else 'remove'))
    emu = machine(); emu.reg_write(UC_ARM64_REG_X0, 0x10000100)
    emu.reg_write(UC_ARM64_REG_X30, 0x12345678); emu.reg_write(UC_ARM64_REG_SP, 0x10001000)
    reached = []
    def stop_map(uc, address, size, data):
        if address == MENU_OPEN: reached.append(address); uc.emu_stop()
    emu.hook_add(UC_HOOK_CODE, stop_map)
    emu.emu_start(MINIMAP, 0, count=10)
    assert reached == [MENU_OPEN]
    assert [emu.reg_read(r) for r in (UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3)] == [0, 26, 0, 0]
    assert emu.reg_read(UC_ARM64_REG_X30) == 0x12345678 and emu.reg_read(UC_ARM64_REG_SP) == 0x10001000
    changes = sorted((c['offset'], c['offset'] + c['length']) for c in report['changes'])
    last = 0
    for start, end in changes:
        assert raw[last:start] == fixed[last:start]
        last = end
    assert raw[last:] == fixed[last:]
    report['emulation_cases'] = len(cases) + 1
    return report


if __name__ == '__main__':
    with zipfile.ZipFile(ROOT / 'dist/LostHorizon-alfa-diagnostico-completo-50219.apk') as source:
        raw = source.read('lib/arm64-v8a/libil2cpp.so')
    from anchor_original import patch as anchor_patch
    run(raw)
    report = run(anchor_patch(raw)[0])
    print(f"PASS: {report['emulation_cases'] * 2} ARM64 cases on original and anchor client; map event, ABI, unsubscribe and other menus verified.")
