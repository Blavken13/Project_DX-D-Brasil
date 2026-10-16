"""Icon-only update of the user-validated tester-002 payload."""
import hashlib,json,re,shutil,subprocess,zipfile
import build_app_tester as b
from PIL import Image,ImageOps

SOURCE=b.ROOT/'dist/losthorizon-build-tester-002.apk'
SOURCE_SHA256='22eb7b4ce354b1c55aa451481156ae66bca49bd4a6cef8a59149f7da1a66524d'
PREVIOUS=b.ROOT/'work/app-tester-002'
b.WORK=b.ROOT/'work/app-tester-003';b.MAIN=b.WORK/'main'
b.OUTPUT=b.ROOT/'dist/losthorizon-build-tester-003.apk';b.BASE=SOURCE
ICON=b.PROJECT/'icon2.png'

def verify():
    checked=0
    with zipfile.ZipFile(SOURCE) as old,zipfile.ZipFile(b.OUTPUT) as new:
        before={n for n in old.namelist() if not n.startswith('META-INF/')}
        after={n for n in new.namelist() if not n.startswith('META-INF/')}
        assert before==after,(before-after,after-before)
        changed={n for n in before if old.read(n)!=new.read(n)}
        assert all(n in ('AndroidManifest.xml','resources.arsc') or
                   (n.startswith('res/') and n.endswith(('/app_icon.png','/ic_launcher_background.png','/ic_launcher_foreground.png'))) for n in changed),changed
        for n in before-changed:
            assert hashlib.sha256(old.read(n)).digest()==hashlib.sha256(new.read(n)).digest();checked+=1
        icon=Image.open(ICON).convert('RGBA');icons=[]
        for n in new.namelist():
            if not n.startswith('res/') or not n.endswith(('/app_icon.png','/ic_launcher_background.png','/ic_launcher_foreground.png')):continue
            actual=Image.open(__import__('io').BytesIO(new.read(n))).convert('RGBA')
            expected=Image.new('RGBA',actual.size) if n.endswith('/ic_launcher_foreground.png') else ImageOps.fit(icon,actual.size,method=Image.Resampling.LANCZOS)
            assert actual.tobytes()==expected.tobytes(),n;icons.append(n)
        assert len(icons)==13,len(icons)
        assert new.testzip() is None
    badging=subprocess.check_output([str(b.BT/'aapt2.exe'),'dump','badging',str(b.OUTPUT)],text=True,encoding='utf-8')
    assert "versionCode='3'" in badging and "application-label:'Lost Horizon - Build tester'" in badging
    manifest=subprocess.check_output([str(b.BT/'aapt2.exe'),'dump','xmltree',str(b.OUTPUT),'--file','AndroidManifest.xml'],text=True,encoding='utf-8')
    assert 'android:icon' in manifest
    adaptive=subprocess.check_output([str(b.BT/'aapt2.exe'),'dump','xmltree',str(b.OUTPUT),'--file','res/mipmap-anydpi/app_icon.xml'],text=True,encoding='utf-8')
    assert 'adaptive-icon' in adaptive and 'background' in adaptive and 'foreground' in adaptive
    certs=[]
    for apk in (SOURCE,b.OUTPUT):
        out=subprocess.check_output([str(b.JAVA/'java.exe'),'-jar',str(b.ROOT/'work/apksigner.jar'),'verify','--print-certs',str(apk)],text=True)
        certs.append(next(l for l in out.splitlines() if 'certificate SHA-256 digest:' in l))
    assert certs[0]==certs[1]
    evidence=dict(apk=b.OUTPUT.name,sha256=hashlib.sha256(b.OUTPUT.read_bytes()).hexdigest(),
                  source_sha256=SOURCE_SHA256,same_signer=True,version_code=3,
                  icon_source_dimensions=list(icon.size),icon_variants_verified=icons,
                  adaptive_icon_verified=True,changed_files=sorted(changed),
                  unchanged_payloads_verified=checked,gameplay_and_dex_identical=True)
    (b.WORK/'static-validation.json').write_text(json.dumps(evidence,indent=2)+'\n','utf-8')
    print(json.dumps(evidence,indent=2),flush=True)

def main():
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==SOURCE_SHA256
    b.WORK.mkdir(exist_ok=True)
    if not b.MAIN.exists():shutil.copytree(PREVIOUS/'main',b.MAIN)
    with Image.open(ICON) as source:
        assert source.size==(512,512),source.size
        icon=source.convert('RGBA');records=[]
        for p in (b.MAIN/'res').rglob('*.png'):
            if p.name not in ('app_icon.png','ic_launcher_background.png','ic_launcher_foreground.png'):continue
            with Image.open(p) as current:size=current.size
            image=Image.new('RGBA',size) if p.name=='ic_launcher_foreground.png' else ImageOps.fit(icon,size,method=Image.Resampling.LANCZOS)
            image.save(p);records.append({'path':p.relative_to(b.MAIN).as_posix(),'size':list(size)})
    assert len(records)==13,len(records)
    config=b.MAIN/'apktool.yml';text=config.read_text('utf-8')
    text=re.sub(r'versionCode:.*','versionCode: 3',text);text=re.sub(r'versionName:.*','versionName: tester-003',text);config.write_text(text,'utf-8')
    rebuilt=b.WORK/'icon-patched.apk'
    b.run(b.JAVA/'java.exe','-jar',b.TOOL,'b','-p',b.WORK/'framework','-o',rebuilt,b.MAIN)
    with zipfile.ZipFile(rebuilt) as z:
        for n in z.namelist():
            if n in ('AndroidManifest.xml','resources.arsc') or n.startswith('res/'):b.changes[n]=z.read(n)
    b.audit.update(version_code=3,version_name='tester-003',app_name='Lost Horizon - Build tester',
                   source_sha256=SOURCE_SHA256,icon_sha256=hashlib.sha256(ICON.read_bytes()).hexdigest(),
                   icon_resources=records,change='Launcher legacy/adaptive icon from icon2.png; gameplay preserved')
    b.package();verify()

if __name__=='__main__':main()
