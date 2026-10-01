"""Build the Brazilian client from the user's extracted APK without recompiling Unity."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import secrets
import shutil
import struct
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent
APK = PROJECT / 'DurangoBrasilApk'
WORK = ROOT / 'work'
DIST = ROOT / 'dist'
GATEWAY = 'http://179.197.72.129:8190'
OUTPUT_NAME = 'DurangoBrasil-alfa-2.apk'
TITLE_CREDITS = 'Vision Force\nServidor Brasileiro\nVersão 1.0'
JAVA_BIN = Path('C:/Program Files/Android/Android Studio/jbr/bin')
SDK = Path.home() / 'AppData/Local/Android/Sdk'
TOOLS = SDK / 'build-tools/36.0.0'


def run(*args):
    subprocess.run([str(arg) for arg in args], check=True, cwd=PROJECT)


def original(relative):
    baseline = ROOT / 'base' / relative
    if baseline.is_file():
        records = json.loads((ROOT / 'base/manifest.json').read_text('utf-8'))
        expected = next(entry['sha256'] for entry in records if entry['path'] == relative.as_posix())
        data = baseline.read_bytes()
        if hashlib.sha256(data).hexdigest() != expected:
            raise ValueError('Build baseline hash changed: ' + str(relative))
        return data
    backup = WORK / 'original' / relative
    if not backup.exists():
        backup.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(APK / relative, backup)
    return backup.read_bytes()


def replace_method(text, signature, body):
    pattern = r'^\.method [^\n]*' + re.escape(signature) + r'\n.*?^\.end method'
    result, count = re.subn(pattern, lambda m: '.method ' + m.group(0).split('\n')[0][8:] +
                            '\n' + body + '\n.end method', text, flags=re.M | re.S)
    if count != 1:
        raise ValueError(f'Method not unique: {signature} ({count})')
    return result


def patch_launcher(decoded):
    folder = decoded / 'smali_classes2/com/newdawn/launcher'
    path = folder / 'LauncherActivity.smali'
    text = path.read_text('utf-8')
    for method in ['gateway()Ljava/lang/String;', 'origins()Ljava/lang/String;']:
        text = replace_method(text, method, f'    .locals 1\n    const-string v0, "{GATEWAY}"\n    return-object v0')
    # Separate saved credentials from the community application. Never check its updater
    # or send crash reports to its service; those routes do not exist on this gateway.
    for method in ['checkUpdate()V', 'openUpdate()V', 'reportCrash(Z)V', 'showServerDialog()V']:
        text = replace_method(text, method, '    .locals 0\n    return-void')
    text = text.replace('"newdawn"', '"durango-br-auth-v1"')
    text = text.replace('"launcher 0.4.9"', '"Durango Brasil Android alfa 2"')
    text = text.replace('"https://api.durangonewdawn.com"', json.dumps(GATEWAY))
    text = re.sub(r'"http://api\.durangonewdawn\.com:8190;[^"]+"', json.dumps(GATEWAY), text)
    # v1 is zero in buildUi; allow the local title video to autoplay behind AUTH.
    old_media = 'invoke-virtual {v0, v2}, Landroid/webkit/WebSettings;->setMediaPlaybackRequiresUserGesture(Z)V'
    if text.count(old_media) != 1:
        raise ValueError('Unexpected WebView media configuration')
    text = text.replace(old_media, old_media.replace('{v0, v2}', '{v0, v1}'))
    path.write_text(text, 'utf-8')
    strings = folder / 'Strings.smali'
    text = strings.read_text('utf-8')
    text = replace_method(text, 'get(I)Ljava/lang/String;', '''    .locals 2
    const/16 v0, 0x30
    if-ne p0, v0, :credentials
    const-string v0, "Esse usuário já existe. Entre com sua senha."
    return-object v0
    :credentials
    const/16 v0, 0x31
    if-ne p0, v0, :username
    const-string v0, "Usuário ou senha incorretos."
    return-object v0
    :username
    const/16 v0, 0x17
    if-ne p0, v0, :other
    const-string v0, "Use um usuário de 3 a 32 letras, números, ponto, hífen ou sublinhado."
    return-object v0
    :other
    sget-object v0, Lcom/newdawn/launcher/Strings;->PT:[Ljava/lang/String;
    aget-object v0, v0, p0
    return-object v0''')
    text = text.replace('"E-mail"', '"Usuário"').replace('"PRIMAL"', '"DURANGO"').replace('"COLONY"', '"BRASIL"')
    strings.write_text(text, 'utf-8')


def patch_native():
    relative = Path('lib/arm64-v8a/libnd.so')
    before = original(relative)
    data = bytearray(before)

    def word(offset, expected, value):
        old = struct.unpack_from('<I', data, offset)[0]
        if old != expected:
            raise ValueError(f'Unexpected native instruction at {offset:#x}: {old:#x}')
        struct.pack_into('<I', data, offset, value)

    # This ELF's executable segment maps file offset +0x1000 to the virtual address.
    # Ticket buffer already holds 65 bytes; the gateway issues 43-character Base64URL tokens.
    word(0x3aa4 - 0x1000, 0xf101001f, 0xf100ac1f)  # cmp x0, #64 -> #43
    # After the length check, bypass the obsolete hexadecimal-ticket loop.
    offset = 0x3aac - 0x1000
    word(offset, 0xaa1f03e8, 0x14000000 | ((0x3b04 - 0x3aac) // 4))
    assert data.count(b'SetHeader\x00') == 1
    assert data.count(b'X-NewDawn-Session\x00') == 1
    data = data.replace(b'SetHeader\x00', b'AddField\x00\x00')
    field = b'X-NewDawn-Session\x00'
    data = data.replace(field, b'token\x00'.ljust(len(field), b'\x00'))
    # Do not add form fields to authenticated /entry or /players requests. Their
    # original game-session Authorization headers remain intact.
    word(0x4700 - 0x1000, 0x54000460, 0x54000460)  # /accounts keeps existing origin guard
    word(0x4724 - 0x1000, 0x54000340, 0x54000340)  # /sessions keeps existing origin guard
    word(0x4728 - 0x1000, 0xd28e05e9, 0x14000000 | ((0x4920 - 0x4728) // 4))
    if len(data) != len(before):
        raise ValueError('Native ELF length changed')
    (APK / relative).write_bytes(data)


def patch_assets():
    relative = Path('assets/bin/Data/b7b0096f71e8a0640be95cc8b863f74d')
    data = bytearray(original(relative))
    start = data.index(b'{\n')
    size = struct.unpack_from('<I', data, start - 4)[0]
    old = json.loads(data[start:start + size])
    cluster = next(iter(old['clusters'].values()))
    cluster['gateway_url_root'] = GATEWAY
    cluster['countries'] = ['BR']
    cluster['name'] = {locale: 'Durango Brasil' for locale in ['pt_BR', 'en_US', 'ko_KR', 'th_TH']}
    old['clusters'] = {'durango_brasil': cluster}
    payload = json.dumps(old, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    if len(payload) > size:
        raise ValueError('Cluster payload exceeded original TextAsset allocation')
    data[start:start + size] = payload.ljust(size, b' ')
    (APK / relative).write_bytes(data)
    # Same-sized replacements preserve binary XML/resource string-pool offsets.
    for relative in [Path('AndroidManifest.xml'), Path('resources.arsc')]:
        data = original(relative)
        for old, new in [('com.primalcolony.game', 'com.durangobrasil.apk'), ('Primal Colony', 'DurangoBrasil')]:
            assert len(old) == len(new)
            for encoding in ['utf-8', 'utf-16le']:
                data = data.replace(old.encode(encoding), new.encode(encoding))
        (APK / relative).write_bytes(data)
    apply_login_ui()
    apply_branding()


def validate_branding_inputs():
    manifest = json.loads((ROOT / 'branding/manifest.json').read_text('utf-8'))
    for entry in manifest['files']:
        source = ROOT / 'branding/resources' / entry['path']
        if hashlib.sha256(source.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Branding source hash changed: ' + entry['path'])
        current = APK / entry['path']
        if current.exists() and hashlib.sha256(current.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Imagem editada na pasta original; execute prepare_branding.py antes de compilar: ' + entry['path'])
    return manifest


def apply_branding():
    manifest = validate_branding_inputs()
    for entry in manifest['files']:
        source = ROOT / 'branding/resources' / entry['path']
        shutil.copy2(source, APK / entry['path'])

    # Keep the serialized string allocation and all object offsets unchanged.
    # NGUI's zero-width color resets fill the spare bytes without visible padding.
    previous = '© 2018 NEXON Korea Corp. & What! Studio. All Rights Reserved.'.encode('utf-8')
    payload = TITLE_CREDITS.encode('utf-8')
    remaining = len(previous) - len(payload)
    if remaining < 0 or remaining % 3:
        raise ValueError('Title credits exceed the verified label allocation')
    payload += b'[-]' * (remaining // 3)
    for filename, path_id, width, height, font_size in (
        ('6827ab7f4ebc56143b5702d0d1b8abc6', 175, 482, 20, 19),
        ('7d9842174a415534b91e990f357c7f29', 156, 566, 22, 22),
    ):
        relative = Path('assets/bin/Data') / filename
        baseline = original(relative)
        data = bytearray(baseline)
        if data.count(previous) != 1:
            raise ValueError('Unexpected title copyright label: ' + filename)
        text_offset = data.index(previous)
        label_start = text_offset - 220
        expected = {148: 4, 152: width, 156: height, 216: len(previous), 284: font_size, 332: 2}
        for offset, value in expected.items():
            if struct.unpack_from('<i', data, label_start + offset)[0] != value:
                raise ValueError(f'Unexpected UILabel {path_id} field at {offset}')
        data[text_offset:text_offset + len(previous)] = payload
        # Bottom pivot preserves the bottom margin when ResizeFreely expands to three lines.
        struct.pack_into('<i', data, label_start + 148, 7)
        struct.pack_into('<i', data, label_start + 156, 72)
        struct.pack_into('<i', data, label_start + 284, 18)
        if len(data) != len(baseline):
            raise ValueError('Serialized title asset length changed')
        (APK / relative).write_bytes(data)


def apply_login_ui():
    target = APK / 'assets/newdawn/launcher/web'
    for name in ['index.html', 'mobile.js', 'logo-durango-brasil.png']:
        if (target / name).exists():
            original(Path('assets/newdawn/launcher/web') / name)
        shutil.copy2(ROOT / 'ui' / name, target / name)


def compile_launcher():
    jar = WORK / 'apktool.jar'
    source_apk = WORK / 'inspect.apk'
    with zipfile.ZipFile(source_apk, 'w') as archive:
        for name in ['AndroidManifest.xml', 'resources.arsc', 'classes.dex', 'classes2.dex']:
            archive.writestr(name, original(Path(name)))
    decoded = WORK / 'decoded'
    run(JAVA_BIN / 'java.exe', '-jar', jar, 'd', '-r', '-f', '-o', decoded, source_apk)
    patch_launcher(decoded)
    classes = WORK / 'java-classes'
    classes.mkdir(exist_ok=True)
    run(JAVA_BIN / 'javac.exe', '--release', '8', '-encoding', 'UTF-8', '-cp', WORK / 'android.jar',
        '-d', classes, ROOT / 'src/com/newdawn/launcher/NewDawnApi.java')
    new_dex = WORK / 'new-api'
    new_dex.mkdir(exist_ok=True)
    run(JAVA_BIN / 'java.exe', '-cp', WORK / 'd8.jar', 'com.android.tools.r8.D8', '--min-api', '21',
        '--lib', WORK / 'android.jar', '--output', new_dex, *sorted(classes.rglob('*.class')))
    api_apk = WORK / 'api.apk'
    with zipfile.ZipFile(api_apk, 'w') as archive:
        for name in ['AndroidManifest.xml', 'resources.arsc']:
            archive.writestr(name, original(Path(name)))
        archive.write(new_dex / 'classes.dex', 'classes.dex')
    api_decoded = WORK / 'api-decoded'
    run(JAVA_BIN / 'java.exe', '-jar', jar, 'd', '-r', '-f', '-o', api_decoded, api_apk)
    dest = decoded / 'smali_classes2/com/newdawn/launcher'
    for path in dest.glob('NewDawnApi*.smali'):
        path.unlink()
    for path in (api_decoded / 'smali/com/newdawn/launcher').glob('NewDawnApi*.smali'):
        shutil.copy2(path, dest / path.name)
    rebuilt = WORK / 'launcher-rebuilt.apk'
    run(JAVA_BIN / 'java.exe', '-jar', jar, 'b', decoded, '-o', rebuilt)
    with zipfile.ZipFile(rebuilt) as archive:
        (APK / 'classes2.dex').write_bytes(archive.read('classes2.dex'))


def package():
    unsigned = WORK / 'DurangoBrasil-unsigned.apk'
    with zipfile.ZipFile(unsigned, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(APK.rglob('*')):
            if not path.is_file():
                continue
            name = path.relative_to(APK).as_posix()
            if name.startswith('META-INF/') and (path.suffix.upper() in ['.SF', '.RSA', '.DSA', '.EC'] or path.name == 'MANIFEST.MF'):
                continue
            # Unity/Wwise/video can use AssetManager.openFd, which requires stored assets.
            compression = zipfile.ZIP_STORED if name.startswith('assets/') or name.endswith(('.so', '.arsc')) else zipfile.ZIP_DEFLATED
            archive.write(path, name, compress_type=compression)
    aligned = WORK / 'DurangoBrasil-aligned.apk'
    run(WORK / 'zipalign.exe', '-P', '16', '-f', '4', unsigned, aligned)
    key = ROOT / 'durango-br-test.jks'
    password = ROOT / 'durango-br-test.pass'
    if not key.exists():
        password.write_text(secrets.token_urlsafe(32), 'ascii')
        run(JAVA_BIN / 'keytool.exe', '-genkeypair', '-keystore', key, '-storepass:file', password,
            '-keypass:file', password, '-alias', 'durango-br-test', '-keyalg', 'RSA', '-keysize', '2048',
            '-validity', '10000', '-dname', 'CN=Durango Brasil Teste, O=Durango Brasil, C=BR')
    output = DIST / OUTPUT_NAME
    run(JAVA_BIN / 'java.exe', '-jar', WORK / 'apksigner.jar', 'sign', '--ks', key,
        '--ks-pass', f'file:{password}', '--v4-signing-enabled', 'false',
        '--out', output, aligned)
    run(JAVA_BIN / 'java.exe', '-jar', WORK / 'apksigner.jar', 'verify', '--verbose', '--print-certs', output)
    run(WORK / 'zipalign.exe', '-c', '-P', '16', '4', output)
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    (DIST / (OUTPUT_NAME + '.sha256')).write_text(digest + '  ' + output.name + '\n', 'ascii')
    print(f'APK: {output}\nSHA256: {digest}', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--package-only', action='store_true')
    parser.add_argument('--ui-only', action='store_true', help='Apply only the login assets; do not rebuild or package the APK')
    args = parser.parse_args()
    WORK.mkdir(exist_ok=True); DIST.mkdir(exist_ok=True)
    if args.ui_only:
        apply_login_ui()
        return
    validate_branding_inputs()
    if not args.package_only:
        compile_launcher()
        patch_native()
        patch_assets()
    package()


if __name__ == '__main__':
    main()
