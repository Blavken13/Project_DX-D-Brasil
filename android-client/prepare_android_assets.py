"""Prepare recovered Android bundles and verified embedded tutorial dependencies."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tarfile

ROOT = Path(__file__).resolve().parent.parent
RECOVERY = ROOT / 'android-client/recovered/android-cache-2026-09-30'
ADDITIONAL = ROOT / 'android-client/recovered/upstream-android'
TUTORIAL = ROOT / 'android-client/bundled/tutorial'


def native(path):
    resolved = str(path.resolve())
    return '\\\\?\\' + resolved if os.name == 'nt' and not resolved.startswith('\\\\?\\') else resolved


def digest(path):
    result = hashlib.sha256()
    with open(native(path), 'rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=RECOVERY / 'bundles-android')
    parser.add_argument('--inventory', type=Path, default=RECOVERY / 'inventory.json')
    parser.add_argument('--additional', type=Path, default=ADDITIONAL,
                        help='Diretório com inventory.json e bundles-android recuperados do servidor público')
    parser.add_argument('--output', type=Path, default=ROOT / 'Durango-CustomServer/server/assetbundles/android')
    parser.add_argument('--archive', type=Path, default=ROOT / 'android-client/dist/assetbundles-android-5.2.1.tar.gz')
    args = parser.parse_args()
    source_index = args.source / 'Info.5.2.1.json'
    index = json.loads(source_index.read_text(encoding='utf-8-sig'))
    inventory = json.loads(args.inventory.read_text(encoding='utf-8-sig'))
    known = {entry['server_filename']: entry for entry in inventory['bundles']}
    if digest(source_index) != inventory['summary']['source_index_sha256']:
        raise ValueError('O índice não corresponde ao inventário de recuperação.')
    additional = {}
    extra_inventory = args.additional / 'inventory.json'
    if extra_inventory.is_file():
        extra = json.loads(extra_inventory.read_text(encoding='utf-8'))
        if extra['source_index_sha256'] != digest(source_index):
            raise ValueError('Os recursos adicionais pertencem a outro índice Android.')
        additional = {entry['server_filename']: entry for entry in extra['bundles']}
    # Only these four recovered revisions replace catalog metadata. All other
    # entries retain their original CRC, hash, size and dependency references.
    tutorial = {}
    bundled = json.loads((TUTORIAL / 'manifest.json').read_text(encoding='utf-8'))['bundles']
    by_name = {entry['name']: entry for entry in bundled}
    for entry in index['FileList']:
        if entry['Name'] not in by_name:
            continue
        recovered = by_name[entry['Name']]
        source = TUTORIAL / recovered['name']
        if digest(source) != recovered['sha256'] or source.stat().st_size != recovered['bytes']:
            raise ValueError('Recurso integrado inválido: ' + recovered['name'])
        entry.update(Crc=recovered['crc'], Hash=recovered['cache_hash'], Size=recovered['bytes'])
        tutorial[recovered['server_filename']] = (source, recovered)
    entries = list(index['FileList']) + [{
        'Name': 'preload.bundle', 'Crc': index['PreloadCrc'],
        'Hash': index['PreloadHash'], 'Priority': 9999, 'Dependencies': []}]
    args.output.mkdir(parents=True, exist_ok=True)
    available, missing = [], []
    for entry in entries:
        name = entry['Name']
        if not name.endswith('.bundle') or '/' in name or '\\' in name:
            raise ValueError('Nome inválido no índice: ' + name)
        filename = name[:-7] + '.' + entry['Crc'] + '.bundle'
        source = args.source / filename
        if filename in tutorial:
            source, recovered = tutorial[filename]
        elif not os.path.isfile(native(source)):
            source = args.additional / 'bundles-android' / filename
            if filename not in additional or not os.path.isfile(native(source)):
                missing.append(entry)
                continue
            if additional[filename]['catalog_entry'] != entry:
                raise ValueError('Metadados divergentes no recurso adicional: ' + filename)
            if digest(source) != additional[filename]['sha256']:
                raise ValueError('Hash divergente do recurso adicional: ' + filename)
        checksum = digest(source)
        if filename in known and checksum != known[filename]['sha256']:
            raise ValueError('Hash divergente do celular: ' + filename)
        with open(native(source), 'rb') as stream:
            header = stream.read(96)
        if not header.startswith(b'UnityFS\0') or not any(version in header for version in
                (b'2017.4.34f1\0', b'2017.4.7f1\0')):
            raise ValueError('Bundle incompatível com o APK: ' + filename)
        target = args.output / filename
        if source.resolve() != target.resolve():
            temp = target.with_name(target.name + '.tmp')
            shutil.copyfile(native(source), native(temp))
            if digest(temp) != checksum:
                raise ValueError('Cópia inválida: ' + filename)
            os.replace(native(temp), native(target))
        available.append({'name': name, 'filename': filename, 'sha256': checksum,
                          'bytes': os.path.getsize(native(target))})
    required_missing = [entry for entry in missing if entry['Priority'] >= 500]
    if required_missing:
        raise ValueError(f'Faltam {len(required_missing)} pré-requisitos de prioridade 500 ou maior.')
    available_names = {entry['name'] for entry in available}
    declared_names = {entry['Name'] for entry in entries}
    dependencies_missing = sorted({dep for entry in entries
        if entry['Name'] in available_names and entry['Priority'] >= 500
        for dep in entry.get('Dependencies', [])
        if dep in declared_names and dep not in available_names})
    # Preserve the source verbatim unless verified tutorial revisions were applied.
    temp_index = args.output / 'Info.5.2.1.json.tmp'
    if tutorial:
        temp_index.write_text(json.dumps(index, separators=(',', ':')) + '\n', encoding='utf-8')
    else:
        shutil.copyfile(native(source_index), native(temp_index))
    os.replace(native(temp_index), native(args.output / 'Info.5.2.1.json'))
    report = {'expected': len(entries), 'available': len(available), 'missing': len(missing),
              'payload_bytes': sum(entry['bytes'] for entry in available),
              'required_missing': len(required_missing), 'required_dependencies_missing': dependencies_missing,
              'source_index_sha256': digest(source_index),
              'index_sha256': digest(args.output / 'Info.5.2.1.json'),
              'bundles': available, 'missing_bundles': missing}
    args.archive.parent.mkdir(parents=True, exist_ok=True)
    report_path = args.archive.with_suffix('').with_suffix('.json')
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temp_archive = args.archive.with_name(args.archive.name + '.tmp')
    with tarfile.open(native(temp_archive), 'w:gz', compresslevel=1) as archive:
        for filename in ['Info.5.2.1.json'] + [entry['filename'] for entry in available]:
            archive.add(native(args.output / filename), arcname='assetbundles/android/' + filename, recursive=False)
    os.replace(native(temp_archive), native(args.archive))
    args.archive.with_name(args.archive.name + '.sha256').write_text(
        digest(args.archive) + '  ' + args.archive.name + '\n', encoding='ascii')
    print(json.dumps({key: report[key] for key in ('expected', 'available', 'missing', 'payload_bytes',
          'required_missing')}, ensure_ascii=False), flush=True)
    print(f'Dependências ausentes dos bundles de prioridade >= 500: {len(dependencies_missing)}', flush=True)
    print('Diretório do gateway: ' + str(args.output), flush=True)
    print('Pacote para staging: ' + str(args.archive), flush=True)


if __name__ == '__main__':
    main()
