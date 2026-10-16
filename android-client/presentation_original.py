"""Original Unity 2017 Android client: title links and offline prologue video.

Keep metadata offsets, method definitions and all other string literals intact.
Redirect only the title door callback to the existing notice handler, whose
literal becomes the community Discord. Keep the original movie/skip callbacks.
"""
import hashlib
from pathlib import Path
import struct
import estate_original as estate
import discovery_original as discovery

METADATA = Path('assets/bin/Data/Managed/Metadata/global-metadata.dat')
NATIVE = Path('lib/arm64-v8a/libil2cpp.so')
MOVIE = Path('assets/Movie/Mobile/warping.mp4')
MOVIE_PATH = 'Movie/Mobile/warping.mp4'
DISCORD = 'https://discord.gg/nFHKbS7De'
REPLACEMENTS = {
    'https://m.nexon.com/notice?client_id=MzI0OTEzMjMy': DISCORD,
    'https://d1skbslnewf3os.cloudfront.net/prologue_movie.mp4': MOVIE_PATH,
    'https://d1skbslnewf3os.cloudfront.net/prologue_movie_dev.mp4': MOVIE_PATH,
}
# TitleMenuUserControl.<ShowCluster>c__AnonStorey0.<>m__0 is assigned exclusively
# to _logoutButton.Clicked. Platform.ShowNotice ignores its receiver/MethodInfo,
# making a tail branch ABI-compatible with this void instance callback.
DOOR_CALLBACK = 0x1762484
NOTICE_HANDLER = 0x1951874
DOOR_ORIGINAL = bytes.fromhex('f44fbea9')


def door_branch():
    delta = NOTICE_HANDLER - DOOR_CALLBACK
    assert delta % 4 == 0 and -(1 << 27) <= delta < (1 << 27)
    return struct.pack('<I', 0x14000000 | ((delta // 4) & 0x3ffffff))


def literal_records(data):
    header = struct.unpack_from('<68I', data)
    assert header[:2] == (0xfab11baf, 24)
    assert header[3] % 8 == 0
    for offset in range(header[2], header[2] + header[3], 8):
        length, index = struct.unpack_from('<II', data, offset)
        start = header[4] + index
        assert index + length <= header[5]
        yield offset, start, length, data[start:start + length]


def patch_metadata(data):
    result = bytearray(data)
    seen = set()
    for offset, start, length, payload in literal_records(data):
        old = payload.decode('utf-8', errors='replace')
        if old not in REPLACEMENTS:
            continue
        assert old not in seen, 'Unexpected duplicate literal: ' + old
        seen.add(old)
        new = REPLACEMENTS[old].encode('utf-8')
        assert len(new) <= length
        struct.pack_into('<I', result, offset, len(new))
        result[start:start + length] = new.ljust(length, b'\0')
    assert seen == set(REPLACEMENTS), 'Original presentation literals changed'
    return bytes(result)


def apply(source, client):
    assert (source / MOVIE).is_file() and (source / MOVIE).stat().st_size > 0
    (client / METADATA).write_bytes(patch_metadata((source / METADATA).read_bytes()))
    native = bytearray((client / NATIVE).read_bytes())
    assert native[DOOR_CALLBACK:DOOR_CALLBACK + 4] == DOOR_ORIGINAL
    # B keeps the caller's return address: clicking the door opens Discord and
    # returns to the title menu without clearing the account or requesting logout.
    native[DOOR_CALLBACK:DOOR_CALLBACK + 4] = door_branch()
    (client / NATIVE).write_bytes(native)
    return verify(source, client)


def verify(source, client):
    original = (source / METADATA).read_bytes()
    modified = (client / METADATA).read_bytes()
    assert len(original) == len(modified)
    assert modified == patch_metadata(original), 'Unrelated metadata changed'
    before, after = list(literal_records(original)), list(literal_records(modified))
    assert len(before) == len(after)
    changed = []
    for a, b in zip(before, after):
        assert a[:2] == b[:2], 'Literal offsets changed'
        if a[3] != b[3]:
            old = a[3].decode('utf-8')
            assert old in REPLACEMENTS and b[3].decode('utf-8') == REPLACEMENTS[old]
            changed.append(old)
        else:
            assert a == b
    assert set(changed) == set(REPLACEMENTS)
    # Native edits are limited to authentication, the title-door branch and
    # the estate redirect and two discovery-cache arguments; every unrelated byte is preserved.
    expected = bytearray((source / NATIVE).read_bytes())
    expected[0x960a:0x9616] = b'libbr.so\0'.ljust(12, b'\0')
    struct.pack_into('<I', expected, 0x137b240, 0x2a1f03e0)
    struct.pack_into('<I', expected, 0x137b748, 0x14000048)
    expected[DOOR_CALLBACK:DOOR_CALLBACK + 4] = door_branch()
    # apply() also verifies this module before the estate patch is applied.
    native = (client / NATIVE).read_bytes()
    if native[estate.PAID_CALL:estate.PAID_CALL + 4] == estate.branch(estate.FREE_METHOD):
        expected = bytearray(estate.patch(expected))
    if all(native[site:site+4] == discovery.PATCHES[site] for site in discovery.SITES):
        expected = bytearray(discovery.patch(expected))
    assert native == bytes(expected), 'Unexpected native edit'
    movie = (source / MOVIE).read_bytes()
    assert (client / MOVIE).read_bytes() == movie, 'Transition video changed'
    return {
        'megaphone_url': DISCORD, 'door_url': DISCORD,
        'door_callback': hex(DOOR_CALLBACK), 'notice_handler': hex(NOTICE_HANDLER),
        'door_native_patch_bytes': 4, 'changed_string_literals': len(changed),
        'unrelated_string_literals_preserved': len(before) - len(changed),
        'metadata_offsets_and_method_definitions_preserved': True,
        'prologue_video': MOVIE_PATH,
        'video_sha256': hashlib.sha256(movie).hexdigest(),
        'original_movie_completion_and_hold_to_skip_preserved': True,
    }
