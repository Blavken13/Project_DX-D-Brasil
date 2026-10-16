"""Verify the signed update and its ARM64 character-selection regression paths."""
import hashlib,importlib.util,io,json,struct,subprocess,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent;WORK=ROOT/'work/app-tester-002'
sys.path[:0]=[str(ROOT),str(ROOT/'work/tester-python-deps'),str(ROOT/'work/python-deps')]
import build_app_tester as b
from PIL import Image,ImageOps
from elftools.elf.elffile import ELFFile
from unicorn import Uc,UC_ARCH_ARM64,UC_MODE_ARM,UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_PC,UC_ARM64_REG_SP,UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2,UC_ARM64_REG_X3,UC_ARM64_REG_X30
from verify_app_tester import wrapper

def selection_tests(raw):
    results=[]
    for slide in (0,0x7100000000):
        ef=ELFFile(io.BytesIO(raw));syms={s.name:s['st_value']+slide for s in ef.get_section_by_name('.symtab').iter_symbols() if s['st_value']}
        uc=Uc(UC_ARCH_ARM64,UC_MODE_ARM);uc.mem_map(slide,0x20000);uc.mem_map(0x80000000,0x40000)
        for seg in ef.iter_segments():
            if seg['p_type']=='PT_LOAD':uc.mem_write(slide+seg['p_vaddr'],seg.data())
        def put(name,value):uc.mem_write(syms[name],struct.pack('<Q',value))
        def integer(name,value):uc.mem_write(syms[name],struct.pack('<i',value))
        def pointer(at):return struct.unpack('<Q',uc.mem_read(at,8))[0]
        def number(name):return struct.unpack('<i',uc.mem_read(syms[name],4))[0]
        mocks={};handles={};state={'next':1,'valid':True};seen=[]
        for i,name in enumerate(('root_new','root_target','root_free','invoke','original_apply','original_recommended','original_updated')):
            address=0x80030000+i*16;mocks[address]=name;put(name,address)
        put('player_info',0x111);put('apply_choice',0x222)
        uc.mem_write(syms['token'],b'A'*43+b'\0')
        player=0x80020000;uc.mem_write(player+16,struct.pack('<i',8))
        other=0x80020100;uc.mem_write(other+16,struct.pack('<i',8))
        def hook(em,at,size,data):
            if at not in mocks:return
            name=mocks[at];a=em.reg_read(UC_ARM64_REG_X0);c=em.reg_read(UC_ARM64_REG_X1);d=em.reg_read(UC_ARM64_REG_X2)
            out=0
            if name=='root_new':out=state['next'];state['next']+=1;handles[out]=a
            elif name=='root_target':out=handles.get(a,0)
            elif name=='root_free':handles.pop(a,None)
            elif name=='original_apply':out=c;seen.append(('apply',c,d))
            elif name=='original_recommended':out=state.get('recommended',other);em.reg_write(UC_ARM64_REG_X1,0)
            elif name=='original_updated':seen.append(('updated',))
            elif name=='invoke':
                if a==0x111:out=0x80020200 if state['valid'] else 0
                elif a==0x222:
                    pair=pointer(d);chosen=pointer(pair);slot=struct.unpack('<i',em.mem_read(pair+8,4))[0]
                    seen.append(('restore',chosen,slot))
                else:raise AssertionError(('unexpected method',a))
                em.mem_write(em.reg_read(UC_ARM64_REG_X3),b'\0'*8)
            em.reg_write(UC_ARM64_REG_X0,out);em.reg_write(UC_ARM64_REG_PC,em.reg_read(UC_ARM64_REG_X30))
        # Android logging is irrelevant to the model; skip its PLT entry.
        rel=ef.get_section_by_name('.rela.plt');st=ef.get_section(rel['sh_link']);plt=ef.get_section_by_name('.plt')
        log_targets={slide+plt['sh_addr']+32+i*16 for i,r in enumerate(rel.iter_relocations()) if st.get_symbol(r['r_info_sym']).name=='__android_log_print'}
        def logger(em,at,size,data):
            if at in log_targets:em.reg_write(UC_ARM64_REG_X0,0);em.reg_write(UC_ARM64_REG_PC,em.reg_read(UC_ARM64_REG_X30))
        uc.hook_add(UC_HOOK_CODE,hook);uc.hook_add(UC_HOOK_CODE,logger)
        def run(name,*args):
            for reg,value in zip((UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X2,UC_ARM64_REG_X3),args):uc.reg_write(reg,value)
            uc.reg_write(UC_ARM64_REG_SP,0x80010000);uc.reg_write(UC_ARM64_REG_X30,0x8003fff0)
            uc.emu_start(syms[name],0x8003fff0,count=10000)
            assert uc.reg_read(UC_ARM64_REG_PC)==0x8003fff0,name
        for slot in (0,1,2):
            run('choose',0x80021000,player,slot,0)
            assert number('has_selection')==1 and number('selected_slot')==slot
            seen.clear();run('updated',0x80022000,0x80023000,0)
            assert seen==[('restore',player,slot),('updated',)],seen
        integer('recommending',1);run('choose',0x80021000,other,0,0);integer('recommending',0)
        assert number('selected_slot')==2,'default overwrote explicit selection'
        seen.clear();run('updated',0x80022000,0,0);assert seen==[('updated',)]
        state['valid']=False;seen.clear();run('updated',0x80022000,0x80023000,0)
        assert seen==[('updated',)] and number('has_selection')==0,'deleted character restored'
        state['valid']=True;run('choose',0x80021000,player,1,0);integer('loading',1)
        seen.clear();run('updated',0x80022000,0x80023000,0)
        assert seen==[('updated',)],'choice leaked into another authentication'
        uc.mem_write(player+16,struct.pack('<i',0));state['valid']=False
        seen.clear();run('updated',0x80022000,0x80023000,0)
        assert seen==[('restore',player,1),('updated',)],'new-character choice lost'
        results.append({'aslr':hex(slide),'scenarios':['slots 0/1/2','implicit default','null account','removed character','authentication reset','new character']})
    return results

def main():
    report=json.loads((WORK/'build-report.json').read_text('utf-8'));apk=Path(report['apk']);source=ROOT/'dist/losthorizon-app-tester-001.apk'
    assert hashlib.sha256(apk.read_bytes()).hexdigest()==report['sha256']
    assert hashlib.sha256(source.read_bytes()).hexdigest()==report['source_sha256']
    checked=0
    with zipfile.ZipFile(source) as old,zipfile.ZipFile(apk) as new:
        assert new.testzip() is None
        assert {n for n in old.namelist() if not n.startswith('META-INF/')}=={n for n in new.namelist() if not n.startswith('META-INF/')}
        for name in old.namelist():
            if name.startswith('META-INF/') or name in report['changed_files']:continue
            assert hashlib.sha256(old.read(name)).digest()==hashlib.sha256(new.read(name)).digest(),name;checked+=1
        icons=[n for n in new.namelist() if n.startswith('res/') and n.endswith('/app_icon.png')]
        assert icons
        for name in icons:
            actual=Image.open(io.BytesIO(new.read(name))).convert('RGBA')
            expected=ImageOps.fit(Image.open(PROJECT/'icon2.png').convert('RGBA'),actual.size,method=Image.Resampling.LANCZOS)
            assert actual.tobytes()==expected.tobytes(),name
        bridge=new.read('lib/arm64-v8a/libdbr.so');result=wrapper(bridge,b.mod.elf(bridge)['symbols'])
        native=selection_tests(new.read('lib/arm64-v8a/libtester.so'))
    badging=subprocess.check_output([str(b.BT/'aapt2.exe'),'dump','badging',str(apk)],text=True,encoding='utf-8')
    assert "versionCode='2'" in badging and "application-label:'Lost Horizon - Build tester'" in badging
    certs=[]
    for path in (source,apk):
        text=subprocess.check_output([str(b.JAVA/'java.exe'),'-jar',str(ROOT/'work/apksigner.jar'),'verify','--print-certs',str(path)],text=True)
        certs.append(next(l for l in text.splitlines() if 'certificate SHA-256 digest:' in l))
    assert certs[0]==certs[1]
    evidence=dict(sha256=report['sha256'],same_signer=True,untouched_payloads_verified=checked,icon_variants_exact=len(icons),app_name=b.APP_NAME if b.APP_NAME!='Aplicação Tester' else 'Lost Horizon - Build tester',wrapper=result,character_regressions=native)
    (WORK/'static-validation.json').write_text(json.dumps(evidence,indent=2),'utf-8');print(json.dumps(evidence,indent=2))

if __name__=='__main__':main()
