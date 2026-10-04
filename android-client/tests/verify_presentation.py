"""Verify the original APK's scoped Discord callbacks and local movie path."""
import io
import json
from pathlib import Path
import struct
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'android-client'))
sys.path.insert(0, str(ROOT / 'android-client/work/python-deps'))
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN
from elftools.elf.elffile import ELFFile
import presentation_original as presentation

SOURCE = ROOT / 'Durango original'
CLIENT = ROOT / 'android-client/work/original-nexon/client'
APK = ROOT / 'android-client/dist/LostHorizon-alfa.apk'


def method_addresses(selected=None):
    if selected is None:
        selected = {('Durango.System', 'Platform'): {'ShowNotice', 'get_PrologueMovieUrl'},
                    ('', '<ShowCluster>c__AnonStorey0'): {'<>m__0'}}
    data = (SOURCE / presentation.NATIVE).read_bytes()
    elf = ELFFile(io.BytesIO(data))
    segments = [s for s in elf.iter_segments() if s['p_type'] == 'PT_LOAD']
    relocations = {r['r_offset']: r['r_addend'] for r in
                   elf.get_section_by_name('.rela.dyn').iter_relocations() if r['r_info_type'] == 1027}

    def offset(address):
        for segment in segments:
            if segment['p_vaddr'] <= address < segment['p_vaddr'] + segment['p_filesz']:
                return segment['p_offset'] + address - segment['p_vaddr']
        raise ValueError(address)

    def pointer(address):
        return relocations.get(address, struct.unpack_from('<Q', data, offset(address))[0])

    metadata = (SOURCE / presentation.METADATA).read_bytes()
    header = struct.unpack_from('<68I', metadata)
    methods = [struct.unpack_from('<12I4H', metadata, header[12] + i * 56)
               for i in range(header[13] // 56)]
    count = max(m[6] for m in methods if m[6] != 0xffffffff) + 1
    candidates = []
    needle = struct.pack('<Q', count)
    start = 0
    while True:
        pos = data.find(needle, start)
        if pos < 0:
            break
        start = pos + 8
        if pos % 8:
            continue
        try:
            segment = next(s for s in segments if s['p_offset'] <= pos < s['p_offset'] + s['p_filesz'])
            address = segment['p_vaddr'] + pos - segment['p_offset']
            table = pointer(address + 8)
            if sum(0x100000 < pointer(table + i * 8) < 0x5000000 for i in range(20)) > 8:
                candidates.append(table)
        except (ValueError, StopIteration, struct.error):
            continue
    assert len(candidates) == 1, 'Original IL2CPP registration must be unambiguous'
    table = candidates[0]

    def text(index):
        start = header[6] + index
        return metadata[start:metadata.index(b'\0', start)].decode()

    targets = {}
    for i in range(header[41] // 104):
        typ = struct.unpack_from('<20I8H2I', metadata, header[40] + i * 104)
        name, namespace = text(typ[0]), text(typ[1])
        if (namespace, name) not in selected:
            continue
        for method in methods[typ[13]:typ[13] + typ[20]]:
            method_name = text(method[0])
            if method_name in selected[(namespace, name)]:
                targets[(name, method_name)] = pointer(table + method[6] * 8)
    return targets


def main():
    report = presentation.verify(SOURCE, CLIENT)
    targets = method_addresses()
    assert targets[('Platform', 'ShowNotice')] == presentation.NOTICE_HANDLER
    assert targets[('Platform', 'get_PrologueMovieUrl')] == 0x1951158
    assert targets[('<ShowCluster>c__AnonStorey0', '<>m__0')] == presentation.DOOR_CALLBACK
    disassembler = Cs(CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN)
    branch = list(disassembler.disasm(presentation.door_branch(), presentation.DOOR_CALLBACK))
    assert len(branch) == 1 and branch[0].mnemonic == 'b'
    assert int(branch[0].op_str[1:], 16) == presentation.NOTICE_HANDLER
    # Both notice and closure use the same void instance ABI. The notice handler
    # never dereferences its receiver before dispatching Application.OpenURL.
    native = (SOURCE / presentation.NATIVE).read_bytes()
    notice = list(disassembler.disasm(native[0x1951874:0x19518c4], 0x1951874))
    assert notice[-1].mnemonic == 'b' and int(notice[-1].op_str[1:], 16) == 0x3117ccc
    assert any(i.mnemonic == 'mov' and i.op_str == 'x0, xzr' for i in notice)

    with zipfile.ZipFile(APK) as archive:
        for path in (presentation.METADATA, presentation.NATIVE, presentation.MOVIE):
            assert archive.read(path.as_posix()) == (CLIENT / path).read_bytes()
        assert archive.getinfo(presentation.MOVIE.as_posix()).compress_type == zipfile.ZIP_STORED
        assert archive.read('classes.dex') == (SOURCE / 'classes.dex').read_bytes()
    # A damaged unrelated literal must be rejected, rather than accepted as an
    # intended branding edit. Test in memory, without modifying client files.
    original = (SOURCE / presentation.METADATA).read_bytes()
    damaged = bytearray(original)
    first = next(r for r in presentation.literal_records(original)
                 if r[3] == next(iter(presentation.REPLACEMENTS)).encode())
    damaged[first[1]] = ord('X')
    try:
        presentation.patch_metadata(bytes(damaged))
    except AssertionError:
        pass
    else:
        raise AssertionError('Unexpected source metadata must be rejected')
    print('PASS: two scoped Discord callbacks, exact IL2CPP methods/ARM64 branch, three literals,')
    print('      all unrelated metadata/native code preserved, original Java and stored transition video in signed APK.')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
