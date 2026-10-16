"""Validate the final APK against the extracted client and native patch invariants."""
from pathlib import Path
import hashlib
import json
import struct
import zipfile

ROOT = Path(__file__).resolve().parent
APK_NAME = 'DurangoBrasil-alfa-3.apk'
EXTRACTED = ROOT.parent / 'DurangoBrasilApk'
GATEWAY = 'http://179.197.72.129:8190'
checks = 0


def check(value, description):
    global checks
    if not value:
        raise AssertionError(description)
    checks += 1


with zipfile.ZipFile(ROOT / 'dist' / APK_NAME) as archive:
    names = archive.namelist()
    check(len(names) == len(set(names)), 'No duplicate ZIP entries')
    check(archive.testzip() is None, 'ZIP CRC integrity')
    for path in EXTRACTED.rglob('*'):
        if not path.is_file():
            continue
        name = path.relative_to(EXTRACTED).as_posix()
        if name.startswith('META-INF/'):
            continue
        check(archive.read(name) == path.read_bytes(), f'Packed file matches source: {name}')
        if name.startswith('assets/'):
            check(archive.getinfo(name).compress_type == zipfile.ZIP_STORED, f'Unity assets readable with openFd: {name}')
    for relative in ['classes.dex', 'lib/arm64-v8a/libil2cpp.so']:
        path = ROOT / 'base' / relative
        baseline = path if path.exists() else EXTRACTED / relative
        check(archive.read(relative) == baseline.read_bytes(), f'Game binary preserved: {relative}')
    manifest = archive.read('AndroidManifest.xml')
    check('com.durangobrasil.apk'.encode('utf-16le') in manifest, 'Brazilian application package')
    check('com.primalcolony.game'.encode('utf-16le') not in manifest, 'Old package removed from manifest')
    data = archive.read('assets/bin/Data/b7b0096f71e8a0640be95cc8b863f74d')
    start = data.index(b'{')
    size = struct.unpack_from('<I', data, start - 4)[0]
    cluster_set = json.loads(data[start:start+size])
    check(len(cluster_set['clusters']) == 1, 'One game server')
    cluster = next(iter(cluster_set['clusters'].values()))
    check(cluster['gateway_url_root'] == GATEWAY, 'Unity uses Brazilian gateway')
    check(cluster['name']['pt_BR'] == 'Durango Brasil', 'Brazilian server label')
    dex = archive.read('classes2.dex')
    check(GATEWAY.encode() in dex, 'AUTH uses Brazilian gateway')
    check(b'/auth/login' in dex and b'/auth/register' in dex, 'Gateway auth endpoints compiled')
    check(b'auth_token' in dex, 'Gateway login token parser compiled')
    check(b'/launcher/game-session' not in dex, 'Old ticket exchange removed')
    check(b'supabase.co' not in dex, 'Old authentication provider removed')
    native = archive.read('lib/arm64-v8a/libnd.so')
    check(b'AddField\x00' in native and b'SetHeader\x00' not in native, 'Native token form bridge')
    check(b'X-NewDawn-Session\x00' not in native, 'Old ticket header removed')
    check(struct.unpack_from('<I', native, 0x2aa4)[0] == 0xf100ac1f, '43-character token length')
    for file_offset, target_va in [(0x2aac, 0x3b04), (0x3728, 0x4920)]:
        instruction = struct.unpack_from('<I', native, file_offset)[0]
        offset = instruction & 0x3ffffff
        if offset & (1 << 25):
            offset -= 1 << 26
        check(file_offset + 0x1000 + offset * 4 == target_va, 'Native branch target')
    js = archive.read('assets/newdawn/launcher/web/mobile.js').decode('utf-8')
    html = archive.read('assets/newdawn/launcher/web/index.html').decode('utf-8')
    check('Usuário do Durango Brasil' in html and 'type="email"' not in html, 'Username input')
    check('Durango Brasil' in html and 'alfa do Durango Brasil' in js, 'Brazilian launcher text')
    check('id="background-video" autoplay loop muted playsinline' in html, 'Looping muted title video')
    check('../../../Movie/Mobile/title.mp4' in html, 'Existing title video reused')
    check('id="lista"' not in html and 'class="novidades"' not in html, 'News panels removed')
    check('logo-durango-brasil.png' in html, 'Native game logo above login')
    check(archive.read('assets/newdawn/launcher/web/logo-durango-brasil.png') ==
          (ROOT / 'ui/logo-durango-brasil.png').read_bytes(), 'Original title logo packed')
    check('id="confirmar-senha"' in html and "bridge[action](username, password)" in js,
          'Registration confirmation and original native authentication bridge')
    branding = json.loads((ROOT / 'branding/manifest.json').read_text('utf-8'))
    for entry in branding['files']:
        check(hashlib.sha256(archive.read(entry['path'])).hexdigest() == entry['sha256'],
              'User-edited branding preserved: ' + entry['role'])
    for filename in ['6827ab7f4ebc56143b5702d0d1b8abc6', '7d9842174a415534b91e990f357c7f29']:
        data = archive.read('assets/bin/Data/' + filename)
        check('Vision Force\nServidor Brasileiro\nVersão 1.0'.encode('utf-8') in data,
              'Three-line Brazilian title credits: ' + filename)
        check(b'NEXON Korea Corp.' not in data, 'Old title credits replaced: ' + filename)
    icon_manifest = json.loads((ROOT / 'branding/app-icon-manifest.json').read_text('utf-8'))
    for entry in icon_manifest['files']:
        check(hashlib.sha256(archive.read(entry['path'])).hexdigest() == entry['sha256'],
              'Brazilian app icon: ' + entry['path'])
    atlas = archive.read('assets/bin/Data/19ae04aa5e3159148bf3c56716acbcae')
    rectangles = []
    for name in (b'bg_loading_ment_kr', b'bg_loading_ment_en'):
        position = (atlas.index(name) + len(name) + 3) // 4 * 4
        rectangles.append(struct.unpack_from('<4i', atlas, position))
    check(rectangles[0] == rectangles[1] == (1953, 157, 92, 35),
          'All loading locales use the Brazilian artwork rectangle')

file = ROOT / 'dist' / APK_NAME
digest = hashlib.sha256(file.read_bytes()).hexdigest()
check(digest in (ROOT / 'dist' / (APK_NAME + '.sha256')).read_text(), 'APK checksum')
print(f'PASS: {checks} APK integrity/connection checks; size {file.stat().st_size:,} bytes')
