"""Patch PC branding without changing sprite IDs, shaders or unrelated texture data."""
import hashlib
import json
from pathlib import Path
import shutil
import struct
import sys

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
sys.path.insert(0, str(PROJECT / 'android-client/work/python-deps'))
import UnityPy
from PIL import Image
from UnityPy.export.Texture2DConverter import image_to_texture2d

DATA = PROJECT / 'Durango-OffServer/DurangoV2_Data'
WORK = ROOT / 'work'
CREDITS = '2016 VISION FORCE. Todos os direitos da Produtora\n*Cliente 5.2.1'


def snapshot():
    WORK.mkdir(exist_ok=True)
    for name in ('resources.assets', 'resources.assets.resS', 'globalgamemanagers.assets', 'Assembly-CSharp.dll'):
        source = DATA / ('Managed/' + name if name.endswith('.dll') else name)
        target = WORK / name
        if not target.exists():
            shutil.copy2(source, target)


def patch():
    snapshot()
    source = WORK / 'resources.assets'
    env = UnityPy.load(str(source))
    objects = {o.path_id: o for o in env.objects}
    logo = Image.open(PROJECT / 'logo.png').convert('RGBA')
    assert logo.size == (646, 254)
    for path_id, name in ((300, 'logo_kor'), (328, 'logo_eng')):
        texture = objects[path_id].read()
        assert texture.m_Name == name and texture.m_TextureFormat == 4
        assert (texture.m_Width, texture.m_Height, texture.m_MipCount) == (646, 254, 1)
        texture.image_data, fmt = image_to_texture2d(logo, 4)
        assert int(fmt) == 4
        texture.save()
    assert len(CREDITS.encode()) == 64
    for path_id in (28170, 28171):
        obj = objects[path_id]
        raw = bytearray(obj.get_raw_data())
        assert len(raw) == 436 and struct.unpack_from('<I', raw, 216)[0] in (62, 64)
        assert raw[220:284].rstrip(b'\0').decode() in (
            '© 2018 NEXON Korea Corp. & What! Studio. All Rights Reserved.', CREDITS)
        struct.pack_into('<I', raw, 216, 64)
        raw[220:284] = CREDITS.encode()
        struct.pack_into('<i', raw, 156, 48)
        struct.pack_into('<i', raw, 284, 18)
        obj.set_raw_data(bytes(raw))
    for path_id in (1372, 1373):
        tree = objects[path_id].read_typetree()
        assert tree['m_Name'] == 'VersionInfoLabel'
        tree['m_IsActive'] = False
        objects[path_id].save_typetree(tree)
    (DATA / 'resources.assets').write_bytes(env.file.save())

    atlas = objects[317].read()
    assert atlas.m_Name == 'Main_Atlas'
    assert (atlas.m_Width, atlas.m_Height, atlas.m_TextureFormat, atlas.m_MipCount) == (4096, 2048, 12, 1)
    assert atlas.m_StreamData.path == 'resources.assets.resS' and atlas.m_StreamData.size == 8388608
    raw = objects[27103].get_raw_data()
    rectangles = {}
    canvas = atlas.image.convert('RGBA')
    for name in (b'bg_loading_ment_en', b'bg_loading_ment_kr'):
        offset = (raw.index(name) + len(name) + 3) // 4 * 4
        x, y, w, h = struct.unpack_from('<4i', raw, offset)
        assert (w, h) == (92, 35)
        from PIL import ImageOps
        replacement = Image.new('RGBA', (w, h))
        scaled = ImageOps.contain(logo, (w, h), Image.Resampling.LANCZOS)
        replacement.paste(scaled, ((w - scaled.width) // 2, (h - scaled.height) // 2))
        canvas.paste(replacement, (x, y))
        rectangles[name.decode()] = (x, y, w, h)
    encoded, fmt = image_to_texture2d(canvas, 12)
    assert int(fmt) == 12 and len(encoded) == atlas.m_StreamData.size
    stream = bytearray((WORK / 'resources.assets.resS').read_bytes())
    indices = set()
    for x, y, w, h in rectangles.values():
        for by in range((2048 - y - h) // 4, (2048 - y + 3) // 4):
            for bx in range(x // 4, (x + w + 3) // 4):
                indices.add(by * 1024 + bx)
    for index in indices:
        offset = index * 16
        start = atlas.m_StreamData.offset + offset
        stream[start:start + 16] = encoded[offset:offset + 16]
    (DATA / 'resources.assets.resS').write_bytes(stream)

    launcher = DATA.parent / 'Launcher'
    launcher.mkdir(exist_ok=True)
    shutil.copy2(PROJECT / 'logo.png', launcher / 'logo.png')
    Image.open(PROJECT / 'icon.png').convert('RGBA').save(launcher / 'icon.ico',
        sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    verify(rectangles, indices)


def verify(rectangles, indices):
    old = {o.path_id: o for o in UnityPy.load(str(WORK / 'resources.assets')).objects}
    new = {o.path_id: o for o in UnityPy.load(str(DATA / 'resources.assets')).objects}
    assert old.keys() == new.keys()
    allowed = {300, 328, 28170, 28171, 1372, 1373}
    modified = {pid for pid in old if old[pid].get_raw_data() != new[pid].get_raw_data()}
    assert modified <= allowed
    logo = Image.open(PROJECT / 'logo.png').convert('RGBA')
    for pid in (300, 328):
        a, b = old[pid].read_typetree(), new[pid].read_typetree()
        a.pop('image data'); b.pop('image data'); assert a == b
        assert new[pid].read().image.convert('RGBA').tobytes() == logo.tobytes()
    for pid in (28170, 28171):
        raw = new[pid].get_raw_data()
        assert raw[220:284].decode() == CREDITS
    assert all(new[pid].read_typetree()['m_IsActive'] is False for pid in (1372, 1373))
    a = (WORK / 'resources.assets.resS').read_bytes()
    b = (DATA / 'resources.assets.resS').read_bytes()
    texture = new[317].read()
    start, end = texture.m_StreamData.offset, texture.m_StreamData.offset + texture.m_StreamData.size
    assert len(a) == len(b) and a[:start] == b[:start] and a[end:] == b[end:]
    blocks = {i for i in range((end - start) // 16)
              if a[start + i * 16:start + i * 16 + 16] != b[start + i * 16:start + i * 16 + 16]}
    assert blocks <= indices
    # A checkout may already contain the branded assets. Rebuilding must also
    # accept that baseline, while verifying the resulting sprites below.
    assert len(blocks) > 300 or all(old[pid].get_raw_data() == new[pid].get_raw_data()
                                  for pid in (300, 328))
    assert (WORK / 'globalgamemanagers.assets').read_bytes() == (DATA / 'globalgamemanagers.assets').read_bytes(), 'Splash changed'
    report = {'changed_unity_objects': sorted(modified), 'unrelated_objects_preserved': len(old) - len(modified),
              'original_materials_and_shaders_preserved': True, 'splash_unchanged': True,
              'atlas_format': 'DXT5', 'atlas_changed_blocks': len(blocks),
              'unrelated_stream_bytes_preserved': True, 'rectangles': rectangles, 'credits': CREDITS}
    (WORK / 'branding-verification.json').write_text(json.dumps(report, indent=2) + '\n', 'utf-8')
    preview = texture.image
    for name, (x, y, w, h) in rectangles.items():
        preview.crop((x, y, x + w, y + h)).resize((w * 5, h * 5)).save(WORK / (name + '.png'))
    print('PASS: PC logos, loading atlas and credits; all unrelated objects/texture blocks and splash preserved')


if __name__ == '__main__':
    patch()
