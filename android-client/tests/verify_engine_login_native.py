"""Execute the shipped ARM64 authentication gate with mocked managed/HTTP APIs."""
import json
from pathlib import Path
import struct
import sys
import zipfile
ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT), str(ROOT / 'work/python-deps')]
from engine_login_original import elf_symbols
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3, UC_ARM64_REG_X30, UC_ARM64_REG_SP, UC_ARM64_REG_PC


def verify():
    with zipfile.ZipFile(ROOT / 'dist/LostHorizon-login-no-jogo-50226.apk') as z:
        raw = z.read('lib/arm64-v8a/libenginelogin.so')
    symbols = elf_symbols(raw)
    u = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
    phoff, shoff = struct.unpack_from('<QQ', raw, 32)
    phsize, phnum, shsize, shnum = struct.unpack_from('<HHHH', raw, 54)
    segments = [struct.unpack_from('<IIQQQQQQ', raw, phoff + i * phsize) for i in range(phnum)]
    segments = [s for s in segments if s[0] == 1]
    end = max(s[3] + s[6] for s in segments)
    u.mem_map(0, (end + 0xffff) & ~0xffff)
    for s in segments: u.mem_write(s[3], raw[s[2]:s[2] + s[5]])
    u.mem_map(0x10000000, 0x10000); u.mem_map(0x20000000, 0x10000); u.mem_map(0x30000000, 0x100000)
    next_stub, next_object = 0x10000100, 0x30000100
    handlers, methods, roots, events = {}, {}, {}, []
    registers = [UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3]
    def args(): return [u.reg_read(r) for r in registers]
    def qword(p): return struct.unpack('<Q', u.mem_read(p, 8))[0]
    def put(p, n): u.mem_write(p, struct.pack('<Q', n))
    def text(p):
        return bytes(u.mem_read(p, 256)).split(b'\0')[0].decode()
    def allocate(value=b''):
        nonlocal next_object
        p = next_object; next_object += max(512, len(value) + 16)
        if value: u.mem_write(p, value)
        return p
    def stub(handler):
        nonlocal next_stub
        p = next_stub; next_stub += 16
        handlers[p] = handler; u.mem_write(p, bytes.fromhex('c0035fd6')); return p
    def global_set(name, value):
        address, size = symbols[name]; u.mem_write(address, value.to_bytes(size, 'little'))
    def global_get(name):
        address, size = symbols[name]; return int.from_bytes(u.mem_read(address, size), 'little')
    def return_hook(uc, address, size, data):
        if address in handlers:
            value = handlers[address](*args())
            u.reg_write(UC_ARM64_REG_X0, value or 0); u.reg_write(UC_ARM64_REG_PC, u.reg_read(UC_ARM64_REG_X30))
    u.hook_add(UC_HOOK_CODE, return_hook)
    def rooted(obj, *_):
        key = max(roots, default=0) + 1; roots[key] = obj; return key
    def invoke(m, receiver, parameters, exception):
        events.append((methods.get(m, 'select_players'), receiver, qword(parameters) if parameters else 0))
        put(exception, 0); return 0
    def find_method(cls, name, count, *_):
        p = allocate(); methods[p] = text(name); return p
    funcs = {
        'root_new': rooted, 'root_target': lambda n,*_: roots[n],
        'root_free': lambda n,*_: roots.pop(n) and 0,
        'string_new': lambda p,*_: allocate(text(p).encode() + b'\0'),
        'object_new': lambda *_: allocate(), 'object_class': lambda p,*_: 0x1234,
        'method_find': find_method, 'invoke': invoke,
    }
    for name, handler in funcs.items(): global_set(name, stub(handler))
    domain_api = stub(lambda *_: 0x1234)
    assembly = allocate(); put(assembly, 0x1234)
    def assemblies(d, count, *_): put(count, 1); return assembly
    imports = {
        'il2cpp_domain_get': domain_api, 'il2cpp_domain_get_assemblies': stub(assemblies),
        'il2cpp_assembly_get_image': stub(lambda *_: 0x1234),
        'il2cpp_class_from_name': stub(lambda *_: 0x1234),
    }
    def remote(a,b,c,d): events.append(('HTTP', a,b,c,d)); return 0
    for name, handler in {
        'original_accounts': remote,
        'original_updated': lambda a,b,c,*_: events.append(('updated',a,b,c)) or 0,
        'original_camera': lambda a,b,c,*_: events.append(('camera',a,b)) or 0,
        'original_confirm': lambda a,b,c,*_: events.append(('confirm',a,b)) or 0,
    }.items(): global_set(name, stub(handler))
    global_set('select_players', 0x2111)
    token_buffer = allocate(); global_set('auth_token', token_buffer)
    import_handlers = {
        'dlsym': lambda lib,p,*_: imports[text(p)],
        'br_stage': lambda p,*_: events.append(('stage',text(p))) or 0,
        'strlen': lambda p,*_: len(text(p)),
        'memcpy': lambda dst,src,n,*_: u.mem_write(dst, bytes(u.mem_read(src,n))) or dst,
        'memset': lambda dst,v,n,*_: u.mem_write(dst, bytes([v & 255]) * n) or dst,
    }
    sections = [struct.unpack_from('<IIQQQQIIQQ', raw, shoff + i * shsize) for i in range(shnum)]
    for s in sections:
        if s[1] != 4: continue
        dynsym = sections[s[6]]; strings = sections[dynsym[6]]
        names = raw[strings[4]:strings[4] + strings[5]]
        for pos in range(s[4], s[4] + s[5], s[9]):
            address, info, addend = struct.unpack_from('<QQq', raw, pos)
            kind = info & 0xffffffff
            if kind == 1027: put(address, addend)
            elif kind in (1025,1026):
                name_index = struct.unpack_from('<I', raw, dynsym[4] + (info >> 32) * dynsym[9])[0]
                name = names[name_index:names.index(b'\0', name_index)].decode()
                def unexpected(*_, name=name): raise AssertionError('Unexpected API: ' + name)
                put(address, stub(import_handlers.get(name, unexpected)))
    def run(name, *values):
        for r, v in zip(registers, (*values, 0,0,0,0)): u.reg_write(r, v)
        u.reg_write(UC_ARM64_REG_SP, 0x2000f000); u.reg_write(UC_ARM64_REG_X30, 0x1000f000)
        u.emu_start(symbols[name][0], 0x1000f000, count=20000)
        assert u.reg_read(UC_ARM64_REG_PC) == 0x1000f000
        return u.reg_read(UC_ARM64_REG_X0)
    gateway, callback, control = allocate(b'gateway\0'), allocate(), allocate()
    run('account_request',0,gateway,callback,0)
    assert not any(e[0] == 'HTTP' for e in events)
    assert global_get('modal') == 1
    assert roots[global_get('pending_url')] == gateway and roots[global_get('pending_callback')] == callback
    assert any(e[0] == 'Invoke' for e in events)
    for ticket in ('', 'x' * 42, '/' * 43, 'x' * 44):
        run('apply_token', allocate(ticket.encode() + b'\0'))
        assert not any(e[0] == 'HTTP' for e in events), ticket
        assert not any(u.mem_read(token_buffer,44)), ticket
    valid = allocate(b'A' * 43 + b'\0')
    run('apply_token', valid)
    assert bytes(u.mem_read(token_buffer,44)) == b'A' * 43 + b'\0'
    assert [e for e in events if e[0] == 'HTTP'] == [('HTTP',0,gateway,callback,0)]
    run('apply_token', valid)  # no duplicate request while loading
    assert len([e for e in events if e[0] == 'HTTP']) == 1
    run('camera_update',control,0); run('confirm',control,0)
    assert not any(e[0] in ('camera','confirm') for e in events)
    run('account_updated',control,allocate(),0)
    assert global_get('modal') == 0 and global_get('loading') == 0
    assert global_get('selection_pending') == 1
    run('camera_update',control,0); run('confirm',control,0)
    assert any(e[0] == 'camera' for e in events) and any(e[0] == 'confirm' for e in events)
    run('account_request',0,gateway,callback,123)
    assert [e for e in events if e[0] == 'HTTP'][-1] == ('HTTP',0,gateway,callback,123)
    u.mem_write(token_buffer, bytes(44))
    run('account_request',0,gateway,callback,0)
    assert global_get('modal') == 1
    run('apply_token',valid)  # replacement of rooted callback remains valid
    assert len([e for e in events if e[0] == 'HTTP']) == 3
    env_table, env_object, vm_table, vm_object = allocate(), allocate(), allocate(), allocate()
    # Allocate a full JNI table; CallStaticVoidMethod has slot 141 in JNI 1.6.
    env_table = next_object; next_object += 4096
    java_failed = stub(lambda a,b,c,*_: events.append(('java_account_failed',a,b,c)) or 0)
    for slot in range(220): put(env_table + slot * 8, java_failed)
    put(env_object, env_table); put(vm_object, vm_table)
    get_env = stub(lambda v,out,version,*_: put(out, env_object) or 0)
    put(vm_table + 6 * 8, get_env)
    global_set('vm', vm_object); global_set('java_login', 0x1234); global_set('j_account_failed', 0x5678)
    run('account_updated',control,0,0)
    assert global_get('modal') == 1 and global_get('loading') == 0
    assert u.mem_read(token_buffer,1) == b'\0'
    assert ('java_account_failed', env_object, 0x1234, 0x5678) in events
    run('apply_token',valid)
    assert len([e for e in events if e[0] == 'HTTP']) == 4
    result = {'arm64_execution': 'passed', 'cases': ['unauthenticated gate', 'empty local account callback',
              'malformed tokens rejected', 'authenticated replay preserves callback and gateway',
              'duplicate submit prevented', 'touch and confirm blocked during login',
              'real account closes modal', 'selection deferred to Unity frame',
              'normal input restored', 'normal account request ABI preserved', 'account switch replay',
              'failed character request clears token and reopens login', 'retry after account failure']}
    (ROOT / 'dist/engine-login-50226-native-tests.json').write_text(json.dumps(result, indent=2), 'utf-8')
    return result


if __name__ == '__main__': print(json.dumps(verify(), indent=2))
