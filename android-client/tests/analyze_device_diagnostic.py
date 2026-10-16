"""Verify an exported diagnostic ZIP and produce a bounded summary of one device."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile


def analyze(path):
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError('Invalid ZIP CRC')
        manifest = json.loads(archive.read('manifest.json'))
        for entry in manifest['files']:
            data = archive.read(entry['name'])
            if len(data) != entry['bytes'] or hashlib.sha256(data).hexdigest() != entry['sha256']:
                raise ValueError('Manifest mismatch: ' + entry['name'])
        def read(name):
            return archive.read(name).decode('utf-8') if name in archive.namelist() else None
        def events(name):
            return [json.loads(line) for line in (read(name) or '').splitlines() if line.strip()]
        native, java = events('native-events.jsonl'), events('java-events.jsonl')
        return dict(archive=path.name, archive_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                    manifest_hashes_verified=True,
                    files={name: archive.getinfo(name).file_size for name in archive.namelist()},
                    session=json.loads(read('session.json')), exit=json.loads(read('exit-info.json')),
                    graphics=read('graphics-info.txt'), native_event_count=len(native), java_event_count=len(java),
                    native_event_types=dict(Counter(e['event'] for e in native)),
                    invalid_unity_log_levels=[e for e in native if e['event']=='UNITY_LOG' and e['a'] not in range(5)],
                    native_events=native, java_events=java,
                    extra_text={name: read(name) for name in archive.namelist()
                                if name.endswith('.txt') and name != 'graphics-info.txt'})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('zip', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(args.zip)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', 'utf-8')
    printed = dict(result)
    printed['native_events'] = result['native_events'][-30:]
    printed['java_events'] = result['java_events'][-30:]
    print(json.dumps(printed, ensure_ascii=True, indent=2))


if __name__ == '__main__':
    main()
