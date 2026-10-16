"""Repair the minimap tutorial in the verified original ARM64 client.

The minimap uses MenuHelper.Open(WorldMap), and only WorldMap button ToDos
subscribe to MenuOpened. Other menu button ToDos retain MenuClicked.
Completion, hand dismissal, persistence and guide transitions remain native.
"""
import hashlib
import struct
from anchor_original import conditional
from diagnostic_abi_original import program_headers, branch, word

INPUT_HASHES = {
    '4e49e91b49f2e66556e7c26703225c7aedb37f19611be69e9febf4da8ac98fa9',
    '742be97c31b658be28c3259c312db4ce803da110a205c386c3990a5d132243ee',
}
MINIMAP = 0x1453614
MENU_OPEN = 0x1319374
WORLD_MAP = 26
ROUTES = (
    (0x17e172c, 0x17e1744, 0x131c410, 0x131c5e0),
    (0x17e1828, 0x17e1840, 0x131c4f8, 0x131c6c8),
)


def patch(raw):
    assert hashlib.sha256(raw).hexdigest() in INPUT_HASHES, 'Unverified ARM64 client'
    assert raw[MINIMAP:MINIMAP + 12].hex() == 'f44fbea9fd7b01a9fd430091'
    # Constructor stores _menu at +0x40, verified in the original metadata/code.
    assert raw[0x17e164c:0x17e1650] == word(0xb9004260)
    ph, rx = next((p, h) for p, h in program_headers(raw) if h[0] == 1 and h[1] == 5)
    assert rx[2] == rx[3] == 0
    insertion = rx[5]
    assert insertion % 4 == 0
    # Original static IL2CPP ABI: unused receiver, enum, bool, MethodInfo.
    minimap_code = b''.join(word(w) for w in (0xaa1f03e0, 0x52800341, 0x52800002, 0xaa1f03e3))
    minimap_code += branch(MINIMAP + 16, MENU_OPEN)
    changes = [(MINIMAP, minimap_code)]
    shims = bytearray()
    for capture, tail, clicked, opened in ROUTES:
        assert raw[capture:capture + 4] == word(0xaa1403e0)
        assert raw[tail:tail + 4] == branch(tail, clicked)
        address = insertion + len(shims)
        # x19 is the ToDo before its saved register is restored. Compare _menu,
        # then leave receiver/delegate and the original stack epilogue intact.
        shims += word(0xb9404268) + word(0x7100691f) + word(0xaa1403e0)
        shims += branch(address + 12, capture + 4)
        route = insertion + len(shims)
        shims += conditional(route, route + 8, 0x54000000)
        shims += branch(route + 4, clicked)
        shims += branch(route + 8, opened)
        changes.extend(((capture, branch(capture, address)), (tail, branch(tail, route))))
    next_file = min(h[2] for _, h in program_headers(raw) if h[0] == 1 and h[2] > insertion)
    assert insertion + len(shims) <= next_file
    assert raw[insertion:insertion + len(shims)] == bytes(len(shims))
    changes.extend(((insertion, shims), (ph + 32, struct.pack('<QQ', rx[5] + len(shims), rx[6] + len(shims)))))
    result = bytearray(raw)
    for address, value in changes: result[address:address + len(value)] = value
    fixed = bytes(result)
    report = dict(source_sha256=hashlib.sha256(raw).hexdigest(), output_sha256=hashlib.sha256(fixed).hexdigest(),
                  world_map_enum=WORLD_MAP, shim_address=insertion,
                  changes=[dict(offset=a, length=len(b)) for a, b in changes],
                  other_menu_todos_preserved=True, native_guide_completion_preserved=True)
    return fixed, report
