"""Personalize the extracted Unity 6 client without replacing its engine or gameplay DLLs."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile

import build_apk as legacy

ROOT, PROJECT = legacy.ROOT, legacy.PROJECT
SOURCE = PROJECT / 'Durango-Brasil-v5.2.1'
WORK = ROOT / 'work/unity6'
BASE = ROOT / 'base-unity6'
DECODED = WORK / 'decoded-res'
DATA = SOURCE / 'assets/bin/Data'
sys.path.insert(0, str(ROOT / 'work/python-deps'))
import UnityPy
from PIL import Image, ImageOps

def original(relative):
    destination = BASE / relative
    if not destination.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(SOURCE / relative, destination)
    raw = destination.read_bytes()
    manifest = BASE / 'manifest.json'
    if manifest.exists():
        entry = next((v for v in json.loads(manifest.read_text('utf-8')) if v['path'] == relative.as_posix()), None)
        if entry and hashlib.sha256(raw).hexdigest() != entry['sha256']:
            raise ValueError('Unity 6 baseline hash changed: ' + str(relative))
    return raw

def initialize():
    WORK.mkdir(parents=True, exist_ok=True)
    # Decode only Android resources and the embedded selector, keeping builds small.
    inspect = WORK / 'resource-source.apk'
    with zipfile.ZipFile(inspect, 'w') as z:
        for name in ['AndroidManifest.xml', 'resources.arsc', 'classes.dex']:
            z.writestr(name, original(Path(name)))
        for p in (SOURCE / 'res').rglob('*'):
            if p.is_file(): z.write(p, p.relative_to(SOURCE).as_posix())
    legacy.run(legacy.JAVA_BIN/'java.exe', '-jar', ROOT/'work/apktool.jar', 'd', '-s', '-f', '-o', DECODED, inspect)
    inspect = WORK / 'menu.apk'
    bridge = original(Path('lib/arm64-v8a/libdbr.so'))
    with zipfile.ZipFile(inspect, 'w') as z:
        for name in ['AndroidManifest.xml', 'resources.arsc']: z.writestr(name, original(Path(name)))
        z.writestr('classes.dex', bridge[0x6e89:0x6e89+32448])
    legacy.run(legacy.JAVA_BIN/'java.exe', '-jar', ROOT/'work/apktool.jar', 'd', '-r', '-f', '-o', WORK/'menu-decoded', inspect)

def asset(filename):
    if filename == 'globalgamemanagers.assets':
        return UnityPy.load(b''.join(original(Path('assets/bin/Data') / (filename+'.split'+str(i))) for i in range(4)))
    return UnityPy.load(original(Path('assets/bin/Data') / filename))

def save(env, filename):
    if filename == 'globalgamemanagers.assets':
        raw = env.file.save()
        for i, start in enumerate(range(0, len(raw), 1048576)):
            (DATA/(filename+'.split'+str(i))).write_bytes(raw[start:start+1048576])
        return
    (DATA / filename).write_bytes(env.file.save())

def texture_copy(filename, old_filename):
    old = UnityPy.load(str(ROOT / 'branding/resources/assets/bin/Data' / old_filename))
    src = next(o.read() for o in old.objects if o.type.name == 'Texture2D')
    env = asset(filename)
    dst = next(o.read() for o in env.objects if o.type.name == 'Texture2D')
    if (src.m_Width, src.m_Height) != (dst.m_Width, dst.m_Height):
        image_texture(env, dst.object_reader.path_id, src.image, transparent=True)
        save(env, filename)
        return
    for field in ['image_data', 'm_TextureFormat', 'm_MipCount', 'm_CompleteImageSize']:
        setattr(dst, field, getattr(src, field))
    dst.save()
    save(env, filename)

def image_texture(env, path_id, image, transparent=False):
    dst = next(o.read() for o in env.objects if o.type.name == 'Texture2D' and o.path_id == path_id)
    size = (dst.m_Width, dst.m_Height)
    canvas = Image.new('RGBA', size, (0, 0, 0, 0 if transparent else 255))
    fitted = ImageOps.contain(image.convert('RGBA'), size, Image.Resampling.LANCZOS)
    canvas.alpha_composite(fitted, ((size[0]-fitted.width)//2, (size[1]-fitted.height)//2))
    dst.set_image(canvas, target_format=4, mipmap_count=1)
    dst.save()

def branding():
    compatibility=ROOT/'branding/compatibility'
    compiled=compatibility/'durango-br-shaders.bundle'
    metadata=json.loads((compatibility/'manifest.json').read_text('utf-8'))
    assert hashlib.sha256(compiled.read_bytes()).hexdigest()==metadata['sha256']
    assert hashlib.sha256((ROOT/'shaders/Floor2AlphaUV.shader').read_bytes()).hexdigest()==metadata['source_sha256']
    # New Unity 6 bundles use serialized format 23, unsupported by UnityPy 1.25.
    # Verify compiler provenance/hash here; native loading and GPU support are
    # checked by the bridge on the actual Android runtime before applying it.
    assert metadata['platform']=='Android' and metadata['shader']=='Durango/Building/Floor2AlphaUV'
    assert compiled.read_bytes().startswith(b'UnityFS\0')
    assert b'6000.6.0f1\0' in compiled.read_bytes()[:96]
    destination=SOURCE/'assets/newdawn/compatibility'; destination.mkdir(parents=True,exist_ok=True)
    shutil.copy2(compiled,destination/compiled.name)
    texture_copy('b90183040fb82f149b35865343c28b75', 'bcb37f4b3d27e4a4f93a3ec965e256dc')
    for filename in ['299925f2e19be844682e5c88eec948d1', '74700f1ec7cc3004cb0ee34675336b8f']:
        env=asset(filename)
        dst=next(o.read() for o in env.objects if o.type.name=='Texture2D')
        logo=Image.open(ROOT/'ui/logo-durango-brasil.png').convert('RGBA')
        logo=ImageOps.contain(logo,(646,254),Image.Resampling.LANCZOS)
        dst.set_image(logo,target_format=4,mipmap_count=1); dst.save(); save(env,filename)
    splash = Image.open(ROOT / 'branding/resources/res/drawable/unity_static_splash.png')
    for filename, ids in [('07c88ab73b26fed429ce5ae41e5bec29', [1]), ('globalgamemanagers.assets', [4])]:
        env = asset(filename)
        for path_id in ids: image_texture(env, path_id, splash)
        save(env, filename)
    logo = Image.open(ROOT / 'ui/logo-durango-brasil.png')
    env = asset('3822b4c60d9b7a5488e936d1bd7b1d68')
    image_texture(env, 1, logo, transparent=True)
    save(env, '3822b4c60d9b7a5488e936d1bd7b1d68')
    filename = '26ab981ac9145db41bcd977e4a762303'
    env = asset(filename)
    obj = next(o for o in env.objects if o.path_id == 3)
    raw = bytearray(obj.get_raw_data())
    positions = []
    for name, expected in [(b'bg_loading_ment_kr', (1953,157,92,35)), (b'bg_loading_ment_en', (2028,964,92,35))]:
        assert raw.count(name) == 1
        pos = (raw.index(name)+len(name)+3)//4*4
        assert struct.unpack_from('<4i',raw,pos) == expected
        positions.append(pos)
    struct.pack_into('<4i',raw,positions[1],*struct.unpack_from('<4i',raw,positions[0]))
    obj.set_raw_data(bytes(raw)); save(env,filename)
    previous = '© 2018 NEXON Korea Corp. & What! Studio. All Rights Reserved.'.encode()
    credit = legacy.TITLE_CREDITS.encode()
    assert (len(previous)-len(credit))%3 == 0
    payload = credit+b'[-]'*((len(previous)-len(credit))//3)
    for filename, path_id in [('2aa2c5f92ae103743be430366f7d4199',207),('3ec2435b610b59547af012777baa56d4',235)]:
        env=asset(filename); obj=next(o for o in env.objects if o.path_id==path_id)
        raw=bytearray(obj.get_raw_data()); assert raw.count(previous)==1
        position=raw.index(previous); assert position==220
        raw[position:position+len(previous)]=payload
        for offset,value in [(148,7),(156,72),(284,18)]: struct.pack_into('<i',raw,offset,value)
        obj.set_raw_data(bytes(raw)); save(env,filename)
    filename='f75f3fc847049f247886f165c4bf361f'
    env=asset(filename); text=next(o.read() for o in env.objects if o.type.name=='TextAsset')
    cfg=json.loads(text.m_Script)
    cfg['clusters']={'durango_brasil':{'gateway_url_root':legacy.GATEWAY,'name':dict.fromkeys(['pt_BR','en_US','ko_KR','th_TH'],'Durango Brasil')}}
    text.m_Script=json.dumps(cfg,ensure_ascii=False); text.save(); save(env,filename)
    target=SOURCE/'assets/newdawn/launcher/web'; target.mkdir(parents=True,exist_ok=True)
    for name in ['index.html','mobile.js','logo-durango-brasil.png']: shutil.copy2(ROOT/'ui'/name,target/name)
    shutil.copy2(ROOT/'branding/resources/res/drawable/unity_static_splash.png', target/'vision-force-splash.png')
    # PlayerSettings has a Unity 6 field absent from UnityPy's schema. Patch only
    # verified byte ranges and preserve the entire serialized allocation.
    filename='globalgamemanagers'; env=asset(filename)
    obj=next(o for o in env.objects if o.type.name=='PlayerSettings')
    raw=bytearray(obj.get_raw_data())
    assert len(raw)==920 and raw[108:110]==b'\x01\x01'
    assert struct.unpack_from('<I',raw,36)[0]==11 and raw[40:51]==b'NEXON Korea'
    assert struct.unpack_from('<I',raw,52)[0]==14 and raw[56:70]==b'Durango:Reborn'
    raw[36:52]=struct.pack('<I',12)+b'Vision Force'
    raw[56:70]=b'Durango Brasil'
    raw[108:110]=b'\x00\x00'
    obj.set_raw_data(bytes(raw)); save(env,filename)
    shutil.copy2(PROJECT/'DurangoBrasilApk/assets/Movie/Mobile/title.mp4',SOURCE/'assets/Movie/Mobile/title.mp4')

def native():
    relative=Path('lib/arm64-v8a/libdbr.so'); before=original(relative)
    assert hashlib.sha256(before).hexdigest()=='5fc10e14bb3b6f405816c22181aa88c70c06ff79df646114c54f9dad010bdf67'
    data=bytearray(before)
    # Skip the optional community copyright/footer feature at its original guard.
    # The branch shares the exact target used when this feature is disabled.
    from elftools.elf.elffile import ELFFile
    from io import BytesIO
    elf=ELFFile(BytesIO(before)); address=0x49ed58
    segment=next(s for s in elf.iter_segments() if s['p_type']=='PT_LOAD' and s['p_vaddr']<=address<s['p_vaddr']+s['p_filesz'])
    code_offset=segment['p_offset']+address-segment['p_vaddr']
    struct.pack_into('<I',data,code_offset,0x14000000|((0x49fff0-address)//4))
    # Community logo_tick scales the widget, independently of texture resolution.
    address=0x49f700
    segment=next(s for s in elf.iter_segments() if s['p_type']=='PT_LOAD' and s['p_vaddr']<=address<s['p_vaddr']+s['p_filesz'])
    code_offset=segment['p_offset']+address-segment['p_vaddr']
    assert struct.unpack_from('<I',data,code_offset)[0]==0x72a7fb88
    struct.pack_into('<I',data,code_offset,0x72a7e188)  # movk w8, #0x3f0c, lsl #16: 0.55 instead of 1.725

    from io import BytesIO
    encoded=BytesIO()
    logo_image=ImageOps.contain(Image.open(ROOT/'ui/logo-durango-brasil.png').convert('RGBA'),(646,254),Image.Resampling.LANCZOS)
    logo_image.save(encoded,format='PNG'); logo=encoded.getvalue()
    offset,size=0xedb0,930265
    assert data[offset:offset+8]==b'\x89PNG\r\n\x1a\n' and len(logo)<=size
    data[offset:offset+size]=logo.ljust(size,b'\0')
    menu=WORK/'menu-decoded/smali/com/durango/fanmod/ServerMenu.smali'
    text=menu.read_text('utf-8')
    text=legacy.replace_method(text,'setVisible(Landroid/app/Activity;Z)V','    .locals 0\n    return-void')
    menu.write_text(text,'utf-8')
    legacy.run(legacy.JAVA_BIN/'java.exe','-jar',ROOT/'work/apktool.jar','b',WORK/'menu-decoded','-o',WORK/'menu-rebuilt.apk')
    with zipfile.ZipFile(WORK/'menu-rebuilt.apk') as z: dex=z.read('classes.dex')
    assert len(dex)<=32448 and data[0x6e89:0x6e89+4]==b'dex\n'
    data[0x6e89:0x6e89+32448]=dex.ljust(32448,b'\0')
    assert len(data)==len(before)
    (SOURCE/relative).write_bytes(data)
    clang=legacy.SDK/'ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
    legacy.run(clang,'--target=aarch64-linux-android26','-shared','-fPIC','-O2','-Wall','-Wextra','-Werror',
               '-Wl,-z,max-page-size=16384','-Wl,-z,common-page-size=16384','-Wl,-soname,libdurangobr.so',
               ROOT/'native/unity6_auth_bridge.c','-ldl','-llog','-pthread','-o',SOURCE/'lib/arm64-v8a/libdurangobr.so')

def launcher():
    # Decode the existing Brazilian launcher, then retain only its classes in a second DEX.
    inspect=WORK/'launcher-source.apk'
    with zipfile.ZipFile(inspect,'w') as z:
        for name in ['AndroidManifest.xml','resources.arsc','classes.dex']: z.writestr(name,original(Path(name)))
        z.write(PROJECT/'DurangoBrasilApk/classes2.dex','classes2.dex')
    launcher_decoded=WORK/'launcher-decoded'
    legacy.run(legacy.JAVA_BIN/'java.exe','-jar',ROOT/'work/apktool.jar','d','-r','-f','-o',launcher_decoded,inspect)
    dest=DECODED/'smali_classes2/com/newdawn/launcher'
    dest.mkdir(parents=True,exist_ok=True)
    shutil.copytree(launcher_decoded/'smali_classes2/com/newdawn/launcher',dest,dirs_exist_ok=True)
    path=dest/'LauncherActivity.smali'; text=path.read_text('utf-8')
    # Unity 6 runs in this same process: the community launcher's delayed exit
    # would kill the game 1500 ms after the login activity finishes.
    text=legacy.replace_method(text,'lambda$onDestroy$3()V','    .locals 0\n    return-void')
    assert 'Ljava/lang/System;->exit(I)V' not in text
    text=text.replace('"com.unity3d.player.UnityPlayerActivity"','"com.newdawn.launcher.BrazilSplashActivity"')
    text=text.replace('"Durango Brasil Android alfa 3"','"Durango Brasil Unity 6 alfa 6"')
    # Preserve the token before the original smali reuses p1 for other intent fields.
    signature='.method private startGame(Ljava/lang/String;Ljava/lang/String;)V'
    start=text.index(signature); end=text.index('.end method',start)
    body=text[start:end]
    body=body.replace('    .locals 2','    .locals 3',1)
    body=body.replace('    :try_start_0\n','    :try_start_0\n    invoke-static {p0, v2}, Lcom/newdawn/launcher/BrazilUnityBridge;->prepareGame(Landroid/content/Context;Ljava/lang/String;)V\n',1)
    body=body.replace('    .line 544','    move-object v2, p1\n\n    .line 544',1)
    assert 'prepareGame' in body
    text=text[:start]+body+text[end:]; path.write_text(text,'utf-8')
    classes=WORK/'bridge-classes'; classes.mkdir(exist_ok=True)
    legacy.run(legacy.JAVA_BIN/'javac.exe','--release','8','-encoding','UTF-8','-cp',ROOT/'work/android.jar','-d',classes,
               ROOT/'src/com/newdawn/launcher/BrazilUnityBridge.java', ROOT/'src/com/newdawn/launcher/BrazilSplashActivity.java')
    newdex=WORK/'bridge-dex'; newdex.mkdir(exist_ok=True)
    legacy.run(legacy.JAVA_BIN/'java.exe','-cp',ROOT/'work/d8.jar','com.android.tools.r8.D8','--min-api','26','--lib',ROOT/'work/android.jar','--output',newdex,*classes.rglob('*.class'))
    inspect=WORK/'bridge.apk'
    with zipfile.ZipFile(inspect,'w') as z:
        for name in ['AndroidManifest.xml','resources.arsc']:z.writestr(name,original(Path(name)))
        z.write(newdex/'classes.dex','classes.dex')
    bridge_decoded=WORK/'bridge-decoded'
    legacy.run(legacy.JAVA_BIN/'java.exe','-jar',ROOT/'work/apktool.jar','d','-r','-f','-o',bridge_decoded,inspect)
    shutil.copy2(bridge_decoded/'smali/com/newdawn/launcher/BrazilUnityBridge.smali',dest/'BrazilUnityBridge.smali')
    for path in (bridge_decoded/'smali/com/newdawn/launcher').glob('BrazilSplashActivity*.smali'):
        shutil.copy2(path,dest/path.name)

def resources():
    ns='http://schemas.android.com/apk/res/android'; ET.register_namespace('android',ns)
    manifest=DECODED/'AndroidManifest.xml'; tree=ET.parse(manifest); root=tree.getroot()
    root.set('package','com.durangobrasil.apk')
    app=root.find('application'); game=app.find('activity'); game.set('{'+ns+'}exported','false')
    for child in list(game):
        if child.tag=='intent-filter':game.remove(child)
    key=lambda name:'{'+ns+'}'+name
    for meta in app.findall('meta-data'):
        if meta.get(key('name'))=='unity.splash-enable': meta.set(key('value'),'false')
    for child in list(app):
        if child.tag == 'activity' and child.get(key('name')) in ['com.newdawn.launcher.LauncherActivity','com.newdawn.launcher.BrazilSplashActivity']:
            app.remove(child)
    launcher=ET.SubElement(app,'activity',{key('name'):'com.newdawn.launcher.LauncherActivity',key('exported'):'true',key('hardwareAccelerated'):'true',key('screenOrientation'):'userLandscape',key('theme'):'@android:style/Theme.Material.NoActionBar.Fullscreen',key('configChanges'):game.get(key('configChanges')),key('resizeableActivity'):'false'})
    intent=ET.SubElement(launcher,'intent-filter')
    ET.SubElement(intent,'action',{key('name'):'android.intent.action.MAIN'})
    ET.SubElement(intent,'category',{key('name'):'android.intent.category.LAUNCHER'})
    ET.SubElement(app,'activity',{key('name'):'com.newdawn.launcher.BrazilSplashActivity',key('exported'):'false',key('screenOrientation'):'userLandscape',key('theme'):'@android:style/Theme.Material.NoActionBar.Fullscreen',key('configChanges'):game.get(key('configChanges')),key('resizeableActivity'):'false'})
    tree.write(manifest,encoding='utf-8',xml_declaration=True)
    yaml=DECODED/'apktool.yml'; text=yaml.read_text('utf-8')
    text=re.sub(r'versionCode: \d+','versionCode: 50204',text)
    text=re.sub(r'versionName: [^\n]+','versionName: 1.0-unity6-alfa6',text); yaml.write_text(text,'utf-8')
    icon=Image.open(PROJECT/'icon.png').convert('RGBA')
    for path in (DECODED/'res').rglob('*.png'):
        if path.name not in ['app_icon.png','ic_launcher_foreground.png','ic_launcher_background.png']:continue
        with Image.open(path) as old:size=old.size
        if path.name=='ic_launcher_background.png': result=Image.new('RGBA',size,(18,23,18,255))
        elif path.name=='ic_launcher_foreground.png':
            result=Image.new('RGBA',size); fit=ImageOps.contain(icon,(int(size[0]*.66),int(size[1]*.66)),Image.Resampling.LANCZOS)
            result.alpha_composite(fit,((size[0]-fit.width)//2,(size[1]-fit.height)//2))
        else:result=icon.resize(size,Image.Resampling.LANCZOS)
        result.save(path)
    rebuilt=WORK/'resources-rebuilt.apk'
    legacy.run(legacy.JAVA_BIN/'java.exe','-jar',ROOT/'work/apktool.jar','b',DECODED,'-o',rebuilt)
    with zipfile.ZipFile(rebuilt) as z:
        for name in z.namelist():
            if name.startswith('res/') or name in ['AndroidManifest.xml','resources.arsc','classes2.dex']:
                dest=SOURCE/name; dest.parent.mkdir(parents=True,exist_ok=True); dest.write_bytes(z.read(name))

def validate():
    assert hashlib.sha256((SOURCE/'lib/arm64-v8a/libil2cpp.so').read_bytes()).hexdigest()=='03092d5a0b76eb2490f815efb723340febd9a756469a563543ac1d156c876782'
    assert hashlib.sha256((SOURCE/'classes.dex').read_bytes()).hexdigest()=='a1ac8f378d67241e2d80c7e93dc29d3c3f93bd53c09bfcab557cb161e6999c57'
    for name in ['b90183040fb82f149b35865343c28b75','299925f2e19be844682e5c88eec948d1','globalgamemanagers.assets']:
        path=DATA/name
        env=UnityPy.load(str(path if path.exists() else Path(str(path)+'.split0'))); assert env.file.unity_version=='6000.6.3f1'
        for obj in env.objects:
            if obj.type.name=='Texture2D': assert obj.read().image.size
    records=[{'path':p.relative_to(BASE).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in BASE.rglob('*') if p.is_file() and p.name!='manifest.json']
    (BASE/'manifest.json').write_text(json.dumps(records,indent=2)+'\n','utf-8')

def main():
    # The full decoded tree is disposable; original files used for patches are preserved separately.
    for name in ['AndroidManifest.xml','resources.arsc','classes.dex']:original(Path(name))
    initialize(); branding(); native(); launcher(); resources(); validate()
    legacy.APK=SOURCE; legacy.OUTPUT_NAME='DurangoBrasil-unity6-alfa-6.apk'
    legacy.PACKAGE_EXCLUDES={'assets/newdawn/launcher/web/login-background.mp4','assets/Movie/PC/title.mp4'}
    # Unsupported 32-bit builds lack the community bridge and would bypass auth.
    arm32=SOURCE/'lib/armeabi-v7a'
    moved=WORK/'armeabi-v7a-original'
    if arm32.exists():shutil.move(str(arm32),str(moved))
    try:legacy.package()
    finally:
        if moved.exists():shutil.move(str(moved),str(arm32))

if __name__=='__main__':main()
