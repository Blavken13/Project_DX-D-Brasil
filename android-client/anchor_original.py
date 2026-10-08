"""Add the existing WarpToPort ButtonInfo to hunting/safehouse mobile rows.

No new UI or managed metadata. Keep all existing shortcuts, original actions,
validity delegates and icons. Tutorial and offline branches remain untouched.
Only this verified original ARM64 game library is accepted.
"""
import hashlib
import struct
from diagnostic_abi_original import program_headers, branch, word

SOURCE_HASH = '4e49e91b49f2e66556e7c26703225c7aedb37f19611be69e9febf4da8ac98fa9'
RISKY_ALLOC, SAFE_ALLOC = 0x13d0a30, 0x13d0b68
RISKY_HOOK, SAFE_HOOK = 0x13d0b50, 0x13d0bac
URBAN_HOOK = 0x13d0a10
CONTINUE, NULL_LIST = 0x13d0bb4, 0x13d0bb0


def conditional(source, target, base):
    delta = target - source
    assert delta % 4 == 0 and -(1 << 20) <= delta < (1 << 20)
    return word(base | ((delta // 4) & 0x7ffff) << 5)


def pair(load, rn, offset):
    assert offset % 16 == 0 and -64 <= offset // 16 < 64
    return word((0xad400000 if load else 0xad000000) |
                ((offset // 16) & 127) << 15 | 3 << 10 | rn << 5 | 2)


def patch(raw):
    assert hashlib.sha256(raw).hexdigest() == SOURCE_HASH, 'Unknown game library'
    ph, rx = next((p, h) for p, h in program_headers(raw) if h[0] == 1 and h[1] == 5)
    assert rx[2] == rx[3] == 0 and rx[5] == rx[6] == 0x4df0360
    insertion = rx[5]
    changes = []
    for address, expected, replacement in (
        (RISKY_ALLOC, 0x321e03e1, word(0x528000a1)),  # allocate five structs
        (SAFE_ALLOC, 0x320003e1, word(0x52800041)),   # allocate two structs
    ):
        assert struct.unpack_from('<I', raw, address)[0] == expected
        changes.append((address, replacement))
    code = bytearray()
    shims = {}
    for name, hook, count in (('risky_or_outpost', RISKY_HOOK, 4), ('safehouse', SAFE_HOOK, 1),
                              ('urban_personal_rural', URBAN_HOOK, 0)):
        assert raw[hook:hook + 4] == conditional(hook, CONTINUE, 0xb5000015)
        shims[name] = insertion + len(code)
        if count == 4:
            code += word(0xb9401a88)  # ldr w8, [x20, #24] (array length)
            code += word(0x7100151f)  # cmp w8, #5
            skip = insertion + len(code)
            code += b'\0' * 4
        # Shift backwards to avoid overwriting original ButtonInfo values.
        for index in range((count if count else 1) - 1, -1, -1):
            code += pair(True, 20, 0x20 + index * 32)
            code += pair(False, 20, 0x40 + index * 32)
        code += pair(True, 29, -0xd0)  # original WarpToPort local
        code += pair(False, 20, 0x20)
        tail = insertion + len(code)
        if count == 4:
            code[skip - insertion:skip - insertion + 4] = conditional(skip, tail, 0x54000001)
        code += word(0xb4000055)  # cbz x21, local null-list branch (+8)
        code += branch(insertion + len(code), CONTINUE)
        code += branch(insertion + len(code), NULL_LIST)
        changes.append((hook, branch(hook, shims[name])))
    next_file = min(h[2] for _, h in program_headers(raw) if h[0] == 1 and h[2] > insertion)
    assert insertion + len(code) <= next_file
    assert raw[insertion:insertion + len(code)] == bytes(len(code)), 'Code gap is not empty'
    changes += [(insertion, bytes(code)), (ph + 32, struct.pack('<QQ', rx[5] + len(code), rx[6] + len(code)))]
    out = bytearray(raw)
    for offset, data in changes:
        out[offset:offset + len(data)] = data
    assert len(out) == len(raw)
    report = dict(source_sha256=SOURCE_HASH, output_sha256=hashlib.sha256(out).hexdigest(),
                  shims=shims, changes=[dict(offset=p, length=len(b)) for p, b in changes],
                  button='WarpToPort', icon='icon_map_port',
                  tutorial_and_offline_preserved=True, existing_shortcuts_preserved=True)
    return bytes(out), report
