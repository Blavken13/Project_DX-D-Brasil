"""Execute actual ARM64 adapters and the TCP observer, without an Android device."""
import json
import argparse
from pathlib import Path
import struct
import sys
sys.path[:0] = [str(Path(__file__).resolve().parents[1]),
               str(Path(__file__).resolve().parents[1] / 'work/python-deps')]
from diagnostic_abi_original import patch, program_headers, file_offset
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X29, UC_ARM64_REG_X30, UC_ARM64_REG_SP, UC_ARM64_REG_D0, UC_ARM64_REG_PC

ROOT = Path(__file__).resolve().parents[1]
BASE, STACK, EXTERNAL = 0x100000000, 0x200000000, 0x300000000
REGISTER = [UC_ARM64_REG_X0 + i for i in range(29)] + [UC_ARM64_REG_X29, UC_ARM64_REG_X30]


def machine(raw):
    uc = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
    uc.mem_map(BASE, 0x200000)
    for _, h in program_headers(raw):
        if h[0] == 1 and h[5]:
            uc.mem_write(BASE + h[3], raw[h[2]:h[2] + h[5]])
    uc.mem_map(STACK, 0x20000)
    uc.mem_map(EXTERNAL, 0x10000)
    uc.reg_write(UC_ARM64_REG_SP, STACK + 0x10000)
    uc.reg_write(UC_ARM64_REG_X30, EXTERNAL + 0x100)
    return uc


def verify_adapters(original, fixed, report):
    checks = 0
    double_bits = struct.unpack('<Q', struct.pack('<d', 12345.6789))[0]
    for item in report['hooks']:
        count = item['argument_registers']
        arguments = [0x600000000 + 0x1111 * i for i in range(count)]
        uc = machine(fixed)
        for index, value in enumerate([0] + arguments):
            uc.reg_write(REGISTER[index], value)
        uc.reg_write(UC_ARM64_REG_D0, double_bits)
        uc.emu_start(BASE + item['entry'], BASE + item['entry'] + 4, count=100)
        assert [uc.reg_read(REGISTER[i]) for i in range(count)] == arguments, item['name']
        assert uc.reg_read(UC_ARM64_REG_D0) == double_bits
        assert uc.reg_read(UC_ARM64_REG_X30) == EXTERNAL + 0x100
        # Independently execute the unchanged displaced prologue for SP/FP/LR.
        reference = machine(original)
        reference.emu_start(BASE + item['entry'], BASE + item['entry'] + 4, count=1)
        assert uc.reg_read(UC_ARM64_REG_SP) == reference.reg_read(UC_ARM64_REG_SP)
        checks += 4
        uc = machine(fixed)
        for index, value in enumerate(arguments):
            uc.reg_write(REGISTER[index], value)
        uc.reg_write(REGISTER[item['target_register']], EXTERNAL)
        uc.reg_write(UC_ARM64_REG_D0, double_bits)
        uc.emu_start(BASE + item['outgoing'], EXTERNAL, count=100)
        assert [uc.reg_read(REGISTER[i]) for i in range(count + 1)] == [0] + arguments, item['name']
        assert uc.reg_read(UC_ARM64_REG_D0) == double_bits
        assert uc.reg_read(UC_ARM64_REG_SP) == STACK + 0x10000
        assert uc.reg_read(UC_ARM64_REG_X30) == EXTERNAL + 0x100
        checks += 4
    return checks


def tcp_observer(raw):
    uc = machine(raw)
    args = [0, 1, 0, 4000, 24, EXTERNAL + 0x2000, 12, EXTERNAL + 0x4000]
    for index, value in enumerate(args):
        uc.reg_write(REGISTER[index], value)
    bits = struct.unpack('<Q', struct.pack('<d', 17.5))[0]
    uc.reg_write(UC_ARM64_REG_D0, bits)
    uc.mem_write(BASE + 0x55610, struct.pack('<Q', EXTERNAL))
    calls, events = [], []

    def text(address):
        data = bytes(uc.mem_read(address, 512))
        return data.split(b'\0', 1)[0].decode('ascii')

    def callback(emu, address, size, _):
        if address == EXTERNAL:
            calls.append(([emu.reg_read(REGISTER[i]) for i in range(8)], emu.reg_read(UC_ARM64_REG_D0)))
            emu.reg_write(UC_ARM64_REG_X0, 24)
        elif address == BASE + 0x18500:  # snprintf, no host C library dependency
            detail = 'type=%d name=%s seq=%d reply=%d' % (emu.reg_read(REGISTER[3]),
                text(emu.reg_read(REGISTER[4])), emu.reg_read(REGISTER[5]), emu.reg_read(REGISTER[6]))
            emu.mem_write(emu.reg_read(REGISTER[0]), detail.encode() + b'\0')
            emu.reg_write(UC_ARM64_REG_X0, len(detail))
        elif address == BASE + 0x18460:  # atomic increment
            emu.reg_write(UC_ARM64_REG_X0, 1)
        elif address == BASE + 0x18650:  # emulated TLS slot
            emu.reg_write(UC_ARM64_REG_X0, EXTERNAL + 0x1000)
        elif address == BASE + 0xf0f8:  # bounded event emitter
            events.append((text(emu.reg_read(REGISTER[1])), text(emu.reg_read(REGISTER[4]))))
        else:
            return
        emu.reg_write(UC_ARM64_REG_PC, emu.reg_read(UC_ARM64_REG_X30))

    uc.hook_add(UC_HOOK_CODE, callback)
    uc.emu_start(BASE + 0x11b6c, EXTERNAL + 0x100, count=10000)
    assert len(calls) == 1
    return dict(expected_registers=args, actual_registers=calls[0][0],
                time_preserved=calls[0][1] == bits, events=events,
                return_value=uc.reg_read(UC_ARM64_REG_X0),
                stack_restored=uc.reg_read(UC_ARM64_REG_SP) == STACK + 0x10000)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / 'work/compat-50220/libnd.so')
    parser.add_argument('--report', type=Path, default=ROOT / 'work/compat-50220/abi-tests.json')
    args = parser.parse_args()
    source = args.source
    original = source.read_bytes()
    fixed, report = patch(original)
    checks = verify_adapters(original, fixed, report)
    before, after = tcp_observer(original), tcp_observer(fixed)
    assert before['actual_registers'] != before['expected_registers'], 'Must reproduce the baseline ABI defect'
    assert after['actual_registers'] == after['expected_registers'], after
    assert after['time_preserved'] and after['stack_restored'] and after['return_value'] == 24
    assert 'type=4000 name=GetClock seq=1 reply=0' in after['events'][0][1], after
    # All original mapped bytes survive except the 22 intentional branches.
    changed = {h['entry'] for h in report['hooks']} | {h['call'] for h in report['hooks']}
    for _, h in program_headers(original):
        if h[0] != 1:
            continue
        for offset in range(0, h[5], 4):
            address = h[3] + offset
            if address < 0x270 or address in changed:
                continue
            count = min(4, h[5] - offset)
            assert original[h[2] + offset:h[2] + offset + count] == fixed[file_offset(fixed, address):file_offset(fixed, address) + count]
    result = dict(passed=True, adapter_checks=checks, repaired_hooks=len(report['hooks']),
                  baseline_tcp=before, corrected_tcp=after,
                  original_mapped_bytes_preserved_except_elf_headers_and_22_branches=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: {checks} adapter checks; real TCP observer preserves all arguments, time, return and stack; baseline defect reproduced.')


if __name__ == '__main__':
    main()
