"""Verify authentication callbacks/ABI in the real Unity 2017 client and packaged fixes."""
import subprocess
import sys
import zipfile
from pathlib import Path
from verify_presentation import method_addresses

ROOT = Path(__file__).resolve().parents[2]
ANDROID = ROOT / 'android-client'
JAVA = Path('C:/Program Files/Android/Android Studio/jbr/bin')
WORK = ANDROID / 'work/mobile-stability-tests'

def main():
    WORK.mkdir(exist_ok=True)
    methods = {'CallCallback': 0x24e3a94, 'set_ConnectTimeout': 0x24e1c68,
               'set_Timeout': 0x24e1c70, 'set_DisableRetry': 0x24e1c38,
               'get_State': 0x24d4df0, 'get_Response': 0x24d4dd8}
    resolved = method_addresses({('BestHTTP', 'HTTPRequest'): set(methods)})
    for name, address in methods.items(): assert resolved[('HTTPRequest', name)] == address
    native = (ROOT / 'Durango original/lib/arm64-v8a/libil2cpp.so').read_bytes()
    assert native[0x24e3a94:0x24e3aa4] == bytes.fromhex('f657bda9f44f01a9fd7b02a9fd830091')
    # ARM64 setters store the single-register TimeSpan payload, or the bool byte.
    assert native[0x24e1c68:0x24e1c78] == bytes.fromhex('015800f9c0035fd6015c00f9c0035fd6')
    assert native[0x24e1c38:0x24e1c44] == bytes.fromhex('2800001208600139c0035fd6')
    sources = [ANDROID/'src/com/newdawn/launcher'/n for n in ['LegacyServicePolicy.java', 'AnrSummary.java']]
    subprocess.run([str(JAVA/'javac.exe'), '-encoding', 'UTF-8', '-d', str(WORK),
                    *map(str, sources), str(ANDROID/'tests/MobileStabilityTest.java')], check=True)
    subprocess.run([str(JAVA/'java.exe'), '-cp', str(WORK), 'com.newdawn.launcher.MobileStabilityTest'], check=True)
    with zipfile.ZipFile(ANDROID/'dist/LostHorizon-alfa.apk') as apk:
        assert apk.read('classes.dex') == (ROOT/'Durango original/classes.dex').read_bytes()
        dex = apk.read('classes2.dex')
        for marker in [b'LegacyServicePolicy', b'AnrSummary', b'LEGACY_AD_ID_SKIPPED']:
            assert marker in dex
        bridge = apk.read('lib/arm64-v8a/libnd.so')
        for marker in [b'SESSION_CALLBACK_ENTER', b'SESSION_CALLBACK_RETURN', b'SESSION_SEND_RETURN', b'set_Timeout']:
            assert marker in bridge
    print('PASS: original metadata method addresses, ARM64 timeout ABI/callback prologue and signed APK stability guards.')

if __name__ == '__main__': main()
