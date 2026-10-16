"""Route estate expansion through the native free-expansion request path.

The Brazilian server makes expansion and maintenance free. The legacy paid
dialog silently returns when maintenance is zero, before sending ExpandEstate.
Reuse the existing request/callback path (with its size guard and tile-to-cell
conversion); permissions, adjacency and the actual limit remain server-owned.
"""
from pathlib import Path
import struct

NATIVE = Path('lib/arm64-v8a/libil2cpp.so')
CLICK_METHOD = 0x139bf14
PAID_CALL = 0x139bf68
PAID_METHOD = 0x139c108
FREE_METHOD = 0x139bf80


def branch(target):
    delta = target - PAID_CALL
    assert delta % 4 == 0 and -(1 << 27) <= delta < (1 << 27)
    return struct.pack('<I', 0x94000000 | ((delta // 4) & 0x3ffffff))


def patch(data):
    assert data[PAID_CALL:PAID_CALL + 4] == branch(PAID_METHOD), 'Estate call differs from original ARM64 build'
    result = bytearray(data)
    result[PAID_CALL:PAID_CALL + 4] = branch(FREE_METHOD)
    return bytes(result)


def apply(client):
    path = client / NATIVE
    path.write_bytes(patch(path.read_bytes()))


def verify(source, client):
    original = (source / NATIVE).read_bytes()
    modified = (client / NATIVE).read_bytes()
    assert original[PAID_CALL:PAID_CALL + 4] == branch(PAID_METHOD)
    assert modified[PAID_CALL:PAID_CALL + 4] == branch(FREE_METHOD)
    assert modified[FREE_METHOD:PAID_METHOD] == original[FREE_METHOD:PAID_METHOD], 'Free request/callback path changed'
    assert modified[PAID_METHOD:PAID_METHOD + 0xac0] == original[PAID_METHOD:PAID_METHOD + 0xac0], 'Legacy dialog changed'
    return {'free_expansion': True, 'native_patch_bytes': 4,
            'click_method': hex(CLICK_METHOD), 'request_path': hex(FREE_METHOD),
            'personal_island_path_preserved': True, 'request_callback_preserved': True,
            'server_validates_permissions_adjacency_and_limit': True}
