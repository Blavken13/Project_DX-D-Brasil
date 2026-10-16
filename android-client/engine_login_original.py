"""Inspect original IL2CPP APIs used by the in-engine login integration."""
import sys
import struct
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'work/python-deps'))


def elf_symbols(raw):
    shoff = struct.unpack_from('<Q', raw, 40)[0]
    shsize, shnum = struct.unpack_from('<HH', raw, 58)
    sections = [struct.unpack_from('<IIQQQQIIQQ', raw, shoff + i * shsize) for i in range(shnum)]
    result = {}
    for section in sections:
        if section[1] not in (2, 11): continue
        strings = sections[section[6]]
        names = raw[strings[4]:strings[4] + strings[5]]
        for pos in range(section[4], section[4] + section[5], section[9]):
            name, info, other, index, value, size = struct.unpack_from('<IBBHQQ', raw, pos)
            if name and value:
                result[names[name:names.index(b'\0', name)].decode()] = (value, size)
    return result


def offsets_header(nd, il2cpp, output):
    selected = {('', 'NGUIDebug'): {'OnGUI'}, ('Durango.UI', 'TitleMenuGroup'): {'StartGame'},
                ('Durango.UI', 'TitleMenuUserControlBase'): {'OnClusterAccountUpdated', 'OnConfirm'},
                ('Durango.Logic.Clusters', 'Clusters'): {'RequestAccounts'}, ('', 'UICamera'): {'Update'}}
    addresses = method_addresses(selected)
    token, size = elf_symbols(nd)['token']
    if size != 44: raise ValueError('Unexpected legacy token buffer')
    fields = [('START', 'TitleMenuGroup', 'StartGame'), ('ACCOUNTS', 'Clusters', 'RequestAccounts'),
              ('UPDATED', 'TitleMenuUserControlBase', 'OnClusterAccountUpdated'),
              ('CONFIRM', 'TitleMenuUserControlBase', 'OnConfirm'),
              ('CAMERA', 'UICamera', 'Update'), ('GUI', 'NGUIDebug', 'OnGUI')]
    original = (ROOT.parent / 'Durango original/lib/arm64-v8a/libil2cpp.so').read_bytes()
    lines = [f'#define ND_TOKEN_OFFSET 0x{token:x}']
    for macro, cls, member in fields:
        address = addresses[(cls, member)]
        prologue = original[address:address + 16]
        if il2cpp[address:address + 16] != prologue: raise ValueError('Engine entry differs: ' + member)
        lines += [f'#define {macro}_OFFSET 0x{address:x}',
                  f'#define {macro}_PROLOGUE ((const unsigned char[]){{' + ','.join(f'0x{b:02x}' for b in prologue) + '})']
    output.write_text('\n'.join(lines) + '\n', 'ascii')
    return {'token_offset': token, 'entries': {f'{cls}.{m}': addresses[(cls,m)] for _,cls,m in fields}}


def method_addresses(selected):
    native = (ROOT.parent / 'Durango original/lib/arm64-v8a/libil2cpp.so').read_bytes()
    phoff, shoff = struct.unpack_from('<QQ', native, 32)
    phsize, phnum, shsize, shnum = struct.unpack_from('<HHHH', native, 54)
    segments = [struct.unpack_from('<IIQQQQQQ', native, phoff + i * phsize) for i in range(phnum)]
    segments = [p for p in segments if p[0] == 1]
    sections = [struct.unpack_from('<IIQQQQIIQQ', native, shoff + i * shsize) for i in range(shnum)]
    relocations = {}
    for section in sections:
        if section[1] == 4:
            for pos in range(section[4], section[4] + section[5], section[9]):
                target, info, addend = struct.unpack_from('<QQq', native, pos)
                if info & 0xffffffff == 1027:
                    relocations[target] = addend
    def offset(address):
        for seg in segments:
            if seg[3] <= address < seg[3] + seg[5]:
                return seg[2] + address - seg[3]
        raise ValueError(address)
    def pointer(address):
        return relocations.get(address, struct.unpack_from('<Q', native, offset(address))[0])
    metadata = (ROOT.parent / 'Durango original/assets/bin/Data/Managed/Metadata/global-metadata.dat').read_bytes()
    header = struct.unpack_from('<68I', metadata)
    methods = [struct.unpack_from('<12I4H', metadata, header[12] + i * 56) for i in range(header[13] // 56)]
    count = max(m[6] for m in methods if m[6] != 0xffffffff) + 1
    needle, start, candidates = struct.pack('<Q', count), 0, []
    while True:
        pos = native.find(needle, start)
        if pos < 0: break
        start = pos + 8
        if pos % 8: continue
        try:
            seg = next(s for s in segments if s[2] <= pos < s[2] + s[5])
            table = pointer(seg[3] + pos - seg[2] + 8)
            if sum(0x100000 < pointer(table + i * 8) < 0x5000000 for i in range(20)) > 8:
                candidates.append(table)
        except (ValueError, StopIteration, struct.error): pass
    assert len(candidates) == 1
    def text(index):
        start = header[6] + index
        return metadata[start:metadata.index(b'\0', start)].decode()
    targets = {}
    for i in range(header[41] // 104):
        typ = struct.unpack_from('<20I8H2I', metadata, header[40] + i * 104)
        key = text(typ[1]), text(typ[0])
        if key not in selected: continue
        for m in methods[typ[13]:typ[13] + typ[20]]:
            name = text(m[0])
            if name in selected[key] and m[6] != 0xffffffff:
                targets[(key[1], name)] = pointer(candidates[0] + m[6] * 8)
    return targets


def metadata_types():
    data = (ROOT.parent / 'Durango original/assets/bin/Data/Managed/Metadata/global-metadata.dat').read_bytes()
    header = struct.unpack_from('<68I', data)
    def text(index):
        start = header[6] + index
        return data[start:data.index(b'\0', start)].decode()
    methods = [struct.unpack_from('<12I4H', data, header[12] + i * 56) for i in range(header[13] // 56)]
    types = {}
    for i in range(header[41] // 104):
        typ = struct.unpack_from('<20I8H2I', data, header[40] + i * 104)
        types[(text(typ[1]), text(typ[0]))] = [(text(m[0]), m[15], m[6]) for m in methods[typ[13]:typ[13] + typ[20]]]
    return types


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--disassemble', action='store_true')
    parser.add_argument('--gui-api', action='store_true')
    args = parser.parse_args()
    chosen = {('UnityEngine', 'GUI'), ('UnityEngine', 'TouchScreenKeyboard'),
              ('', 'NGUIDebug'), ('Durango.UI', 'TitleMenuGroup'),
              ('Durango.UI', 'TitleMenuUserControlBase'), ('Durango.Logic.Clusters', 'Clusters'),
              ('Durango.UI', 'TitlePlayerSelectionGroupBase'), ('', 'UICamera'),
              ('Durango.Logic.Clusters', 'Account')}
    types = metadata_types()
    if args.gui_api:
        for name in ('GUISkin', 'GUIStyle', 'Screen', 'Texture2D'):
            print(name, types.get(('UnityEngine', name)))
        exit()
    selected = {key: {name for name, _, idx in types.get(key, []) if idx != 0xffffffff} for key in chosen}
    addresses = method_addresses(selected)
    for key in sorted(chosen):
        if args.disassemble:
            continue
        print(key)
        for name, count, idx in types.get(key, []):
            print(' ', name, count, hex(addresses.get((key[1], name), 0)))
    if args.disassemble:
        from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN
        raw = (ROOT.parent / 'Durango original/lib/arm64-v8a/libil2cpp.so').read_bytes()
        dis = Cs(CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN)
        for cls, name in [('NGUIDebug','OnGUI'),('TitleMenuGroup','StartGame'),('Clusters','RequestAccounts'),
                          ('TitleMenuUserControlBase','OnClusterAccountUpdated'),('UICamera','Update'),('Account','.ctor')]:
            address = addresses[(cls,name)]
            print(cls, name, hex(address), raw[address:address+16].hex())
            for instruction in dis.disasm(raw[address:address+96], address):
                print(hex(instruction.address),instruction.mnemonic,instruction.op_str)
