"""Native math/credential boundary tests and signed original-client regressions.

Tests do not claim to reproduce an Android driver, loader or Unity runtime.
"""
import ctypes
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ANDROID = ROOT / 'android-client'
WORK = ANDROID / 'work/compatibility-poco-x6'
sys.path.insert(0, str(ANDROID / 'work/python-deps'))
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN
from elftools.elf.elffile import ELFFile

def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)

def math_tests():
    ndk = Path(os.environ['LOCALAPPDATA']) / 'Android/Sdk/ndk/28.2.13676358/toolchains/llvm/prebuilt/windows-x86_64/bin'
    run(ndk/'clang.exe', '--target=x86_64-pc-windows-msvc', '-O2', '-ffreestanding', '-fno-stack-protector',
        '-c', ANDROID/'tests/runtime_math_test.c', '-o', WORK/'runtime-math-test.obj')
    run(ndk/'ld.lld.exe', '-flavor', 'link', '/dll', '/noentry', '/nodefaultlib',
        '/out:'+str(WORK/'runtime-math-test.dll'), WORK/'runtime-math-test.obj')
    dll=ctypes.CDLL(str(WORK/'runtime-math-test.dll'))
    dll.test_token.argtypes=[ctypes.c_char_p];dll.test_route.argtypes=[ctypes.c_char_p]
    valid=b'abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG'
    assert dll.test_token(valid)==1
    for ticket in [None,b'',b'a'*42,b'a'*44,valid[:-1]+b'!',b'a'*20+b'\0'+b'a'*22]:
        assert dll.test_token(ticket)==0
    gateway=b'http://179.197.72.129:8190'
    for path in [b'/accounts',b'/sessions',b'/accounts?version=5.2.1',b'/sessions?q=1']:
        assert dll.test_route(gateway+path)==1
    for url in [None,b'',b'http://',gateway+b'/players',gateway+b'/entry',gateway+b'/accounts/other',
                gateway+b'/sessions-extra',gateway+b'0/accounts',gateway+b'.evil/accounts',
                b'https://179.197.72.129:8190/accounts',b'http://example.org/accounts']:
        assert dll.test_route(url)==0
    p=ctypes.POINTER(ctypes.c_uint64)
    dll.test_span.argtypes=[ctypes.c_uint64,ctypes.c_uint64,ctypes.c_uint64,p,p]
    for page in [4096,16384,65536]:
        for address,length in [(0x100000,16),(0x100000+page-8,16),(0x100003,1),(0x100000+page-16,16)]:
            start=ctypes.c_uint64();span=ctypes.c_uint64()
            assert dll.test_span(address,length,page,ctypes.byref(start),ctypes.byref(span))==1
            assert start.value==address//page*page
            assert span.value==((address+length+page-1)//page*page)-start.value
    for address,length,page in [(1,16,2048),(1,16,5000),(1,0,4096),((1<<64)-4,16,4096)]:
        start=ctypes.c_uint64();span=ctypes.c_uint64()
        assert dll.test_span(address,length,page,ctypes.byref(start),ctypes.byref(span))==0
    dll.test_relocate.argtypes=[ctypes.c_uint32,ctypes.c_uint64,ctypes.POINTER(ctypes.c_uint32),p,ctypes.c_int]
    dis=Cs(CS_ARCH_ARM64,CS_MODE_LITTLE_ENDIAN)
    raw=(ROOT/'Durango original/lib/arm64-v8a/libil2cpp.so').read_bytes()
    native_source=(ANDROID/'native/original/runtime_compat.c').read_text('utf-8')
    for api in ['UnityEngine.Screen::get_width()', 'UnityEngine.Screen::get_height()',
                'UnityEngine.Screen::SetResolution(System.Int32,System.Int32,System.Boolean,System.Int32)',
                'UnityEngine.Application::set_targetFrameRate(System.Int32)']:
        assert api in native_source and api.encode()+b'\0' in raw
    for entry in [0x24e3d3c,0x24e3a94,0x2436120,0x24366c4,0x243a5e4,0x175de3c,0x157feb4,0x16f88ec]:
        for index in range(4):
            word=struct.unpack_from('<I',raw,entry+index*4)[0]
            pc=0x7000000000+entry+index*4
            output=ctypes.c_uint32();literal=ctypes.c_uint64()
            assert dll.test_relocate(word,pc,ctypes.byref(output),ctypes.byref(literal),index)==1
            original=next(dis.disasm(struct.pack('<I',word),pc))
            if original.mnemonic in ['adr','adrp']:
                assert literal.value==int(original.op_str.rsplit('#',1)[1],16)
                relocated=next(dis.disasm(struct.pack('<I',output.value),0x900000+index*4))
                assert relocated.mnemonic=='ldr'
                assert int(relocated.op_str.rsplit('#',1)[1],16)==0x900000+64+index*8
            else: assert output.value==word
    for word in [0x14000001,0x94000001,0x54000020,0xb4000020,0x36000020,0x58000020,0xd65f03c0]:
        output=ctypes.c_uint32();literal=ctypes.c_uint64()
        assert dll.test_relocate(word,0x10000,ctypes.byref(output),ctypes.byref(literal),0)==0
    print('PASS: own native token/origin guards; 4/16/64 KB boundaries; actual prologue ADRP relocation and branch rejection.')

def java_tests():
    java=Path('C:/Program Files/Android/Android Studio/jbr/bin')
    run(java/'javac.exe','-encoding','UTF-8','-d',WORK/'host-tests',
        ANDROID/'src/com/newdawn/launcher/TombstoneSummary.java',ANDROID/'tests/TombstoneSummaryTest.java')
    run(java/'java.exe','-cp',WORK/'host-tests','com.newdawn.launcher.TombstoneSummaryTest')

def apk_tests():
    client=ANDROID/'work/original-nexon/client'
    with zipfile.ZipFile(ANDROID/'dist/LostHorizon-alfa.apk') as apk:
        assert apk.read('classes.dex')==(ROOT/'Durango original/classes.dex').read_bytes()
        for name in ['libunity.so','libmain.so','libBlueDoveMediaRender.so']:
            path='lib/arm64-v8a/'+name
            assert apk.read(path)==(ROOT/'Durango original'/path).read_bytes()
        for name in ['libnd.so','libbr.so']:
            path='lib/arm64-v8a/'+name;elf=ELFFile(io.BytesIO(apk.read(path)))
            for segment in elf.iter_segments():
                if segment['p_type']=='PT_LOAD': assert segment['p_align']>=16384
                if segment['p_type']=='PT_GNU_RELRO':assert (segment['p_vaddr']+segment['p_memsz'])%16384==0
            assert apk.read(path)==(client/path).read_bytes()
        assert b'newdawn/seed/stamp.txt' not in apk.read('lib/arm64-v8a/libnd.so')
        assert b'Diagn\xc3\xb3stico' in apk.read('assets/durango-br/launcher/web/index.html')
    import xml.etree.ElementTree as ET
    manifest=ET.parse(ANDROID/'work/original-nexon/decoded/AndroidManifest.xml').getroot()
    key=lambda s:'{http://schemas.android.com/apk/res/android}'+s
    app=manifest.find('application')
    assert app.get(key('pageSizeCompat'))=='enabled'
    assert app.get(key('name'))=='com.newdawn.launcher.DiagnosticApplication'
    provider=next(x for x in app.findall('provider') if x.get(key('name'))=='com.newdawn.launcher.ReportProvider')
    assert provider.get(key('exported'))=='false' and provider.get(key('grantUriPermissions'))=='true'
    assert (ANDROID/'work/original-nexon/verification.json').is_file()
    print('PASS: signed APK retains original engine/media/game DEX; new bridges ELF/RELRO 16 KB; private report provider and compatibility manifest.')
    sys.path.insert(0,str(ANDROID))
    import runtime_settings_original as settings
    settings.verify(ROOT/'Durango original',client)
    print('PASS: one PlayerSettings byte disables MTRendering; all other settings and shader resources retained.')

if __name__=='__main__':
    WORK.mkdir(parents=True,exist_ok=True)
    math_tests();java_tests();apk_tests()
