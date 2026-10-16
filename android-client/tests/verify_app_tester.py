"""Validate real APK payload integrity and execute the ARM64 tick wrapper with ASLR."""
import hashlib,importlib.util,io,json,struct,sys,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT.parent;WORK=ROOT/'work/app-tester-001'
sys.path[:0]=[str(ROOT/'work/tester-python-deps'),str(ROOT/'work/python-deps')]
from elftools.elf.elffile import ELFFile
from unicorn import Uc,UC_ARCH_ARM64,UC_MODE_ARM,UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_PC,UC_ARM64_REG_SP,UC_ARM64_REG_X0,UC_ARM64_REG_X1,UC_ARM64_REG_X19,UC_ARM64_REG_X20,UC_ARM64_REG_X29,UC_ARM64_REG_X30
spec=importlib.util.spec_from_file_location('base',PROJECT/'tools/inspect-tester-base.py');base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
def wrapper(raw,s):
    ef=ELFFile(io.BytesIO(raw));rel=ef.get_section_by_name('.rela.plt');st=ef.get_section(rel['sh_link']);plt=ef.get_section_by_name('.plt')
    targets={st.get_symbol(r['r_info_sym']).name:plt['sh_addr']+32+i*16 for i,r in enumerate(rel.iter_relocations())}
    for aslr in (0,0x7100000000):
        em=Uc(UC_ARCH_ARM64,UC_MODE_ARM)
        em.mem_map(aslr,0x700000);em.mem_map(0x80000000,0x20000)
        for segment in ef.iter_segments():
            if segment['p_type']=='PT_LOAD':em.mem_write(aslr+segment['p_vaddr'],segment.data())
        callback=aslr+0x600000;seen=[]
        def hook(uc,address,size,data):
            if address==aslr+targets['dlopen']:
                assert uc.reg_read(UC_ARM64_REG_X1)==6;seen.append('open');uc.reg_write(UC_ARM64_REG_X0,0x1234);uc.reg_write(UC_ARM64_REG_PC,uc.reg_read(UC_ARM64_REG_X30))
            elif address==aslr+targets['dlsym']:
                assert uc.reg_read(UC_ARM64_REG_X0)==0x1234;seen.append('resolve');uc.reg_write(UC_ARM64_REG_X0,callback);uc.reg_write(UC_ARM64_REG_PC,uc.reg_read(UC_ARM64_REG_X30))
            elif address==callback:
                seen.append('callback');uc.reg_write(UC_ARM64_REG_X0,0x999);uc.reg_write(UC_ARM64_REG_X1,0x999);uc.reg_write(UC_ARM64_REG_PC,uc.reg_read(UC_ARM64_REG_X30))
        em.hook_add(UC_HOOK_CODE,hook)
        values={UC_ARM64_REG_X0:0x101,UC_ARM64_REG_X1:0x202,UC_ARM64_REG_X19:0x1919,UC_ARM64_REG_X20:0x2020,UC_ARM64_REG_X29:0x2929,UC_ARM64_REG_X30:0x3030}
        for _ in range(2):
            for r,v in values.items():em.reg_write(r,v)
            em.reg_write(UC_ARM64_REG_SP,0x80010000)
            em.emu_start(aslr+s['tick']['address'],aslr+s['tick']['address']+16,count=300)
            assert em.reg_read(UC_ARM64_REG_PC)==aslr+s['tick']['address']+16
            assert em.reg_read(UC_ARM64_REG_SP)==0x80010000-160
            for r,v in values.items():assert em.reg_read(r)==v,(r,v)
        assert seen==['open','resolve','callback','callback'],seen
        assert struct.unpack('<Q',em.mem_read(aslr+s['show_notice_box.ok']['address'],8))[0]==callback
    return 'ARM64 wrapper passed at fixed and randomized load addresses; callback cached; register/stack state preserved.'
def main():
    report=json.loads((WORK/'build-report.json').read_text('utf-8'));apk=ROOT/'dist/losthorizon-app-tester-001.apk'
    assert hashlib.sha256(apk.read_bytes()).hexdigest()==report['sha256']
    with zipfile.ZipFile(PROJECT/'Durango-Brasil-v2.2-Lite.zip') as old,zipfile.ZipFile(apk) as new:
        changed=set(report['changed_files']);checked=0;actual=[]
        for entry in old.infolist():
            n=entry.filename
            if n.startswith(('META-INF/','lib/armeabi-v7a/')):continue
            if n in changed:actual.append(n);continue
            assert n in new.namelist(),n
            # Every untouched asset/library is checked, not just the main engine.
            assert hashlib.sha256(old.read(n)).digest()==hashlib.sha256(new.read(n)).digest(),n;checked+=1
        raw=new.read('lib/arm64-v8a/libdbr.so');s=base.elf(raw)['symbols']
        result=wrapper(raw,s)
        nx=s['nx_ctor'];assert raw[nx['offset']:nx['offset']+4]==bytes.fromhex('c0035fd6'), 'Nexus config constructor still active'
        for name in ('SERVERMENU_DEX','PHOTOUI_DEX','SESSION_DEX'):
            dex=s[name];data=raw[dex['offset']:dex['offset']+dex['size']];length=struct.unpack_from('<I',data,32)[0]
            assert hashlib.sha1(data[32:length]).digest()==data[12:32],name
            if name=='SERVERMENU_DEX':
                assert b'Nexus BR' not in data and b'jogo.revengerserverstudioficial.online' not in data, 'Nexus remains in Java server defaults'
        png=s['LOGO_PNG'];assert raw[png['offset']:png['offset']+len((PROJECT/'logo.png').read_bytes())]==(PROJECT/'logo.png').read_bytes()
        assert new.testzip() is None
        validation=dict(apk_sha256=report['sha256'],untouched_payloads_verified=checked,wrapper=result,embedded_dex_valid=True,embedded_logo_exact=True,zip_integrity=True,nexus_config_injection_disabled=True,nexus_java_defaults_removed=True)
    (WORK/'static-validation.json').write_text(json.dumps(validation,indent=2),'utf-8');print(json.dumps(validation,indent=2))
if __name__=='__main__':main()
