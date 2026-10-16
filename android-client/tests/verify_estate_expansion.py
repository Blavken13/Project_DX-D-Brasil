"""Verify the real estate click, request ABI and signed APK's scoped native edit."""
import json
import os
from pathlib import Path
import sys
import zipfile
from verify_presentation import method_addresses

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'android-client'))
import estate_original as estate
import presentation_original as presentation
from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN

SOURCE = ROOT / 'Durango original'
CLIENT = ROOT / 'android-client/work/original-nexon/client'
APK = Path(os.environ.get('LH_TEST_APK', ROOT / 'android-client/dist/LostHorizon-alfa-enclave1-50217.apk'))


def main():
    addresses = method_addresses({('Durango.UI', 'EstateGridGroup'):
        {'OnExpandEstateClick', 'ExpandEstate', 'ExpandPersonalEstate'},
        ('', 'EstateSystem'): {'ExpandEstate'}})
    assert addresses[('EstateGridGroup', 'OnExpandEstateClick')] == estate.CLICK_METHOD
    assert addresses[('EstateGridGroup', 'ExpandEstate')] == estate.PAID_METHOD
    assert addresses[('EstateGridGroup', 'ExpandPersonalEstate')] == estate.FREE_METHOD
    native = (SOURCE / estate.NATIVE).read_bytes()
    dis = Cs(CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN)
    redirect = list(dis.disasm(estate.branch(estate.FREE_METHOD), estate.PAID_CALL))
    assert len(redirect) == 1 and redirect[0].mnemonic == 'bl'
    assert int(redirect[0].op_str[1:], 16) == estate.FREE_METHOD
    click = list(dis.disasm(native[estate.CLICK_METHOD:estate.FREE_METHOD], estate.CLICK_METHOD))
    # Both paths use the same instance/Point2/MethodInfo ABI and caller epilogue.
    assert any(i.mnemonic == 'cmp' and i.op_str == 'w8, #4' for i in click)
    assert any(i.mnemonic == 'bl' and i.op_str == '#0x139bf80' for i in click)
    request = list(dis.disasm(native[estate.FREE_METHOD:estate.PAID_METHOD], estate.FREE_METHOD))
    assert any(i.address == 0x139c014 and i.mnemonic == 'b.ge' for i in request), 'Native size guard missing'
    assert any(i.address == 0x139c050 and i.op_str == 'w2, #4' for i in request), 'Tile-to-cell divisor changed'
    assert any(i.address == 0x139c0ec and i.mnemonic == 'bl' and i.op_str == '#0x18047e4'
               for i in request), 'Native ExpandEstate send missing'
    assert addresses[('EstateSystem', 'ExpandEstate')] == 0x18047e4
    # The zero-maintenance guard in the old dialog is exactly the silent failure
    # reported by mobile testers; the modified click no longer reaches it.
    dialog = list(dis.disasm(native[0x139c33c:0x139c344], 0x139c33c))
    assert [(i.mnemonic, i.op_str) for i in dialog] == [('cmp', 'x26, #1'), ('b.lt', '#0x139cba8')]
    report = estate.verify(SOURCE, CLIENT)
    presentation.verify(SOURCE, CLIENT)  # Exact whole-file native byte allowlist.
    with zipfile.ZipFile(APK) as archive:
        assert archive.read(estate.NATIVE.as_posix()) == (CLIENT / estate.NATIVE).read_bytes()
        assert archive.read('classes.dex') == (SOURCE / 'classes.dex').read_bytes()
    damaged = bytearray(native)
    damaged[estate.PAID_CALL] ^= 1
    try:
        estate.patch(damaged)
    except AssertionError:
        pass
    else:
        raise AssertionError('Unsupported native build must be rejected')
    print('PASS: original IL2CPP method addresses, click routing, instance/Point2 ABI, size guard,')
    print('      native ExpandEstate request/callback, signed APK and all unrelated native bytes preserved.')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
