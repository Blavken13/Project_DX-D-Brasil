"""Execute the actual optional installer with and without its hook calls."""
import argparse
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT), str(ROOT / 'work/python-deps')]
from diagnostic_observers_original import patch, START, END, INSTALL_PLT
from diagnostic_abi_original import program_headers
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X19, UC_ARM64_REG_X29, UC_ARM64_REG_X30, UC_ARM64_REG_SP, UC_ARM64_REG_PC

BASE, STACK, RETURN, GAME = 0x100000000, 0x200000000, 0x300000000, 0x400000000


def execute(raw, enabled, coverage_file):
    uc = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
    uc.mem_map(BASE, 0x100000)
    for _, header in program_headers(raw):
        if header[0] == 1:
            uc.mem_write(BASE + header[3], raw[header[2]:header[2] + header[5]])
    uc.mem_map(STACK, 0x20000)
    uc.mem_map(RETURN, 0x1000)
    uc.mem_write(BASE + 0x211c0, (b'/private-test-dir\0' if enabled else b'\0'))
    uc.reg_write(UC_ARM64_REG_SP, STACK + 0x10000)
    uc.reg_write(UC_ARM64_REG_X0, 0x1234)
    uc.reg_write(UC_ARM64_REG_X1, GAME)
    uc.reg_write(UC_ARM64_REG_X30, RETURN)
    saved = {UC_ARM64_REG_X19 + i: 0xa0000000 + i for i in range(10)}
    saved[UC_ARM64_REG_X29] = 0xb0000000
    for register, value in saved.items():
        uc.reg_write(register, value)
    calls, symbols, events = [], [], []

    def string(address):
        data = bytearray()
        for i in range(160):
            char = uc.mem_read(address + i, 1)[0]
            if not char:
                return data.decode('ascii')
            data.append(char)
        raise ValueError('Unbounded test string')

    def instruction(machine, address, size, user):
        word = struct.unpack('<I', machine.mem_read(address, 4))[0]
        if word & 0xfc000000 != 0x94000000:
            return
        displacement = word & 0x3ffffff
        if displacement & 0x2000000:
            displacement -= 0x4000000
        target = address + displacement * 4 - BASE
        result = 0
        if target == INSTALL_PLT:
            calls.append(machine.reg_read(UC_ARM64_REG_X0))
            # Baseline fallback path: pretend prologue verification rejected hook.
        elif target == 0x18600:  # dlsym
            symbols.append(string(machine.reg_read(UC_ARM64_REG_X1)))
            result = 0x500000000 + len(symbols) * 0x100
        elif target == 0x18510:  # fopen
            result = 0x600000000 if coverage_file else 0
        elif target == 0xf0f8:  # emit, observe the real event arguments
            events.append(dict(event=string(machine.reg_read(UC_ARM64_REG_X1)),
                               key=string(machine.reg_read(UC_ARM64_REG_X0 + 4))))
        elif target not in (0x18500, 0x18540, 0x18550, 0x18720):
            raise ValueError(f'Unexpected external call: {target:#x}')
        machine.reg_write(UC_ARM64_REG_X0, result)
        machine.reg_write(UC_ARM64_REG_X30, address + 4)
        machine.reg_write(UC_ARM64_REG_PC, address + 4)

    uc.hook_add(UC_HOOK_CODE, instruction, begin=BASE + START, end=BASE + END)
    uc.emu_start(BASE + START, RETURN, count=10000)
    if uc.reg_read(UC_ARM64_REG_PC) != RETURN:
        raise ValueError('Installer failed to return')
    if uc.reg_read(UC_ARM64_REG_SP) != STACK + 0x10000:
        raise ValueError('Installer stack not restored')
    if any(uc.reg_read(register) != value for register, value in saved.items()):
        raise ValueError('Callee-saved registers changed')
    return dict(install_calls=len(calls), api_symbols=symbols, events=events,
                diagnostic_api_pointers=bytes(uc.mem_read(BASE + 0x555c0, 40)).hex(),
                original_hook_pointers=bytes(uc.mem_read(BASE + 0x555e8, 224)).hex(),
                stack_and_callee_saved_registers_preserved=True)


def verify(raw):
    fixed, report = patch(raw)
    cases = []
    for enabled in (False, True):
        for coverage in (False, True):
            before, after = execute(raw, enabled, coverage), execute(fixed, enabled, coverage)
            if before['install_calls'] != (28 if enabled else 0) or after['install_calls'] != 0:
                raise ValueError('Optional installer call count invalid')
            if {k: v for k, v in before.items() if k != 'install_calls'} != {k: v for k, v in after.items() if k != 'install_calls'}:
                raise ValueError('Changes exceed the optional unavailable-hook path')
            cases.append(dict(enabled=enabled, coverage_file_available=coverage,
                              baseline_install_calls=before['install_calls'], test_install_calls=0,
                              diagnostic_api_pointers_preserved=True, passed=True))
    report.update(emulation=cases,
                  limitation='External file/log APIs are stubbed; no physical Android crash reproduced')
    return fixed, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / 'work/anchor-50221/client/lib/arm64-v8a/libnd.so')
    parser.add_argument('--report', type=Path, default=ROOT / 'work/poco-f7-no-observers-tests.json')
    args = parser.parse_args()
    _, report = verify(args.source.read_bytes())
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    print('PASS: four ARM64 installer cases; no optional hook calls; API resolution, fallback events, stack and saved registers preserved.')


if __name__ == '__main__':
    main()
