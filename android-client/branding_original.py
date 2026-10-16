"""Insert Lost Horizon artwork into the original Unity 2017 client.

Keep asset IDs, sprite rectangles, texture formats and all unrelated objects.
Only the ETC2 blocks occupied by the two loading logos are recompressed.
"""
import hashlib
import json
from pathlib import Path
import shutil
import struct
import sys

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
sys.path.insert(0, str(ROOT / 'work/python-deps'))
import UnityPy
from PIL import Image, ImageOps
from UnityPy.export.Texture2DConverter import image_to_texture2d

DATA = 'assets/bin/Data/'
LOGOS = (DATA + 'c6c98a0f398372a41862a640adc466d2',
         DATA + 'b480e154e4087644ca53f5d666feeb3a')
ATLAS = DATA + 'bcb37f4b3d27e4a4f93a3ec965e256dc'
SPRITES = DATA + '19ae04aa5e3159148bf3c56716acbcae'
CREDITS = {DATA + '6827ab7f4ebc56143b5702d0d1b8abc6': 175,
           DATA + '7d9842174a415534b91e990f357c7f29': 156}
SPLASH = 'res/drawable/unity_static_splash.png'
TITLE_CREDITS = '2016 VISION FORCE. Todos os direitos da Produtora\n*Cliente 5.2.1'
OLD_CREDITS = '© 2018 NEXON Korea Corp. & What! Studio. All Rights Reserved.'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def icon_paths(source):
    return sorted(p.relative_to(source).as_posix() for p in (source / 'res').rglob('app_icon*.png'))


def changed_files(source):
    return set(LOGOS) | {ATLAS, SPLASH} | set(CREDITS) | set(icon_paths(source))


def object_by_id(env, path_id):
    return next(o for o in env.objects if o.path_id == path_id)


def loading_rectangles(source):
    env = UnityPy.load(str(source / SPRITES))
    raw = object_by_id(env, 3).get_raw_data()
    rectangles = {}
    for name in (b'bg_loading_ment_kr', b'bg_loading_ment_en'):
        assert raw.count(name) == 1
        offset = (raw.index(name) + len(name) + 3) // 4 * 4
        rectangles[name.decode()] = struct.unpack_from('<4i', raw, offset)
    assert rectangles == {'bg_loading_ment_kr': (1953, 157, 92, 35),
                          'bg_loading_ment_en': (2028, 964, 92, 35)}
    return rectangles


def logo_patch(logo, width, height):
    canvas = Image.new('RGBA', (width, height))
    resized = ImageOps.contain(logo, (width, height), Image.Resampling.LANCZOS)
    canvas.paste(resized, ((width - resized.width) // 2, (height - resized.height) // 2))
    return canvas


def atlas_blocks(rectangles, width, height):
    # Unity stores the compressed image bottom-up; NGUI rectangles are top-down.
    indices = set()
    for x, y, w, h in rectangles.values():
        for by in range((height - y - h) // 4, (height - y + 3) // 4):
            for bx in range(x // 4, (x + w + 3) // 4):
                indices.add(by * (width // 4) + bx)
    return indices


def save(env, path):
    path.write_bytes(env.file.save())


def apply(source, client, work):
    logo = Image.open(PROJECT / 'logo.png').convert('RGBA')
    assert logo.size == (646, 254)
    for relative in LOGOS:
        env = UnityPy.load(str(source / relative))
        texture = object_by_id(env, 1).read()
        assert (texture.m_Width, texture.m_Height, texture.m_TextureFormat, texture.m_MipCount) == (646, 254, 4, 1)
        texture.image_data, fmt = image_to_texture2d(logo, 4)
        assert int(fmt) == 4
        texture.save()
        save(env, client / relative)

    env = UnityPy.load(str(source / ATLAS))
    texture = object_by_id(env, 1).read()
    assert (texture.m_Width, texture.m_Height, texture.m_TextureFormat, texture.m_MipCount) == (4096, 2048, 47, 1)
    rectangles = loading_rectangles(source)
    image = texture.image.convert('RGBA')
    for x, y, w, h in rectangles.values():
        image.paste(logo_patch(logo, w, h), (x, y))
    encoded, fmt = image_to_texture2d(image, 47)
    original = bytes(texture.image_data)
    assert int(fmt) == 47 and len(encoded) == len(original) == 8388608
    patched = bytearray(original)
    blocks = atlas_blocks(rectangles, image.width, image.height)
    for index in blocks:
        offset = index * 16
        patched[offset:offset + 16] = encoded[offset:offset + 16]
    texture.image_data = bytes(patched)
    texture.save()
    save(env, client / ATLAS)

    payload = TITLE_CREDITS.encode('utf-8')
    assert len(payload) == 64  # Same aligned allocation as the original 62-byte label.
    for relative, label_id in CREDITS.items():
        env = UnityPy.load(str(source / relative))
        label = object_by_id(env, label_id)
        raw = bytearray(label.get_raw_data())
        assert len(raw) == 436 and struct.unpack_from('<I', raw, 216)[0] == 62
        assert bytes(raw[220:282]) == OLD_CREDITS.encode('utf-8')
        struct.pack_into('<I', raw, 216, len(payload))
        raw[220:284] = payload
        struct.pack_into('<i', raw, 156, 48)  # Label height accommodates two lines.
        struct.pack_into('<i', raw, 284, 18)  # Font fits the first line in both layouts.
        label.set_raw_data(bytes(raw))
        version = object_by_id(env, 2)
        tree = version.read_typetree()
        assert tree['m_Name'] == 'VersionInfoLabel'
        tree['m_IsActive'] = False  # Version is now the second line of the credits.
        version.save_typetree(tree)
        save(env, client / relative)

    splash = PROJECT / 'credits_splash_nexon_what.png'
    assert Image.open(splash).size == Image.open(source / SPLASH).size == (1024, 576)
    shutil.copy2(splash, client / SPLASH)
    icon = Image.open(PROJECT / 'icon.png').convert('RGBA')
    paths = icon_paths(source)
    assert len(paths) == 30
    for relative in paths:
        size = Image.open(source / relative).size
        ImageOps.fit(icon, size, Image.Resampling.LANCZOS).save(client / relative)

    report = verify(source, client)
    (work / 'branding-verification.json').write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    return report


def verify_objects(source, client, relative, allowed):
    before = UnityPy.load(str(source / relative))
    after = UnityPy.load(str(client / relative))
    original = {o.path_id: o for o in before.objects}
    rebuilt = {o.path_id: o for o in after.objects}
    assert original.keys() == rebuilt.keys(), 'Object references changed: ' + relative
    modified = set()
    for path_id, obj in original.items():
        if obj.get_raw_data() != rebuilt[path_id].get_raw_data():
            assert path_id in allowed, f'Unexpected modified object: {relative}:{path_id}'
            modified.add(path_id)
    assert modified == allowed
    return original, rebuilt


def verify(source, client):
    logo = Image.open(PROJECT / 'logo.png').convert('RGBA')
    for relative in LOGOS:
        before, after = verify_objects(source, client, relative, {1})
        a, b = before[1].read_typetree(), after[1].read_typetree()
        a.pop('image data'); b.pop('image data')
        assert a == b, 'Logo metadata changed'
        assert after[1].read().image.convert('RGBA').tobytes() == logo.tobytes()

    before, after = verify_objects(source, client, ATLAS, {1})
    a, b = before[1].read_typetree(), after[1].read_typetree()
    raw_a, raw_b = bytes(a.pop('image data')), bytes(b.pop('image data'))
    assert a == b, 'Atlas metadata changed'
    rectangles = loading_rectangles(source)
    allowed_blocks = atlas_blocks(rectangles, 4096, 2048)
    changed_blocks = {i for i in range(len(raw_a) // 16)
                      if raw_a[i * 16:i * 16 + 16] != raw_b[i * 16:i * 16 + 16]}
    assert changed_blocks <= allowed_blocks and len(changed_blocks) > 300
    assert sha(source / SPRITES) == sha(client / SPRITES), 'Sprite references changed'
    preview = after[1].read().image.convert('RGBA')
    for name, (x, y, w, h) in rectangles.items():
        region = preview.crop((x, y, x + w, y + h))
        expected = logo_patch(logo, w, h)
        # Compressed colors are lossy; compare opacity silhouettes to catch flips/offsets.
        alpha = region.getchannel('A'); expected_alpha = expected.getchannel('A')
        error = sum(abs(a - b) for a, b in zip(alpha.tobytes(), expected_alpha.tobytes())) / (w * h)
        assert error < 12, f'Incorrect loading logo at {name}: {error}'

    for relative, label_id in CREDITS.items():
        before, after = verify_objects(source, client, relative, {2, label_id})
        raw = after[label_id].get_raw_data()
        length = struct.unpack_from('<I', raw, 216)[0]
        assert raw[220:220 + length].decode('utf-8') == TITLE_CREDITS
        assert len(raw) == len(before[label_id].get_raw_data())
        assert after[2].read_typetree()['m_IsActive'] is False

    assert sha(client / SPLASH) == sha(PROJECT / 'credits_splash_nexon_what.png')
    icon = Image.open(PROJECT / 'icon.png').convert('RGBA')
    paths = icon_paths(source)
    for relative in paths:
        actual = Image.open(client / relative).convert('RGBA')
        assert actual.size == Image.open(source / relative).size
        assert actual.tobytes() == ImageOps.fit(icon, actual.size, Image.Resampling.LANCZOS).tobytes()
    assert sha(client / 'assets/durango-br/launcher/web/logo-durango-brasil.png') == sha(PROJECT / 'logo.png')
    return {'name': 'Lost Horizon', 'inputs': {name: sha(PROJECT / name) for name in
            ('icon.png', 'logo.png', 'credits_splash_nexon_what.png')},
            'changed_files': sorted(changed_files(source)), 'icon_variants': len(paths),
            'loading_rectangles': rectangles, 'atlas_format': 'ETC2_RGBA8',
            'atlas_changed_blocks': len(changed_blocks), 'atlas_total_blocks': len(raw_a) // 16,
            'unrelated_atlas_blocks_preserved': True, 'sprite_metadata_preserved': True,
            'title_credits': TITLE_CREDITS, 'unrelated_unity_objects_preserved': True}
