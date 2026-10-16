"""Merge the prepared 2213 bundles into an unpacked Unity 6 client/catalog."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parent


def merge(platform, destination, dry_run=False):
    source = ROOT / 'Clients' / platform
    additions = json.loads((source / 'Info.5.2.1.additions.json').read_text(encoding='utf-8'))
    destination = Path(destination).resolve()
    index_path = destination / 'Info.5.2.1.json'
    original = index_path.read_bytes()
    index = json.loads(original.decode('utf-8-sig'))
    for key in ('FileList', 'ItemList'):
        if not isinstance(index.get(key), list):
            raise ValueError('Catálogo inválido: ' + key)
        names = [entry['Name'] for entry in index[key]]
        if len(names) != len(set(names)):
            raise ValueError('O catálogo de destino contém duplicatas: ' + key)
    copies = []
    manifest = json.loads((source / 'manifest.json').read_text(encoding='utf-8'))
    hashes = {x['name']: x['sha256'] for x in manifest['files']}
    for entry in additions['FileList']:
        name = entry['Name'][:-7] + '.' + entry['Crc'] + '.bundle'
        if Path(name).name != name or any(c in name for c in ('/', '\\', ':')):
            raise ValueError('Nome de bundle inválido')
        path = source / 'AssetBundles' / name
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != hashes[name] or hashlib.md5(data).hexdigest() != entry['Crc']:
            raise ValueError('Bundle alterado ou incompleto: ' + name)
        if not data.startswith(b'UnityFS\0') or b'6000.6.' not in data[:96]:
            raise ValueError('Versão inesperada do bundle: ' + name)
        copies.append((path, destination / name))
    for key in ('FileList', 'ItemList'):
        incoming = {entry['Name']: entry for entry in additions[key]}
        index[key] = [incoming.pop(entry['Name'], entry) for entry in index[key]] + list(incoming.values())
    declared = {entry['Name'] for entry in index['FileList']}
    if any(d not in declared for entry in additions['FileList'] for d in entry['Dependencies']):
        raise ValueError('Dependência ausente do catálogo combinado')
    encoded = (json.dumps(index, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    changed = encoded != original
    if not dry_run:
        for src, dst in copies:
            if not dst.exists() or hashlib.sha256(dst.read_bytes()).hexdigest() != hashes[dst.name]:
                shutil.copy2(src, dst)
        if changed:
            backup = index_path.with_name(index_path.name + '.before-2213.' + hashlib.sha256(original).hexdigest()[:12] + '.bak')
            if not backup.exists():
                backup.write_bytes(original)
            temporary = index_path.with_suffix('.2213.tmp')
            temporary.write_bytes(encoded)
            temporary.replace(index_path)
    return dict(platform=platform, destination=str(destination), bundles=len(copies), index_changed=changed, dry_run=dry_run)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', choices=['Windows', 'Android'], required=True)
    parser.add_argument('--assetbundles', required=True, type=Path, help='Pasta extraída que contém Info.5.2.1.json')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    print(json.dumps(merge(args.platform, args.assetbundles, args.dry_run), indent=2, ensure_ascii=False))
