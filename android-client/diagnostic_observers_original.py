"""Disable only the 28 optional hook-install calls for an explicit A/B build.

Keep diagnostic API resolution and direct authentication breadcrumbs working.
Never skip the whole installer: its dlsym setup is also used by direct HTTP
events from the mandatory authentication bridge. This does not repair a proven
crash cause; it isolates optional instrumentation from the original runtime.
"""
import hashlib
import struct
from diagnostic_abi_original import file_offset

BASE_HASH = 'c3164743cff60e9b82c09fd6a2c819543b288ad599059c27c6700369af22ecc3'
START, END = 0xfd60, 0x11204
INSTALL_PLT = 0x18590
CALLS = (0xfe74, 0xff38, 0xffe4, 0x10084, 0x10134, 0x101f0, 0x102b0,
         0x10360, 0x10420, 0x104dc, 0x1058c, 0x10634, 0x106d4, 0x1077c,
         0x10848, 0x108f0, 0x109a0, 0x10a64, 0x10b04, 0x10bb4, 0x10c64,
         0x10d1c, 0x10ddc, 0x10ea4, 0x10f4c, 0x10ffc, 0x110a0, 0x11148)
REPLACEMENT = struct.pack('<I', 0x2a1f03e0)  # mov w0, wzr: unavailable optional hook


def patch(raw):
    if hashlib.sha256(raw).hexdigest() != BASE_HASH:
        raise ValueError('Unknown repaired diagnostics library; refuse patch')
    found = []
    for address in range(START, END, 4):
        instruction = struct.unpack_from('<I', raw, file_offset(raw, address))[0]
        if instruction & 0xfc000000 != 0x94000000:
            continue
        displacement = instruction & 0x3ffffff
        if displacement & 0x2000000:
            displacement -= 0x4000000
        if address + displacement * 4 == INSTALL_PLT:
            found.append(address)
    if tuple(found) != CALLS:
        raise ValueError('Unexpected hook installation call sites')
    result = bytearray(raw)
    allowed = set()
    for address in CALLS:
        offset = file_offset(raw, address)
        result[offset:offset + 4] = REPLACEMENT
        allowed.update(range(offset, offset + 4))
    if len(result) != len(raw) or any(a != b and i not in allowed for i, (a, b) in enumerate(zip(raw, result))):
        raise ValueError('Unexpected native changes')
    return bytes(result), dict(
        input_sha256=BASE_HASH, output_sha256=hashlib.sha256(result).hexdigest(),
        replaced_instructions=len(CALLS), changed_instruction_bytes=4 * len(CALLS),
        optional_observers_installed=False, call_addresses=list(CALLS),
        api_resolution_and_direct_http_events_preserved=True,
        mandatory_authentication_tutorial_and_media_hooks_preserved=True,
        elf_layout_and_static_abi_repairs_preserved=True,
        experimental=True, physical_validation='pending', crash_cause='unconfirmed')
