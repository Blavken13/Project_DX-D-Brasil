"""Reproducible, isolated adaptation of the SHA-pinned community Unity 6 APK."""
import argparse,hashlib,importlib.util,io,json,re,shutil,struct,subprocess,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent;PROJECT=ROOT.parent
WORK=ROOT/'work/app-tester-001';BASE=PROJECT/'Durango-Brasil-v2.2-Lite.zip'
sys.path[:0]=[str(ROOT/'work/tester-python-deps'),str(ROOT/'work/python-deps')]
sys.stdout.reconfigure(encoding='utf-8')
from PIL import Image,ImageOps
import UnityPy,capstone
spec=importlib.util.spec_from_file_location('baseline',PROJECT/'tools/inspect-tester-base.py');mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
JAVA=Path('C:/Program Files/Android/Android Studio/jbr/bin')
SDK=Path('C:/Users/Ander/AppData/Local/Android/Sdk');BT=SDK/'build-tools/36.0.0'
NDK=SDK/'ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin'
ANDROID=SDK/'platforms/android-36/android.jar'
TOOL=ROOT/'work/apktool.jar';MAIN=WORK/'main'
FOOTER='2016 VISION FORCE. Todos os direitos da Produtora\n*Cliente 5.2.1'
GATEWAY='http://179.197.72.129:8190'
OUTPUT=ROOT/'dist/losthorizon-app-tester-001.apk'
APP_NAME='Aplicação Tester';ICON=PROJECT/'icon.png';VERSION_CODE=1;VERSION_NAME='tester-001'
changes={};audit={}
def run(*args):
    subprocess.run([str(x) for x in args],check=True,cwd=PROJECT)
def replace_method(text,name,body):
    pattern=r'(^\.method [^\n]*\b'+re.escape(name)+r'\([^\n]*\n).*?^\.end method'
    out,count=re.subn(pattern,lambda m:m[1]+body+'\n.end method',text,flags=re.M|re.S)
    assert count==1,(name,count);return out
def branch(start,target):
    delta=target-start;assert delta%4==0 and -(1<<27)<=delta<(1<<27)
    return struct.pack('<I',0x14000000|((delta//4)&0x3ffffff))
def prepare_main():
    if not MAIN.exists():shutil.copytree(ROOT/'work/startup-community-50226/community-decoded',MAIN)
    manifest=MAIN/'AndroidManifest.xml';text=manifest.read_text('utf-8')
    text=text.replace('package="com.durango.offserver"','package="com.losthorizon.tester"')
    text=text.replace('android:scheme="durangooffserver"','android:scheme="losthorizontester"')
    manifest.write_text(text,'utf-8')
    yml=MAIN/'apktool.yml';t=yml.read_text('utf-8');t=re.sub(r'versionCode:.*',f"versionCode: {VERSION_CODE}",t);t=re.sub(r'versionName:.*',f"versionName: {VERSION_NAME}",t);yml.write_text(t,'utf-8')
    for p in (MAIN/'res').rglob('strings.xml'):
        t=p.read_text('utf-8');t=re.sub(r'(<string name="app_name">).*?(</string>)',lambda m:m[1]+APP_NAME+m[2],t);p.write_text(t,'utf-8')
    with Image.open(ICON) as source:
        icon=source.convert('RGBA')
        for p in (MAIN/'res').rglob('*.png'):
            if p.name not in ('app_icon.png','ic_launcher_background.png','ic_launcher_foreground.png'):continue
            with Image.open(p) as original:size=original.size
            # The launcher uses app_icon.xml on modern Android. Its background
            # must carry the photograph and its old Unity foreground must go.
            image=Image.new('RGBA',size) if p.name=='ic_launcher_foreground.png' else ImageOps.fit(icon,size,method=Image.Resampling.LANCZOS)
            image.save(p)
    p=MAIN/'smali/com/durango/offserver/DurangoActivity.smali'
    t=p.read_text('utf-8')
    needle='    invoke-super {p0, p1}, Lcom/unity3d/player/UnityPlayerActivity;->onCreate(Landroid/os/Bundle;)V'
    preparation='    invoke-static {p0}, Lcom/newdawn/launcher/TesterLogin;->prepare(Landroid/app/Activity;)V\n\n'
    if preparation not in t:t=t.replace(needle,preparation+needle)
    p.write_text(t,'utf-8')
    run(JAVA/'java.exe','-jar',TOOL,'b','-p',WORK/'framework','-o',WORK/'main-patched.apk',MAIN)
    with zipfile.ZipFile(WORK/'main-patched.apk') as z:
        for n in z.namelist():
            if n=='AndroidManifest.xml' or n=='resources.arsc' or n=='classes.dex' or n.startswith('res/'):changes[n]=z.read(n)
    classes=WORK/'java-classes';classes.mkdir(exist_ok=True)
    run(JAVA/'javac.exe','-encoding','UTF-8','-source','8','-target','8','-classpath',ANDROID,'-d',classes,ROOT/'src/com/newdawn/launcher/TesterLogin.java',ROOT/'src/com/newdawn/launcher/NewDawnApi.java')
    jar=WORK/'tester-java.jar';run(JAVA/'jar.exe','cf',jar,'-C',classes,'.')
    dex=WORK/'tester-dex';dex.mkdir(exist_ok=True)
    run(JAVA/'java.exe','-cp',BT/'lib/d8.jar','com.android.tools.r8.D8','--min-api','26','--lib',ANDROID,'--output',dex,jar)
    changes['classes2.dex']=(dex/'classes.dex').read_bytes()
def patch_embedded(raw,s):
    path=WORK/'embedded-ui-decoded/smali/com/durango/fanmod/ServerMenu.smali';t=path.read_text('utf-8')
    t=replace_method(t,'showInfo','    .locals 0\n    invoke-static {p0}, Lcom/newdawn/launcher/TesterLogin;->show(Landroid/app/Activity;)V\n    return-void')
    t=replace_method(t,'setVisible','    .locals 0\n    invoke-static {p0,p1}, Lcom/newdawn/launcher/TesterLogin;->setVisible(Landroid/app/Activity;Z)V\n    return-void')
    t=replace_method(t,'openDialog','    .locals 0\n    invoke-static {p0}, Lcom/newdawn/launcher/TesterLogin;->show(Landroid/app/Activity;)V\n    return-void')
    # One fixed endpoint. The original disk configuration and third-party chooser
    # cannot introduce a different server into the tester build.
    t=replace_method(t,'load',f'''    .locals 4
    new-instance v0, Ljava/util/ArrayList;
    invoke-direct {{v0}}, Ljava/util/ArrayList;-><init>()V
    new-instance v1, Lcom/durango/fanmod/ServerMenu$Entry;
    const-string v2, "Lost Horizon Brasil"
    const-string v3, "{GATEWAY}"
    invoke-direct {{v1,v2,v3}}, Lcom/durango/fanmod/ServerMenu$Entry;-><init>(Ljava/lang/String;Ljava/lang/String;)V
    invoke-interface {{v0,v1}}, Ljava/util/List;->add(Ljava/lang/Object;)Z
    return-object v0''')
    t=replace_method(t,'defaults','    .locals 1\n    invoke-static {p0}, Lcom/durango/fanmod/ServerMenu;->load(Landroid/app/Activity;)Ljava/util/List;\n    move-result-object v0\n    return-object v0')
    # Legacy community web/device binding is superseded by the gateway token.
    for name in ('isClusterWebAuth','isCurrentServerWebAuth'):t=replace_method(t,name,'    .locals 1\n    const/4 v0, 0x0\n    return v0')
    for name in ('startAuthPolling','pollImmediate','onServerSelected'):t=replace_method(t,name,'    .locals 0\n    return-void')
    path.write_text(t,'utf-8')
    run(JAVA/'java.exe','-jar',TOOL,'b','-p',WORK/'framework','-o',WORK/'embedded-patched.apk',WORK/'embedded-ui-decoded')
    with zipfile.ZipFile(WORK/'embedded-patched.apk') as z:dex=z.read('classes.dex')
    sym=s['SERVERMENU_DEX'];assert len(dex)<=sym['size'],(len(dex),sym['size'])
    raw[sym['offset']:sym['offset']+sym['size']]=dex+b'\0'*(sym['size']-len(dex));audit['embedded_menu_dex_bytes']=len(dex)
def patch_bridge():
    raw=bytearray((WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes());d=mod.elf(raw);s=d['symbols']
    old=b'http://201.18.211.139:8290';new=GATEWAY.encode();assert len(new)<=len(old)
    end=s['SERVERMENU_DEX']['offset']
    raw[:end]=raw[:end].replace(old+b'\0',new+b'\0'*(len(old)+1-len(new)))
    patch_embedded(raw,s)
    png=(PROJECT/'logo.png').read_bytes();v=s['LOGO_PNG'];assert len(png)<=v['size']
    raw[v['offset']:v['offset']+v['size']]=png+b'\0'*(v['size']-len(png));audit['bridge_logo_bytes']=len(png)
    # Preserve the native photo compositor, which uses this same LOGO_PNG.
    # Replace copyright strings, including the bridge's later runtime overwrite.
    for match in list(re.finditer(rb'[^\x00]{1,700}\x00',bytes(raw[:51769]))):
        content=match.group()[:-1]
        if b'2018 NEXON' in content and len(content)>=len(FOOTER.encode()):
            replacement=FOOTER.encode();raw[match.start():match.end()]=replacement+b'\0'*(len(content)+1-len(replacement))
        elif content.startswith(b'  |  Durango Brasil'):
            raw[match.start():match.end()]=b'\0'*(len(content)+1)
    tick=s['tick'];notice=s['show_notice_box'];cave=notice['address']+16
    raw[notice['offset']:notice['offset']+4]=branch(notice['address'],s['mf_fan_notice']['address'])
    # Community account/device gating must not supersede our server login.
    nx=s['nx_accounts_decision'];raw[nx['offset']:nx['offset']+8]=bytes.fromhex('00008052c0035fd6')
    # nx_ctor appends Nexus to offserver.txt every time libdbr is loaded.
    # Disable that constructor's body, preserving the other startup constructors.
    nx_ctor=s['nx_ctor'];assert raw[nx_ctor['offset']:nx_ctor['offset']+4]==bytes.fromhex('fd7bbca9')
    raw[nx_ctor['offset']:nx_ctor['offset']+4]=bytes.fromhex('c0035fd6')
    audit['nexus_config_injection_disabled']=True
    # Reuse the removed notice's callback slots; no ELF segments or engine ABI change.
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    original=bytes(raw[tick['offset']:tick['offset']+16]);assert all(i.mnemonic=='stp' for i in dis.disasm(original,tick['address']))
    asm=f'''.text
.global _start
_start:
stp x29,x30,[sp,#-64]!
stp x19,x20,[sp,#16]
stp x0,x1,[sp,#32]
adrp x19,cache_ptr
add x19,x19,:lo12:cache_ptr
ldr x20,[x19]
cbnz x20,invoke_callback
adr x0,library_name
mov w1,#6
bl dbr_dlopen
cbz x0,skip_callback
adr x1,callback_name
bl dbr_dlsym
mov x20,x0
str x20,[x19]
cbz x20,skip_callback
invoke_callback:
blr x20
skip_callback:
ldp x0,x1,[sp,#32]
ldp x19,x20,[sp,#16]
ldp x29,x30,[sp],#64
.byte {','.join(str(x) for x in original)}
b tick_rest
library_name: .asciz "libtester.so"
callback_name: .asciz "tester_before_tick"
'''
    source=WORK/'tick-wrapper.S';source.write_text(asm,'ascii');obj=WORK/'tick-wrapper.o';exe=WORK/'tick-wrapper.elf';binary=WORK/'tick-wrapper.bin'
    # Resolve PLT targets from ELF relocation metadata, never from another APK.
    from elftools.elf.elffile import ELFFile
    ef=ELFFile(io.BytesIO(raw));rela=ef.get_section_by_name('.rela.plt');symtab=ef.get_section(rela['sh_link']);plt=ef.get_section_by_name('.plt')
    targets={symtab.get_symbol(r['r_info_sym']).name:plt['sh_addr']+32+i*16 for i,r in enumerate(rela.iter_relocations())}
    run(NDK/'clang.exe','--target=aarch64-linux-android26','-c',source,'-o',obj)
    run(NDK/'ld.lld.exe','-Ttext='+hex(cave),'--defsym=cache_ptr='+hex(s['show_notice_box.ok']['address']),'--defsym=dbr_dlopen='+hex(targets['dlopen']),'--defsym=dbr_dlsym='+hex(targets['dlsym']),'--defsym=tick_rest='+hex(tick['address']+16),obj,'-o',exe)
    run(NDK/'llvm-objcopy.exe','-O','binary','--only-section=.text',exe,binary)
    wrapper=binary.read_bytes();assert len(wrapper)<notice['size']-16
    raw[notice['offset']+16:notice['offset']+16+len(wrapper)]=wrapper
    raw[tick['offset']:tick['offset']+4]=branch(tick['address'],cave)
    audit['tick_wrapper']=[f'{i.address:#x} {i.mnemonic} {i.op_str}' for i in dis.disasm(wrapper[:104],cave)]
    (WORK/'tick-wrapper-disassembly.json').write_text(json.dumps(audit['tick_wrapper'],indent=2),'utf-8')
    changes['lib/arm64-v8a/libdbr.so']=bytes(raw)
    header=WORK/'app_tester_offsets.h';header.write_text(f'#define DBR_BLOB {s["thpt_blob"]["address"]:#x}u\n#define DBR_HOOK {s["hook_code"]["address"]:#x}u\n','ascii')
    library=WORK/'libtester.so'
    run(NDK/'clang.exe','--target=aarch64-linux-android26','-shared','-fPIC','-O2','-Wall','-Wextra','-Werror','-Wl,-z,max-page-size=16384','-Wl,-z,common-page-size=16384','-I',WORK,ROOT/'native/app_tester.c','-o',library,'-ldl','-llog')
    changes['lib/arm64-v8a/libtester.so']=library.read_bytes()
def read_asset(z,path):
    if path in z.namelist():return z.read(path)
    chunks=[];i=0
    while path+f'.split{i}' in z.namelist():chunks.append(z.read(path+f'.split{i}'));i+=1
    assert chunks,path;return b''.join(chunks)
def write_asset(z,path,raw):
    if path in z.namelist():changes[path]=raw;return
    names=[];i=0
    while path+f'.split{i}' in z.namelist():names.append(path+f'.split{i}');i+=1
    assert names,path
    # Retain the original split boundaries; only the last chunk can change size.
    offset=0
    for i,n in enumerate(names):
        end=offset+z.getinfo(n).file_size if i<len(names)-1 else len(raw)
        changes[n]=raw[offset:end];offset=end
def patch_assets(z):
    current=zipfile.ZipFile(ROOT/'dist/LostHorizon-login-no-jogo-50226.apk')
    logo=Image.open(PROJECT/'logo.png').convert('RGBA')
    for name in ('299925f2e19be844682e5c88eec948d1','74700f1ec7cc3004cb0ee34675336b8f','3822b4c60d9b7a5488e936d1bd7b1d68','sharedassets2.assets'):
        path='assets/bin/Data/'+name;env=UnityPy.load(read_asset(z,path))
        for o in env.objects:
            if o.type.name!='Texture2D':continue
            t=o.read()
            if name=='sharedassets2.assets' and t.m_Name not in ('img_logo_en','img_logo'):continue
            image=Image.new('RGBA',(t.m_Width,t.m_Height));fit=ImageOps.contain(logo,image.size,Image.Resampling.LANCZOS);image.paste(fit,((image.width-fit.width)//2,(image.height-fit.height)//2));t.m_MipCount=1;t.set_image(image,4);t.save()
        write_asset(z,path,env.file.save())
    # Exact loading atlas artwork already approved and used by build 50226.
    path='assets/bin/Data/b90183040fb82f149b35865343c28b75';env=UnityPy.load(z.read(path));old=UnityPy.load(current.read('assets/bin/Data/bcb37f4b3d27e4a4f93a3ec965e256dc'))
    tex=next(o.read() for o in old.objects if o.type.name=='Texture2D')
    for o in env.objects:
        if o.type.name=='Texture2D':
            t=o.read();assert (t.m_Width,t.m_Height)==(tex.m_Width,tex.m_Height)
            t.m_MipCount=1;t.set_image(tex.image,4);t.save()
    changes[path]=env.file.save()
    for name in ('2aa2c5f92ae103743be430366f7d4199','3ec2435b610b59547af012777baa56d4'):
        path='assets/bin/Data/'+name;env=UnityPy.load(z.read(path))
        for o in env.objects:
            if o.type.name!='MonoBehaviour':continue
            raw=o.get_raw_data();found=raw.find(b'2018 NEXON')
            if found<0:continue
            idx=220;oldlen=struct.unpack_from('<I',raw,idx-4)[0];assert oldlen<500 and b'2018 NEXON' in raw[idx:idx+oldlen]
            payload=FOOTER.encode();data=raw[:idx-4]+struct.pack('<I',len(payload))+payload+b'\0'*(-len(payload)%4)+raw[idx+oldlen+(-oldlen%4):];o.set_raw_data(data)
        changes[path]=env.file.save()
    path='assets/bin/Data/f75f3fc847049f247886f165c4bf361f';env=UnityPy.load(z.read(path))
    for o in env.objects:
        if o.type.name=='TextAsset':
            t=o.read();cfg=json.loads(t.m_Script);cfg['clusters']={'os_'+GATEWAY:{'gateway_url_root':GATEWAY,'name':{'en_US':'Lost Horizon Brasil','ko_KR':'Lost Horizon Brasil'}}};t.m_Script=json.dumps(cfg);t.save()
    changes[path]=env.file.save();current.close()
    # Both the native startup video and all Unity/WWise media stay byte identical.
    config='enabled=1\nname=Lost Horizon Brasil\ngateway='+GATEWAY+'\nserver=Lost Horizon Brasil|'+GATEWAY+'\n'
    changes['assets/offserver-config.txt']=config.encode()
def package():
    unsigned=WORK/'tester-unsigned.apk';aligned=WORK/'tester-aligned.apk'
    with zipfile.ZipFile(BASE) as z,zipfile.ZipFile(unsigned,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as out:
        original=set(z.namelist())
        for info in z.infolist():
            n=info.filename
            if n.startswith('META-INF/') or n.startswith('lib/armeabi-v7a/'):continue
            payload=changes.get(n)
            if payload is None:payload=z.read(n)
            # Media that openFd reads must remain uncompressed.
            compression=zipfile.ZIP_STORED if n.lower().endswith(('.mp4','.ogg','.wem','.bnk')) else info.compress_type
            out.writestr(n,payload,compress_type=compression)
        for n,payload in changes.items():
            if n not in original:out.writestr(n,payload)
    run(ROOT/'work/zipalign.exe','-P','16','-f','4',unsigned,aligned)
    run(JAVA/'java.exe','-jar',ROOT/'work/apksigner.jar','sign','--ks',ROOT/'durango-br-test.jks','--ks-pass','file:'+str(ROOT/'durango-br-test.pass'),'--v4-signing-enabled','false','--out',OUTPUT,aligned)
    run(JAVA/'java.exe','-jar',ROOT/'work/apksigner.jar','verify','--verbose',OUTPUT)
    run(ROOT/'work/zipalign.exe','-c','-P','16','4',OUTPUT)
    digest=hashlib.sha256(OUTPUT.read_bytes()).hexdigest();OUTPUT.with_suffix('.apk.sha256').write_text(digest+'  '+OUTPUT.name+'\n','ascii')
    audit.update(apk=str(OUTPUT),sha256=digest,bytes=OUTPUT.stat().st_size,changed_files={n:hashlib.sha256(b).hexdigest() for n,b in changes.items()})
    with zipfile.ZipFile(BASE) as a,zipfile.ZipFile(OUTPUT) as b:
        for n in ('lib/arm64-v8a/libunity.so','lib/arm64-v8a/libmain.so','lib/arm64-v8a/libil2cpp.so','assets/bin/Data/boot.config'):
            assert a.read(n)==b.read(n),n
        videos=[n for n in a.namelist() if n.lower().endswith('.mp4')]
        for n in videos:assert a.read(n)==b.read(n),n
        audit['preserved_videos']=videos;audit['engine_identical']=True
    (WORK/'build-report.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps({k:audit[k] for k in ('apk','sha256','bytes','engine_identical')},indent=2),flush=True)
def main():
    WORK.mkdir(exist_ok=True);OUTPUT.parent.mkdir(exist_ok=True)
    assert hashlib.sha256(BASE.read_bytes()).hexdigest()=='080b8cac71e807987398056303eaca91a119304d8dcfcee8466c84b1a92306e1'
    prepare_main();patch_bridge()
    with zipfile.ZipFile(BASE) as z:patch_assets(z)
    cache=WORK/'patched-files';cache.mkdir(exist_ok=True)
    for n,b in changes.items():p=cache/n;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
    package()
if __name__=='__main__':main()
