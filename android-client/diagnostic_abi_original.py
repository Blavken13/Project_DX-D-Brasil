"""Repair static IL2CPP hook calls in the supplied 50219 diagnostics library.

Unity 2017 passes an unused x0 to managed static methods, followed by the
arguments and MethodInfo. 50219 observers omitted it. Keep their bodies and
unwind frames intact; add frameless register adapters on both sides of each
observer. Instance methods, game libraries and diagnostics filtering stay intact.
"""
import hashlib
import struct

BASE_SHA256 = '59fde87b96174467e6e9c923ada6d30e436870e356aa154d71697603c7ae7032'
PAGE_ALIGNMENT = 16384
# name, entry, number of integer/pointer arguments including MethodInfo,
# original-call instruction, branch register, original-pointer variable.
# TCP's double time travels independently in d0 and is never modified.
HOOKS = (
    ('HTTP_RESULT', 0x11204, 3, 0x1130c, 8, 0x555e8),
    ('EXCEPTION', 0x11784, 3, 0x11850, 3, 0x555f8),
    ('UNITY_LOG', 0x11854, 5, 0x11a2c, 8, 0x55600),
    ('TCP_HEADER', 0x11b6c, 7, 0x11c54, 8, 0x55610),
    ('RESOURCE_LOAD', 0x11e4c, 3, 0x11f84, 8, 0x55628),
    ('RESOURCE_ALL', 0x11fe0, 3, 0x12110, 8, 0x55630),
    ('SCENE_LOAD', 0x12c68, 5, 0x12db4, 8, 0x55670),
    ('SCENE_LOADED', 0x12e14, 3, 0x12e9c, 3, 0x55678),
    ('LOW_MEMORY', 0x12ea0, 1, 0x12f10, 1, 0x55680),
    ('LOADER_STATE', 0x137a0, 2, 0x13820, 2, 0x556b8),
    ('LOADER_ERROR', 0x13824, 2, 0x13958, 8, 0x556c0),
)


def program_headers(raw):
    assert raw[:6] == b'\x7fELF\x02\x01', 'Expected little-endian ELF64'
    offset = struct.unpack_from('<Q', raw, 32)[0]
    size, count = struct.unpack_from('<HH', raw, 54)
    assert size == 56
    return [(offset + i * size, struct.unpack_from('<IIQQQQQQ', raw, offset + i * size))
            for i in range(count)]


def file_offset(raw, address):
    for _, h in program_headers(raw):
        if h[0] == 1 and h[3] <= address < h[3] + h[5]:
            return h[2] + address - h[3]
    raise ValueError(f'Unmapped file address {address:#x}')


def word(value):
    return struct.pack('<I', value)


def mov(dest, source):
    return word(0xaa0003e0 | source << 16 | dest)


def branch(source, target, link=False):
    distance = target - source
    assert distance % 4 == 0 and -(1 << 27) <= distance < (1 << 27)
    return word((0x94000000 if link else 0x14000000) | ((distance // 4) & 0x3ffffff))


def patch(raw):
    assert hashlib.sha256(raw).hexdigest() == BASE_SHA256, 'Unknown diagnostics library; refuse to patch'
    headers = program_headers(raw)
    exec_header, rx = next((p, h) for p, h in headers if h[0] == 1 and h[1] == 5)
    insertion = rx[2] + rx[5]
    code_start = rx[3] + rx[5]
    assert insertion == 0x14810 and code_start == 0x18810
    shims = bytearray()
    changes, report = [], []
    for name, entry, count, call, register, original_pointer in HOOKS:
        entry_offset = file_offset(raw, entry)
        original_first = raw[entry_offset:entry_offset + 4]
        first_word = struct.unpack('<I', original_first)[0]
        assert first_word == 0xd10503ff or first_word == 0xd106c3ff or first_word & 0xffc003ff == 0xa98003fd, \
            f'Unexpected prologue: {name} {first_word:#x}'
        incoming = code_start + len(shims)
        for reg in range(count):
            shims += mov(reg, reg + 1)
        # Replay only the displaced stack instruction. All remaining stack and
        # unwind instructions retain their original addresses and semantics.
        shims += original_first
        shims += branch(code_start + len(shims), entry + 4)
        outgoing = code_start + len(shims)
        shims += mov(16, register)  # preserve target before shifting its register
        for reg in range(count, 0, -1):
            shims += mov(reg, reg - 1)
        shims += mov(0, 31)  # x0 is unused by the managed static function
        shims += word(0xd61f0200)  # br x16; retain the observer's original LR
        call_offset = file_offset(raw, call)
        opcode = struct.unpack_from('<I', raw, call_offset)[0]
        link = opcode == 0xd63f0000 | register << 5
        assert link or opcode == 0xd61f0000 | register << 5, f'Unexpected branch: {name}'
        changes += [(entry_offset, branch(entry, incoming)),
                    (call_offset, branch(call, outgoing, link))]
        report.append(dict(name=name, entry=entry, incoming=incoming, outgoing=outgoing,
                           call=call, target_register=register, argument_registers=count,
                           original_pointer=original_pointer, original_first=original_first.hex()))
    assert len(shims) < PAGE_ALIGNMENT
    next_segment = min(h[3] for _, h in headers if h[0] == 1 and h[3] > rx[3])
    assert code_start + len(shims) <= next_segment
    # Insert a whole 16 KiB file page. VA addresses of every existing symbol,
    # relocation and unwind entry remain unchanged. Preserve p_offset % p_align.
    result = bytearray(raw[:insertion] + bytes(PAGE_ALIGNMENT) + raw[insertion:])
    result[insertion:insertion + len(shims)] = shims
    for position, data in changes:
        result[position:position + len(data)] = data
    for position, header in headers:
        values = list(header)
        if values[2] >= insertion:
            values[2] += PAGE_ALIGNMENT
        if position == exec_header:
            values[5] += len(shims)
            values[6] += len(shims)
        struct.pack_into('<IIQQQQQQ', result, position, *values)
    section_offset = struct.unpack_from('<Q', raw, 40)[0]
    section_size, section_count = struct.unpack_from('<HH', raw, 58)
    assert section_size == 64 and section_offset >= insertion
    updated_sections = section_offset + PAGE_ALIGNMENT
    struct.pack_into('<Q', result, 40, updated_sections)
    for index in range(section_count):
        position = updated_sections + index * section_size
        offset = struct.unpack_from('<Q', result, position + 24)[0]
        if offset >= insertion:
            struct.pack_into('<Q', result, position + 24, offset + PAGE_ALIGNMENT)
    for _, header in program_headers(result):
        if header[0] == 1:
            assert header[2] % header[7] == header[3] % header[7]
    return bytes(result), dict(input_sha256=BASE_SHA256,
                              output_sha256=hashlib.sha256(result).hexdigest(),
                              shim_bytes=len(shims), inserted_bytes=PAGE_ALIGNMENT,
                              original_virtual_addresses_preserved=True,
                              original_unwind_frames_preserved=True,
                              floating_point_registers_preserved=True, hooks=report)
