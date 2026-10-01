"""Recover only the verified public resources recorded in the upstream inventory."""
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parent / 'recovered/upstream-android'


def main():
    manifest = json.loads((ROOT / 'inventory.json').read_text('utf-8'))
    target = ROOT / 'bundles-android'
    target.mkdir(exist_ok=True)
    for entry in manifest['bundles']:
        name = entry['server_filename']
        if Path(name).name != name or '..' in name or ':' in name or '\\' in name:
            raise ValueError('Nome de arquivo inválido no inventário.')
        path = target / name
        data = path.read_bytes() if path.exists() else None
        if data is None or hashlib.sha256(data).hexdigest() != entry['sha256']:
            with urllib.request.urlopen(entry['download_url'], timeout=30) as response:
                data = response.read(entry['bytes'] + 1)
        if len(data) != entry['bytes'] or hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise ValueError('Payload divergente do recurso verificado: ' + name)
        if not data.startswith(b'UnityFS\0') or b'2017.4.34f1\0' not in data[:96]:
            raise ValueError('Versão Unity incompatível: ' + name)
        temp = path.with_name(path.name + '.tmp')
        temp.write_bytes(data)
        temp.replace(path)
        print('Recurso verificado: ' + name)


if __name__ == '__main__':
    main()
