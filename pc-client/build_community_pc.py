"""Adapt the community PC client with Lost Horizon branding and authentication."""
from pathlib import Path
import sys,json,shutil,struct,hashlib,subprocess,os,zipfile,time
PROJECT=Path(__file__).resolve().parent.parent
SOURCE=PROJECT/'Durango-OffServer-Client-v3.1.9/Durango-OffServer'
OUTPUT=PROJECT/'Lost-Horizon-PC'
WORK=PROJECT/'pc-client/work/community-3.1.9'
sys.path.insert(0,str(PROJECT/'android-client/work/tester-python-deps'))
import UnityPy

def inspect():
    WORK.mkdir(parents=True,exist_ok=True)
    report=[]
    for filename in ['globalgamemanagers','globalgamemanagers.assets','resources.assets','sharedassets0.assets','sharedassets1.assets','sharedassets2.assets','sharedassets3.assets']:
        try:
            env=UnityPy.load(str(SOURCE/'DurangoV2_Data'/filename))
            entries=[]
            for o in env.objects:
                if o.type.name=='PlayerSettings':
                    tree=o.read_typetree(check_read=False); (WORK/'player-settings.json').write_text(json.dumps(tree,indent=2),encoding='utf-8'); entries.append(dict(id=o.path_id,type=o.type.name))
                elif o.type.name=='MonoBehaviour' and (b'NEXON' in o.get_raw_data() or b'bg_loading_ment_en' in o.get_raw_data()):
                    raw=o.get_raw_data(); entries.append(dict(id=o.path_id,type=o.type.name,name=o.peek_name(),size=len(raw),nexon=b'NEXON' in raw,atlas=b'bg_loading_ment_en' in raw)); (WORK/(filename+'-'+str(o.path_id)+'.bin')).write_bytes(raw)
                elif o.type.name=='Texture2D':
                    t=o.read()
                    if any(s in t.m_Name.lower() for s in ['logo','splash','nexon','what','atlas']):
                        entries.append(dict(id=o.path_id,type=o.type.name,name=t.m_Name,width=t.m_Width,height=t.m_Height,format=t.m_TextureFormat,stream=str(t.m_StreamData)))
                        if t.m_Name in ['pc_splash','_splash_ipad','logo_eng','offserver_transition_logo','Main_Atlas']: t.image.save(WORK/(t.m_Name+'.png'))
                elif o.type.name in ['GameObject','MonoBehaviour']:
                    name=o.peek_name() or ''
                    if any(s in name.lower() for s in ['copyright','credit','versioninfo','nexon','splash','atlas']):
                        entries.append(dict(id=o.path_id,type=o.type.name,name=name))
                        if o.type.name=='MonoBehaviour':
                            try: (WORK/(filename+'-'+str(o.path_id)+'.json')).write_text(json.dumps(o.read_typetree(),indent=2),encoding='utf-8')
                            except Exception: (WORK/(filename+'-'+str(o.path_id)+'.bin')).write_bytes(o.get_raw_data())
            report.append(dict(file=filename,objects=len(env.objects),entries=entries))
        except Exception as e: report.append(dict(file=filename,error=str(e)))
    (WORK/'asset-inventory.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))


from PIL import Image,ImageOps
from UnityPy.export.Texture2DConverter import image_to_texture2d
DATA=OUTPUT/'Lost Horizon Game_Data'
LOGO=PROJECT/'Durango-OffServer/Launcher/logo.png'
SPLASH=PROJECT/'credits_splash_nexon_what.png'
CREDITS='2016 VISION FORCE. Todos os direitos da Produtora\n*Cliente 5.2.1'
FRAMEWORK=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
GATEWAY='http://179.197.72.129:8190'
TEST_GATEWAY='http://127.0.0.1:8190'
SERVERS=[('Lost Horizon Brasil',GATEWAY),('Server Test',TEST_GATEWAY)]
GAME='Lost Horizon Game.exe'

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def run(*args):
    result=subprocess.run([str(a) for a in args],cwd=PROJECT,check=False,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    print(result.stdout.strip(),flush=True)
    result.check_returncode()
    return result.stdout

def fit(image,size,transparent=False):
    canvas=Image.new('RGBA',size,(0,0,0,0 if transparent else 255))
    scaled=ImageOps.contain(image.convert('RGBA'),size,Image.Resampling.LANCZOS)
    canvas.alpha_composite(scaled,((size[0]-scaled.width)//2,(size[1]-scaled.height)//2))
    return canvas

def own_objects(env,filename):
    return {o.path_id:o for o in env.objects if o.assets_file.name==filename}

def image_texture(obj,image,transparent=False):
    texture=obj.read()
    canvas=fit(image,(texture.m_Width,texture.m_Height),transparent)
    texture.set_image(canvas,target_format=4,mipmap_count=1);texture.save()
    return canvas

def save(env,filename):
    asset=next(f for f in env.files.values() if getattr(f,'name',None)==filename)
    (DATA/filename).write_bytes(asset.save())

def branding():
    logo=Image.open(LOGO);splash=Image.open(SPLASH)
    env=UnityPy.load(str(SOURCE/'DurangoV2_Data/resources.assets'));objects=own_objects(env,'resources.assets')
    for pid in [323,502]: image_texture(objects[pid],logo,True)
    image_texture(objects[230],splash)
    image_texture(objects[350],logo,True)
    for pid in [41593,42682]:
        raw=bytearray(objects[pid].get_raw_data());assert len(raw)==436
        assert b'NEXON Korea' in raw
        assert len(CREDITS.encode())==64
        struct.pack_into('<I',raw,216,64);raw[220:284]=CREDITS.encode()
        struct.pack_into('<i',raw,156,48);struct.pack_into('<i',raw,284,18)
        objects[pid].set_raw_data(bytes(raw))
    for pid in [10183,14459]:
        tree=objects[pid].read_typetree();assert tree['m_Name']=='VersionInfoLabel';tree['m_IsActive']=False;objects[pid].save_typetree(tree)
    atlas=objects[676].read();assert atlas.m_Name=='Main_Atlas' and atlas.m_TextureFormat==25 and atlas.m_MipCount==1
    atlasmeta=objects[46193].get_raw_data();canvas=atlas.image.convert('RGBA');rectangles={}
    for name in ['bg_loading_ment_en','bg_loading_ment_kr']:
        marker=name.encode();at=(atlasmeta.index(marker)+len(marker)+3)//4*4
        x,y,w,h=struct.unpack_from('<4i',atlasmeta,at);assert (w,h)==(92,35)
        canvas.paste(fit(logo,(w,h),True),(x,y));rectangles[name]=(x,y,w,h)
    encoded,fmt=image_to_texture2d(canvas,25);assert int(fmt)==25 and len(encoded)==atlas.m_StreamData.size
    stream=bytearray((DATA/'resources.assets.resS').read_bytes());blocks=set()
    for x,y,w,h in rectangles.values():
        for by in range((atlas.m_Height-y-h)//4,(atlas.m_Height-y+3)//4):
            for bx in range(x//4,(x+w+3)//4):blocks.add(by*(atlas.m_Width//4)+bx)
    for block in blocks:
        start=atlas.m_StreamData.offset+block*16;stream[start:start+16]=encoded[block*16:block*16+16]
    (DATA/'resources.assets.resS').write_bytes(stream)
    save(env,'resources.assets')
    env=UnityPy.load(str(SOURCE/'DurangoV2_Data/globalgamemanagers.assets'));objects=own_objects(env,'globalgamemanagers.assets');image_texture(objects[4],splash);save(env,'globalgamemanagers.assets')
    env=UnityPy.load(str(SOURCE/'DurangoV2_Data/sharedassets2.assets'));objects=own_objects(env,'sharedassets2.assets')
    for pid in [25,49]: image_texture(objects[pid],logo,True)
    save(env,'sharedassets2.assets')
    env=UnityPy.load(str(SOURCE/'DurangoV2_Data/globalgamemanagers'));objects=own_objects(env,'globalgamemanagers');obj=objects[1]
    raw=bytearray(obj.get_raw_data())
    product=b'Durango:Reborn';product_at=raw.index(product);after=(product_at+len(product)+3)//4*4
    # Only edit verified fields at the start of PlayerSettings; preserve newer opaque fields.
    logo_flag=after+12+8+16+1
    assert raw[logo_flag-1:logo_flag+3]==b'\x01\x01\x00\x00' and struct.unpack_from('<f',raw,logo_flag+3)[0]==1.0
    raw[logo_flag]=0
    for old,new in [(b'NEXON Korea',b'Vision Force'),(product,b'Lost Horizon')]:
        at=raw.index(old);assert struct.unpack_from('<I',raw,at-4)[0]==len(old)
        old_end=(at+len(old)+3)//4*4;replacement=struct.pack('<I',len(new))+new+b'\0'*((-len(new))%4)
        raw[at-4:old_end]=replacement
    obj.set_raw_data(bytes(raw));save(env,'globalgamemanagers')
    (DATA/'app.info').write_text('Vision Force\nLost Horizon',encoding='utf-8')
    return dict(loading_rectangles=rectangles,loading_blocks=sorted(blocks),credits=CREDITS)

def launcher():
    destination=OUTPUT/'Launcher';destination.mkdir(exist_ok=True)
    for name in ['login.xaml','logo.png','icon.ico','Start-LostHorizon.ps1']:
        shutil.copy2(PROJECT/'Durango-OffServer/Launcher'/name,destination/name)
    shutil.copy2(PROJECT/'pc-client/LauncherAuth.cs',destination/'LauncherAuth.cs')
    source=(PROJECT/'Durango-OffServer/DurangoLauncher.cs').read_text('utf-8').replace('DurangoV2.exe',GAME).replace('DurangoV2_Data/','Lost Horizon Game_Data/').replace('@"AppData\\LocalLow\\NEXON Korea\\Durango_ Wild Lands"','@"AppData\\LocalLow\\Vision Force\\Lost Horizon"')
    source=source.replace("        catch { video.Play(); throw; }","        catch (System.ComponentModel.Win32Exception error)\n        {\n            video.Play();\n            string detail = error.NativeErrorCode == 577 || error.NativeErrorCode == 1260 || error.NativeErrorCode == 4551\n                ? \"O Windows bloqueou o executável do jogo pelo Controle Inteligente de Aplicativos.\"\n                : \"Não foi possível iniciar o executável do jogo (código \" + error.NativeErrorCode + \").\";\n            throw new AuthFailure(detail, false);\n        }\n        catch { video.Play(); throw; }")
    (destination/'LostHorizonLauncher.cs').write_text(source,encoding='utf-8')
    fallback=(destination/'Start-LostHorizon.ps1').read_text('utf-8').replace("(Join-Path $taskClient 'DurangoLauncher.cs')","(Join-Path $PSScriptRoot 'LostHorizonLauncher.cs')")
    (destination/'Start-LostHorizon.ps1').write_text(fallback,encoding='utf-8')
    xaml=(destination/'login.xaml').read_text('utf-8').replace('Text="Lost Horizon · Alfa"','Text="2016 VISION FORCE. Todos os direitos da Produtora&#10;Lost Horizon · Alfa"')
    (destination/'login.xaml').write_text(xaml,encoding='utf-8')
    (OUTPUT/'Lost Horizon.cmd').write_text('@echo off\r\npowershell.exe -NoProfile -STA -File "%~dp0Launcher\\Start-LostHorizon.ps1" %*\r\n',encoding='ascii')
    references=[FRAMEWORK/name for name in ['System.dll','System.Core.dll','System.Security.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Xml.dll']]
    references += [FRAMEWORK/'WPF'/name for name in ['WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll']]
    run(FRAMEWORK/'csc.exe','/nologo','/platform:x64','/target:winexe','/out:'+str(OUTPUT/'Lost Horizon.exe'),'/win32icon:'+str(destination/'icon.ico'),*['/reference:'+str(p) for p in references],destination/'LostHorizonLauncher.cs',destination/'LauncherAuth.cs')
    run(FRAMEWORK/'csc.exe','/nologo','/target:library','/out:'+str(DATA/'Managed/LostHorizon.PC.dll'),PROJECT/'pc-client/CommunitySessionBridge.cs')
    run('dotnet','build',PROJECT/'pc-client/CommunityPatcher/CommunityPatcher.csproj','-c','Release','--no-restore','--nologo','-v','quiet')
    def linux(p):return '/mnt/'+p.drive[0].lower()+'/'+p.relative_to(p.anchor).as_posix()
    runtime=PROJECT/'Durango-CustomServer/server/bin/test-runtime-linux/dotnet';patched=WORK/'Assembly-CSharp.auth.dll'
    stdout=run('wsl','--distribution','Ubuntu-24.04','--exec',linux(runtime),linux(PROJECT/'pc-client/CommunityPatcher/bin/Release/net9.0/CommunityPatcher.dll'),linux(SOURCE/'DurangoV2_Data/Managed/Assembly-CSharp.dll'),linux(DATA/'Managed/LostHorizon.PC.dll'),linux(patched))
    shutil.copy2(patched,DATA/'Managed/Assembly-CSharp.dll')
    shutil.copy2(WORK/'LostHorizon.Localization.Overrides.dll',DATA/'Managed/LostHorizon.Localization.Overrides.dll')
    (DATA/'Managed/Durango.Localization.Overrides.dll').unlink()
    return json.loads(stdout.splitlines()[-1])

def build():
    resume='--resume' in sys.argv
    if OUTPUT.exists() and not resume:raise RuntimeError('Output already exists; use --verify or --resume for this generated output.')
    WORK.mkdir(parents=True,exist_ok=True)
    print('Copiando nova base Unity 6 para '+str(OUTPUT),flush=True)
    if not resume:shutil.copytree(SOURCE,OUTPUT,ignore=shutil.ignore_patterns('DurangoLauncher.exe','offserver.servers.cache.json','README.txt'))
    if not resume:(OUTPUT/'DurangoV2.exe').rename(OUTPUT/GAME)
    if not resume:(OUTPUT/'DurangoV2_Data').rename(DATA)
    lines=['# Lost Horizon Brasil','gateway='+GATEWAY,'name=Lost Horizon Brasil']+['server='+name+'|'+url for name,url in SERVERS]
    (OUTPUT/'offserver.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    (OUTPUT/'offserver.servers.cache.json').write_text(json.dumps({'servers':[{'name':name,'gateway':url} for name,url in SERVERS]}),encoding='utf-8')
    print('Aplicando logo, splash, loading e créditos...',flush=True)
    # Keep the exact community runtime bytes so OS reputation is not invalidated.
    shutil.copy2(SOURCE/'DurangoV2.exe',OUTPUT/GAME)
    report=branding()
    print('Integrando modal de login e sessão autenticada...',flush=True)
    report['assembly']=launcher()
    match_game_icon()
    (OUTPUT/'LEIA-ME.txt').write_text('Lost Horizon — Cliente PC baseado na comunidade 3.1.9\n\nAbra Lost Horizon.cmd para entrar ou cadastrar sua conta neste computador.\nLost Horizon.exe oferece o mesmo iniciador em ambientes que permitem o EXE personalizado.\nO login usa o mesmo servidor e as mesmas contas do projeto.\nServidor padrão: '+GATEWAY+'\nServidor local: Server Test ('+TEST_GATEWAY+')\n\nLost Horizon Game.exe é o runtime Unity chamado pelo iniciador.\nNão extraia atualizações da comunidade sobre esta distribuição.\n2016 VISION FORCE. Todos os direitos da Produtora\n',encoding='utf-8')
    (WORK/'build-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('Montagem concluída; verificando arquivos...',flush=True)
    verify()
    finalize()

def verify():
    report=json.loads((WORK/'build-report.json').read_text('utf-8'))
    env=UnityPy.load(str(DATA/'resources.assets'));objects=own_objects(env,'resources.assets');logo=Image.open(LOGO).convert('RGBA')
    for pid in [323,502]:assert objects[pid].read().image.convert('RGBA').tobytes()==logo.tobytes()
    for pid in [41593,42682]:assert objects[pid].get_raw_data()[220:284].decode()==CREDITS
    assert all(objects[pid].read_typetree()['m_IsActive']==False for pid in [10183,14459])
    for filename,allowed in [('resources.assets',{230,323,350,502,41593,42682,10183,14459}),('globalgamemanagers.assets',{4}),('sharedassets2.assets',{25,49}),('globalgamemanagers',{1})]:
        old=own_objects(UnityPy.load(str(SOURCE/'DurangoV2_Data'/filename)),filename);new=own_objects(UnityPy.load(str(DATA/filename)),filename)
        assert old.keys()==new.keys(),filename
        changed={pid for pid in old if old[pid].get_raw_data()!=new[pid].get_raw_data()}
        assert changed<=allowed,(filename,changed-allowed)
        report.setdefault('unity_objects',{})[filename]=sorted(changed)
    atlas=objects[676].read();start=atlas.m_StreamData.offset;end=start+atlas.m_StreamData.size
    a=(SOURCE/'DurangoV2_Data/resources.assets.resS').read_bytes();b=(DATA/'resources.assets.resS').read_bytes()
    assert len(a)==len(b) and a[:start]==b[:start] and a[end:]==b[end:]
    changed_blocks=[i for i in range(atlas.m_StreamData.size//16) if a[start+i*16:start+i*16+16]!=b[start+i*16:start+i*16+16]]
    assert set(changed_blocks)<=set(report['loading_blocks'])
    decoded=atlas.image.convert('RGBA')
    for name,(x,y,w,h) in report['loading_rectangles'].items():decoded.crop((x,y,x+w,y+h)).save(WORK/(name+'.png'))
    for name,pid in [('title-logo',323),('splash',230),('transition-logo',350)]:objects[pid].read().image.save(WORK/(name+'.png'))
    for filename,pid,label in [('globalgamemanagers.assets',4,'native-splash'),('sharedassets2.assets',25,'prologue-logo')]:
        own_objects(UnityPy.load(str(DATA/filename)),filename)[pid].read().image.save(WORK/(label+'.png'))
    ps=own_objects(UnityPy.load(str(DATA/'globalgamemanagers')),'globalgamemanagers')[1].read_typetree(check_read=False)
    assert ps['companyName']=='Vision Force' and ps['productName']=='Lost Horizon' and not ps['m_ShowUnitySplashLogo']
    assert (DATA/'app.info').read_text('utf-8')=='Vision Force\nLost Horizon'
    icon_report=WORK/'game-icon-validation.json'
    if icon_report.exists():
        icon=json.loads(icon_report.read_text('utf-8'))
        assert icon['after_sha256']==sha(OUTPUT/GAME) and icon['before_sha256']==sha(SOURCE/'DurangoV2.exe') and icon['code_sections_unchanged'],'Unverified runtime icon edit'
        report['runtime_icon']=icon
    else:
        assert sha(OUTPUT/GAME)==sha(SOURCE/'DurangoV2.exe'),'Unity runtime executable differs from the stock community binary'
    assert 'DurangoV2' not in (OUTPUT/'Launcher/LostHorizonLauncher.cs').read_text('utf-8')
    server_config=(OUTPUT/'offserver.txt').read_text('utf-8')
    assert GATEWAY in server_config and 'Server Test|'+TEST_GATEWAY in server_config
    exceptions={'DurangoLauncher.exe','offserver.servers.cache.json','offserver.txt','README.txt','DurangoV2.exe','DurangoV2_Data/resources.assets','DurangoV2_Data/resources.assets.resS','DurangoV2_Data/globalgamemanagers.assets','DurangoV2_Data/globalgamemanagers','DurangoV2_Data/sharedassets2.assets','DurangoV2_Data/app.info','DurangoV2_Data/Managed/Assembly-CSharp.dll','DurangoV2_Data/Managed/Durango.Localization.Overrides.dll'}
    checked=0
    for source in SOURCE.rglob('*'):
        if not source.is_file():continue
        relative=source.relative_to(SOURCE).as_posix()
        if relative in exceptions:continue
        target=OUTPUT/relative.replace('DurangoV2_Data/','Lost Horizon Game_Data/')
        assert target.is_file() and sha(source)==sha(target),relative;checked+=1
    report.update(untouched_files_verified=checked,loading_blocks_changed=len(changed_blocks),gateway=GATEWAY,entrypoint=str(OUTPUT/'Lost Horizon.exe'),base_version=(DATA/'version.txt').read_text().strip(),source_assembly_sha256=sha(SOURCE/'DurangoV2_Data/Managed/Assembly-CSharp.dll'),patched_assembly_sha256=sha(DATA/'Managed/Assembly-CSharp.dll'),verified=True)
    (WORK/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ['loading_blocks','loading_rectangles']},indent=2),flush=True)

def test_login():
    shell=Path(os.environ.get('WINDIR','C:/Windows'))/'System32/WindowsPowerShell/v1.0/powershell.exe'
    for scenario in ['valid','expired','bad-token','missing']:
        run(shell,'-NoProfile','-File',PROJECT/'pc-client/tests/test-community-bridge.ps1','-Scenario',scenario)
    run(shell,'-NoProfile','-STA','-File',PROJECT/'pc-client/tests/render-community-login.ps1','-Client',OUTPUT,'-Output',WORK)

def smoke():
    log=WORK/'unity-runtime-fix.log'
    evidence={'stock_runtime_exact':sha(OUTPUT/GAME)==sha(SOURCE/'DurangoV2.exe')}
    try:
        process=subprocess.Popen([str(OUTPUT/GAME),'-batchmode','-nographics','-quit','-logFile',str(log)],cwd=OUTPUT)
    except OSError as e:
        evidence.update(started=False,winerror=getattr(e,'winerror',None))
        (WORK/'unity-runtime-fix.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
        print(json.dumps(evidence));raise
    try:
        deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            text=log.read_text('utf-8',errors='replace') if log.exists() else ''
            if '[Sound] title event posted:' in text or process.poll() is not None:break
            time.sleep(0.5)
        failures=[marker for marker in ['FileNotFoundException','FileLoadException','TypeLoadException','MissingMethodException','MissingFieldException','BadImageFormatException'] if marker in text]
        evidence.update(started=True,engine_initialized='6000.6.3f1' in text,title_scene_loaded='[Sound] title event posted:' in text,assembly_errors=failures)
    finally:
        if process.poll() is None:process.terminate()
        process.wait(timeout=10)
    (WORK/'unity-runtime-fix.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print(json.dumps(evidence,indent=2))
    assert evidence['engine_initialized'] and not failures

def finalize():
    # Keep the project's Brazilian catalog available to Unity 6's language loader.
    shutil.copytree(PROJECT/'Durango-OffServer/Languages/pt_BR',OUTPUT/'Languages/pt_BR',dirs_exist_ok=True)
    (OUTPUT/'Languages/pt_BR/language.json').write_text(json.dumps({'name':'Português (Brasil)'},ensure_ascii=False),encoding='utf-8')
    report=json.loads((WORK/'validation.json').read_text('utf-8'))
    report['login_tests']=['23 authentication/session checks','valid bridge','expired bridge','invalid token','missing token','isolated gateways']
    report['login_layouts']=['login.png','register.png']
    report['runtime_sha256']=sha(OUTPUT/GAME)
    fix=WORK/'unity-runtime-fix.json'
    if fix.exists():report['runtime_policy_fix']=json.loads(fix.read_text('utf-8'))
    log=WORK/'unity-smoke.log'
    text=log.read_text('utf-8',errors='replace') if log.exists() else ''
    report['runtime_smoke']={'engine_initialized':'6000.6.3f1' in text,'title_scene_loaded':'[Sound] title event posted:' in text,'assembly_errors':any(x in text for x in ['FileNotFoundException','FileLoadException','TypeLoadException','MissingMethodException','MissingFieldException','BadImageFormatException']),'automatic_exit_completed':False,'termination':'60-second test timeout; only test process stopped'}
    (WORK/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('PASS: Portuguese catalog copied; test evidence saved')

def diagnose_gateway():
    import urllib.request,urllib.error
    for endpoint,body in [('/status',None),('/auth/login',b'username=__losthorizon_probe__&password=')]:
        request=urllib.request.Request(GATEWAY+endpoint,data=body,headers={'Accept':'application/json','Content-Type':'application/x-www-form-urlencoded','User-Agent':'LostHorizon/PC-alpha'})
        started=time.monotonic()
        try:response=urllib.request.urlopen(request,timeout=20)
        except urllib.error.HTTPError as e:response=e
        except Exception as e:
            print(json.dumps({'endpoint':endpoint,'connection_error':str(e)}));continue
        with response:
            raw=response.read();result={'endpoint':endpoint,'status':response.status,'seconds':round(time.monotonic()-started,2),'content_type':response.headers.get('Content-Type'),'content_encoding':response.headers.get('Content-Encoding')}
            try:
                payload=json.loads(raw);result['keys']=list(payload);result['error']=payload.get('error');result['ok']=payload.get('ok');result['version']=payload.get('version')
            except Exception:result['json_valid']=False;result['bytes']=len(raw)
            print(json.dumps(result))

def rebuild_launcher_only():
    destination=OUTPUT/'Launcher'
    references=[FRAMEWORK/name for name in ['System.dll','System.Core.dll','System.Security.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Xml.dll']]
    references += [FRAMEWORK/'WPF'/name for name in ['WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll']]
    run(FRAMEWORK/'csc.exe','/nologo','/platform:x64','/target:winexe','/out:'+str(OUTPUT/'Lost Horizon.exe'),'/win32icon:'+str(destination/'icon.ico'),*['/reference:'+str(p) for p in references],destination/'LostHorizonLauncher.cs',destination/'LauncherAuth.cs')
    print('PASS: launcher rebuilt with explicit Windows policy error')


def diagnose_executables():
    shell=Path(os.environ.get('WINDIR','C:/Windows'))/'System32/WindowsPowerShell/v1.0/powershell.exe'
    script=WORK/'inspect-signatures.ps1'
    script.write_text("param([string]$Source,[string]$Game,[string]$Launcher)\n$(foreach($taskPath in @($Source,$Game,$Launcher)) { $taskSig=Get-AuthenticodeSignature -FilePath $taskPath; [PSCustomObject]@{Path=$taskPath; Status=[string]$taskSig.Status; Signer=$taskSig.SignerCertificate.Subject} }) | ConvertTo-Json\n",encoding='utf-8')
    run(shell,'-NoProfile','-File',script,'-Source',SOURCE/'DurangoV2.exe','-Game',OUTPUT/GAME,'-Launcher',OUTPUT/'Lost Horizon.exe')

def match_game_icon():
    import ctypes
    from ctypes import wintypes
    launcher_exe=OUTPUT/'Lost Horizon.exe';game_exe=OUTPUT/GAME
    backup=WORK/'Lost Horizon Game.working-before-icon.exe'
    if not backup.exists():shutil.copy2(game_exe,backup)
    def sections(p):
        data=p.read_bytes();pe=struct.unpack_from('<I',data,60)[0];count=struct.unpack_from('<H',data,pe+6)[0];table=pe+24+struct.unpack_from('<H',data,pe+20)[0];result={}
        for i in range(count):
            at=table+i*40;name=data[at:at+8].rstrip(b'\0');size,start=struct.unpack_from('<II',data,at+16)
            if name!=b'.rsrc':result[name.decode()]=hashlib.sha256(data[start:start+size]).hexdigest()
        return result
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.LoadLibraryExW.argtypes=[wintypes.LPCWSTR,wintypes.HANDLE,wintypes.DWORD];kernel.LoadLibraryExW.restype=wintypes.HMODULE
    kernel.FreeLibrary.argtypes=[wintypes.HMODULE]
    name_callback=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HMODULE,ctypes.c_void_p,ctypes.c_void_p,wintypes.LPARAM)
    lang_callback=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HMODULE,ctypes.c_void_p,ctypes.c_void_p,wintypes.WORD,wintypes.LPARAM)
    kernel.EnumResourceNamesW.argtypes=[wintypes.HMODULE,ctypes.c_void_p,name_callback,wintypes.LPARAM]
    kernel.EnumResourceLanguagesW.argtypes=[wintypes.HMODULE,ctypes.c_void_p,ctypes.c_void_p,lang_callback,wintypes.LPARAM]
    kernel.FindResourceExW.argtypes=[wintypes.HMODULE,ctypes.c_void_p,ctypes.c_void_p,wintypes.WORD];kernel.FindResourceExW.restype=wintypes.HANDLE
    kernel.LoadResource.argtypes=[wintypes.HMODULE,wintypes.HANDLE];kernel.LoadResource.restype=wintypes.HANDLE
    kernel.SizeofResource.argtypes=[wintypes.HMODULE,wintypes.HANDLE];kernel.SizeofResource.restype=wintypes.DWORD
    kernel.LockResource.argtypes=[wintypes.HANDLE];kernel.LockResource.restype=ctypes.c_void_p
    kernel.BeginUpdateResourceW.argtypes=[wintypes.LPCWSTR,wintypes.BOOL];kernel.BeginUpdateResourceW.restype=wintypes.HANDLE
    kernel.UpdateResourceW.argtypes=[wintypes.HANDLE,ctypes.c_void_p,ctypes.c_void_p,wintypes.WORD,ctypes.c_void_p,wintypes.DWORD];kernel.UpdateResourceW.restype=wintypes.BOOL
    kernel.EndUpdateResourceW.argtypes=[wintypes.HANDLE,wintypes.BOOL];kernel.EndUpdateResourceW.restype=wintypes.BOOL
    def pointer(name):return name if isinstance(name,int) else ctypes.cast(ctypes.c_wchar_p(name),ctypes.c_void_p)
    def groups(handle):
        names=[]
        def named(h,t,n,e):names.append(n if n<65536 else ctypes.wstring_at(n));return True
        callback=name_callback(named);assert kernel.EnumResourceNamesW(handle,14,callback,0)
        result=[]
        for name in names:
            languages=[]
            def lang(h,t,n,l,e):languages.append(l);return True
            callback=lang_callback(lang);assert kernel.EnumResourceLanguagesW(handle,14,pointer(name),callback,0)
            result += [(name,l) for l in languages]
        return result
    def resource(handle,kind,name,lang):
        found=kernel.FindResourceExW(handle,kind,pointer(name),lang);assert found
        loaded=kernel.LoadResource(handle,found);assert loaded
        return ctypes.string_at(kernel.LockResource(loaded),kernel.SizeofResource(handle,found))
    source=kernel.LoadLibraryExW(str(launcher_exe),None,0x22);target=kernel.LoadLibraryExW(str(game_exe),None,0x22);assert source and target
    try:
        source_name,source_lang=groups(source)[0];group=resource(source,14,source_name,source_lang)
        count=struct.unpack_from('<H',group,4)[0];icons={}
        for i in range(count):
            identifier=struct.unpack_from('<H',group,6+14*i+12)[0];icons[identifier]=resource(source,3,identifier,source_lang)
        targets=groups(target)
    finally:kernel.FreeLibrary(source);kernel.FreeLibrary(target)
    before=sections(game_exe);update=kernel.BeginUpdateResourceW(str(game_exe),False);assert update
    success=False
    try:
        for name,lang in targets:
            for identifier,data in icons.items():
                buffer=ctypes.create_string_buffer(data);assert kernel.UpdateResourceW(update,3,identifier,lang,buffer,len(data))
            buffer=ctypes.create_string_buffer(group);assert kernel.UpdateResourceW(update,14,pointer(name),lang,buffer,len(group))
        success=True
    finally:assert kernel.EndUpdateResourceW(update,not success)
    assert sections(game_exe)==before,'Runtime code changed'
    target=kernel.LoadLibraryExW(str(game_exe),None,0x22);assert target
    try:
        for name,lang in targets:
            assert resource(target,14,name,lang)==group
            for identifier,data in icons.items():assert resource(target,3,identifier,lang)==data
    finally:kernel.FreeLibrary(target)
    evidence={'icon_source':str(launcher_exe),'icon_variants_exact':count,'groups_updated':len(targets),'code_sections_unchanged':True,'backup':str(backup),'before_sha256':sha(backup),'after_sha256':sha(game_exe)}
    (WORK/'game-icon-validation.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    print(json.dumps(evidence,indent=2))

def patch_discord_link():
    current=DATA/'Managed/Assembly-CSharp.dll';backup=WORK/('Assembly-CSharp.before-discord-'+sha(current)[:12]+'.dll')
    if not backup.exists():shutil.copy2(current,backup)
    run('dotnet','build',PROJECT/'pc-client/CommunityPatcher/CommunityPatcher.csproj','-c','Release','--no-restore','--nologo','-v','quiet')
    def linux(p):return '/mnt/'+p.drive[0].lower()+'/'+p.relative_to(p.anchor).as_posix()
    runtime=PROJECT/'Durango-CustomServer/server/bin/test-runtime-linux/dotnet';patched=WORK/'Assembly-CSharp.discord.dll'
    stdout=run('wsl','--distribution','Ubuntu-24.04','--exec',linux(runtime),linux(PROJECT/'pc-client/CommunityPatcher/bin/Release/net9.0/CommunityPatcher.dll'),'--discord-link',linux(current),linux(patched))
    evidence=json.loads(stdout.splitlines()[-1]);shutil.copy2(patched,current)
    evidence.update(backup=str(backup),sha256=sha(current))
    (WORK/'discord-link-validation.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')
    report_path=WORK/'build-report.json';report=json.loads(report_path.read_text('utf-8'));report['discord_link']=evidence
    report_path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('PASS: megaphone now opens Discord; all other methods preserved')

if __name__=='__main__':
    if '--discord-link' in sys.argv:patch_discord_link()
    elif '--match-game-icon' in sys.argv:match_game_icon()
    elif '--rebuild-launcher' in sys.argv:rebuild_launcher_only()
    elif '--diagnose-executables' in sys.argv:diagnose_executables()
    elif '--diagnose-gateway' in sys.argv:diagnose_gateway()
    elif '--finalize' in sys.argv:finalize()
    elif '--smoke' in sys.argv:smoke()
    elif '--test-login' in sys.argv:test_login()
    elif '--build' in sys.argv:build()
    elif '--verify' in sys.argv:
        verify();finalize()
    else:inspect()
