"""Inspect editable UI/assets and native startup paths of the tester baseline."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import zipfile
sys.stdout.reconfigure(encoding='utf-8')

ROOT=Path(__file__).resolve().parent
PROJECT=ROOT.parent
WORK=ROOT/'work/app-tester-001'
sys.path[:0]=[str(ROOT/'work/tester-python-deps'),str(ROOT/'work/python-deps'),str(PROJECT/'tools')]
from PIL import Image
import UnityPy
import capstone
spec=importlib.util.spec_from_file_location('inspect_tester_base',PROJECT/'tools/inspect-tester-base.py')
inspection=importlib.util.module_from_spec(spec);spec.loader.exec_module(inspection)
elf=inspection.elf


def assets():
    result=[]
    images=WORK/'artwork';images.mkdir(exist_ok=True)
    with zipfile.ZipFile(PROJECT/'Durango-Brasil-v2.2-Lite.zip') as z:
        names=[n for n in z.namelist() if n.startswith('assets/bin/Data/') and '/' not in n[len('assets/bin/Data/'):] and not n.endswith(('.split1','.split2','.split3','.split4','.split5','.split6','.split7','.split8','.split9'))]
        for name in names:
            if name.endswith('.split0'):
                name=name[:-7];parts=[];i=0
                while name+f'.split{i}' in z.namelist():parts.append(z.read(name+f'.split{i}'));i+=1
                raw=b''.join(parts)
            else:raw=z.read(name)
            try:env=UnityPy.load(raw)
            except Exception as error:
                result.append(dict(file=name,error=type(error).__name__+': '+str(error)[:200]));continue
            selected=[]
            for o in env.objects:
                if o.type.name not in ('Texture2D','TextAsset','MonoBehaviour','PlayerSettings'):continue
                if o.type.name=='MonoBehaviour':
                    data=o.get_raw_data()
                    matches=[s for s in ('NEXON','Durango Brasil','Vision Force','copyright','VersionInfoLabel','bg_loading_ment','gateway_url_root') if s.encode() in data]
                    if matches:selected.append(dict(id=o.path_id,type=o.type.name,bytes=len(data),matches=matches))
                    continue
                try:
                    value=o.read()
                    n=getattr(value,'m_Name','')
                    row=dict(id=o.path_id,type=o.type.name,name=n,bytes=o.byte_size)
                    if o.type.name=='Texture2D':
                        row.update(width=value.m_Width,height=value.m_Height,format=value.m_TextureFormat,mips=value.m_MipCount)
                        if re.search('logo|loading|splash',n,re.I) or name.rsplit('/',1)[-1] in ('b90183040fb82f149b35865343c28b75','299925f2e19be844682e5c88eec948d1','74700f1ec7cc3004cb0ee34675336b8f','3822b4c60d9b7a5488e936d1bd7b1d68'):
                            image=value.image;target=images/(name.rsplit('/',1)[-1]+f'-{o.path_id}.png');image.save(target);row['preview']=str(target)
                    elif o.type.name=='TextAsset':
                        content=value.m_Script
                        if 'gateway_url_root' in content:row['text']=content
                    selected.append(row)
                except Exception as error:selected.append(dict(id=o.path_id,type=o.type.name,error=str(error)[:150]))
            if selected:result.append(dict(file=name,unity=str(getattr(env.file,'unity_version','')),objects=selected))
    (WORK/'assets-inspection.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(json.dumps([x for x in result if 'error' in x or any('preview' in o or 'text' in o or 'matches' in o for o in x.get('objects',[]))],ensure_ascii=False,indent=2))


def summary():
    result=json.loads((WORK/'assets-inspection.json').read_text('utf-8'))
    for f in result:
        rows=[o for o in f.get('objects',[]) if 'preview' in o or 'matches' in o or 'text' in o]
        if rows: print(json.dumps(dict(file=f['file'],objects=rows),ensure_ascii=True))
    details=json.loads((WORK/'dbr-elf.json').read_text('utf-8'))
    syms=details['symbols']
    print(json.dumps({k:v for k,v in syms.items() if re.search(r'notice|popup|version|account|hook_ra|init|send|orig|ctor',k,re.I) and v['type']==2}))
    rows=json.loads((WORK/'native-disassembly.json').read_text('utf-8'))
    for k in ('driver_start','hook_ra','hook_ra0','hook_ra1','nx_accounts_decision','hook_send_request'):print(k+'\n'+'\n'.join(rows.get(k,[])))

def integration():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();d=elf(raw);s=d['symbols']
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    for name in ('mf_fan_notice','show_notice_box','sm_load','hb_init','hook_send_request'):
        v=s[name];print(name+'\n'+'\n'.join(f'{i.address:#x} {i.mnemonic} {i.op_str}' for i in dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address'])))
    print('VARS '+json.dumps({k:v for k,v in s.items() if v['type']==1 and re.search(r'orig|o_|request|gateway|notice|ra_|ph_|png|dex|thpt',k,re.I)}))
    p=WORK/'embedded-ui-decoded/smali/com/durango/fanmod/ServerMenu.smali';src=p.read_text('utf-8')
    for name in ('defaults','showInfo','show','setVisible','getWebAuthStatus','findEntry'):
        print('\n'+name+'\n'+re.search(r'^\.method [^\n]*\b'+name+r'\(.*?^\.end method',src,re.M|re.S).group())

def pointers():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();d=elf(raw);s=d['symbols']
    print(json.dumps({k:v for k,v in s.items() if v['type']==1 and re.search(r'orig|o_|request|gateway|notice|ra_|ph_|png|dex|thpt',k,re.I)},indent=2))
    rows=json.loads((WORK/'native-disassembly.json').read_text('utf-8'))
    print('SEND\n'+'\n'.join(rows['hook_send_request']))
    with zipfile.ZipFile(ROOT/'dist/LostHorizon-login-no-jogo-50226.apk') as z:
        for n in ('6827ab7f4ebc56143b5702d0d1b8abc6','7d9842174a415534b91e990f357c7f29'):
            e=UnityPy.load(z.read('assets/bin/Data/'+n))
            for o in e.objects:
                r=o.get_raw_data()
                if b'VISION' in r or b'Vision' in r: print('CURRENT FOOTER '+repr(r))

def startup():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();d=elf(raw);s=d['symbols']
    rows=json.loads((WORK/'native-disassembly.json').read_text('utf-8'))
    print('\n'.join(rows['tick'][:40]))
    for k in ('tick','g_vm','g_sm_cls','thpt_blob','g_orig_send','g_orig_ra','show_notice_box.ok','show_notice_box.hide','get_env','sm_load'):
        print(k,s.get(k))
    for offset in (0x5288,0x6b0b,0x8fad,0x55a0,0x9674): print(hex(offset),repr(raw[offset:raw.index(b'\0',offset)]))
    for line in rows['tick']:
        if '#0x59d5e8' in line or '#0x5a17b0' in line:print('NOTICECALL',line)
    print('HOOK\n'+'\n'.join(rows['hook_code']))
def paths():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();d=elf(raw)
    for offset in (0x9645,0xa1e3,0x49c7,0x85e5):print(hex(offset),repr(raw[offset:raw.index(b'\0',offset)]))
    for m in re.finditer(rb'[\x20-\x7e]{5,}\x00',raw[:51769]):
        if any(v in m.group().lower() for v in (b'.log',b'.txt',b'trace',b'sigsegv',b'com.durango.offserver',b'offserver.txt',b'checksum',b'crash')):print(hex(m.start()),m.group()[:-1].decode())
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    for name in ('diag_crash','diag_open'):
        v=d['symbols'][name];print(name+'\n'+'\n'.join(f'{i.address:#x} {i.mnemonic} {i.op_str}' for i in dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address'])))
    print(json.dumps({k:v for k,v in d['symbols'].items() if re.search(r'diag|signal|crash|trace',k,re.I) and v['type']==2}))
def fault():
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    for path,pc in ((WORK/'libtester.so',0x5a9c),(WORK/'base/lib/arm64-v8a/libil2cpp.so',0x2005fb0)):
        raw=path.read_bytes();d=elf(raw)
        near={k:v for k,v in d['symbols'].items() if v['type']==2 and v['address']<=pc<v['address']+v['size']}
        print(path.name,json.dumps(near))
        for v in near.values():
            start=max(v['address'],pc-60);offset=v['offset']+start-v['address']
            print('\n'.join(f'{i.address:#x} {i.mnemonic} {i.op_str}' for i in dis.disasm(raw[offset:offset+160],start)))


def servers():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();d=elf(raw)
    literals={}
    for m in re.finditer(rb'[^\x00]{4,700}\x00',raw[:51769]):
        if any(v in m.group().lower() for v in (b'nexus',b'revenger',b'offserver',b'clusters',b'servers.json',b'multiservers')):
            literals[m.start()]=m.group()[:-1].decode('utf-8',errors='replace');print(hex(m.start()),literals[m.start()])
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    v=d['symbols']['tick'];rows=list(dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address']))
    for j,i in enumerate(rows):
        if i.mnemonic!='adrp':continue
        reg,page=i.op_str.split(', #');page=int(page,16)
        for q in rows[j+1:j+5]:
            if q.mnemonic=='add' and q.op_str.startswith(reg+', '+reg+', #'):
                offset=page+int(q.op_str.split('#')[1],16)
                if offset in literals:
                    print('REFERENCE',hex(i.address),repr(literals[offset]),'\n'+'\n'.join(f'{x.address:#x} {x.mnemonic} {x.op_str}' for x in rows[max(0,j-5):j+12]))
    for name,v in d['symbols'].items():
        if v['type']!=2 or name=='tick':continue
        rows=list(dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address']))
        for j,i in enumerate(rows):
            if i.mnemonic!='adrp':continue
            reg,page=i.op_str.split(', #');page=int(page,16)
            for q in rows[j+1:j+5]:
                if q.mnemonic=='add' and q.op_str.startswith(reg+', '+reg+', #'):
                    offset=page+int(q.op_str.split('#')[1],16)
                    if offset in (0x4064,0x4555):
                        print('SERVER_FUNCTION',name,hex(i.address),'\n'+'\n'.join(f'{x.address:#x} {x.mnemonic} {x.op_str}' for x in (rows if len(rows)<400 else rows[max(0,j-15):j+20])))

def decode():
    report=json.loads((WORK/'inspection.json').read_text('utf-8'))
    mini=WORK/'embedded-ui.apk'
    with zipfile.ZipFile(PROJECT/'Durango-Brasil-v2.2-Lite.zip') as z, zipfile.ZipFile(mini,'w') as out:
        for name in ('AndroidManifest.xml','resources.arsc'):out.writestr(name,z.read(name))
        for i,item in enumerate(report['embedded_dex']):out.write(item['path'],'classes'+(str(i+1) if i else '')+'.dex')
    target=WORK/'embedded-ui-decoded'
    if target.exists():raise ValueError('Decode output exists')
    subprocess.run(['C:/Program Files/Android/Android Studio/jbr/bin/java.exe','-jar',str(ROOT/'work/apktool.jar'),'d','-r','-p',str(WORK/'framework'),'-o',str(target),str(mini)],check=True)

def selection():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();s=elf(raw)['symbols']
    print(json.dumps({k:v for k,v in s.items() if v['type']==2 and re.search(r'autoenter|ae_',k,re.I)},indent=2))
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    for name in [k for k,v in s.items() if v['type']==2 and re.search(r'^ae_(start|confirm|pick|enter)',k,re.I)]:
        if name not in s:continue
        v=s[name];print(name+'\n'+'\n'.join(f'{i.address:#x} {i.mnemonic} {i.op_str}' for i in dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address'])))
    for name,v in s.items():
        if v['type']!=2 or not v['size']:continue
        rows=list(dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address']))
        for j,i in enumerate(rows):
            if i.mnemonic=='add' and i.op_str.endswith('#0x522'):
                print('RECOMMENDED '+name+'\n'+'\n'.join(f'{q.address:#x} {q.mnemonic} {q.op_str}' for q in rows[max(0,j-20):j+85]))
    for m in re.finditer(rb'[\x20-\x7e]{4,}\x00',raw[:51769]):
        if re.search(rb'recommend|selected|entity|session|entry|Apply|PlayerId',m.group(),re.I):print(hex(m.start()),m.group()[:-1].decode())


def native():
    raw=(WORK/'base/lib/arm64-v8a/libdbr.so').read_bytes();details=elf(raw)
    dis=capstone.Cs(capstone.CS_ARCH_ARM64,capstone.CS_MODE_LITTLE_ENDIAN)
    result={}
    for n,v in details['symbols'].items():
        if v['type']!=2 or n not in ('driver_start','hook_code','tick','sm_load','ph_ctor','nx_ctor','nx_accounts_decision','hook_ra','hook_ra0','hook_ra1','hook_on_confirm','hook_send_request','photo_drawlogo_hook','photo_save_hook','photo_open_hook','hb_init','sm_is_web_auth_active'):continue
        rows=[f'{i.address:#x}: {i.mnemonic} {i.op_str}' for i in dis.disasm(raw[v['offset']:v['offset']+v['size']],v['address'])]
        result[n]=rows
    (WORK/'native-disassembly.json').write_text(json.dumps(result,indent=2)+'\n','utf-8')
    print(json.dumps(dict(function_lengths={n:len(v) for n,v in result.items()},variables={n:v for n,v in details['symbols'].items() if re.search(r'tick\.(frames|last)|copyright|logo_tick|version|modal|popup|original|orig_init|init_ptr|send_ptr|o_send|o_ra|o_init|SERVERMENU|LOGO|SPLASH',n,re.I)}),indent=2))


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=('assets','decode','native','summary','integration','pointers','startup','paths','fault','servers','selection'));args=parser.parse_args()
    globals()[args.mode]()
