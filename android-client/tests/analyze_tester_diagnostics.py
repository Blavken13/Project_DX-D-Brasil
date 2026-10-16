"""Validate exported ZIPs and write a reproducible, bounded diagnostic summary."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import zipfile

PROJECT = Path(__file__).resolve().parents[2]
DIRECTORY = PROJECT / 'docs/Diagnosticos'


def events(archive, name):
    return [json.loads(line) for line in archive.read(name).decode('utf-8').splitlines() if line.strip()]


def analyze(path):
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        manifest = json.loads(archive.read('manifest.json'))
        for entry in manifest['files']:
            data = archive.read(entry['name'])
            assert len(data) == entry['bytes'], (path, entry['name'], 'size')
            assert hashlib.sha256(data).hexdigest() == entry['sha256'], (path, entry['name'], 'sha256')
        session = json.loads(archive.read('session.json'))
        exit_info = json.loads(archive.read('exit-info.json'))
        native = events(archive, 'native-events.jsonl')
        java = events(archive, 'java-events.jsonl')
        mono = [e['mono_ns'] for e in native]
        invalid_levels = [e for e in native if e['event'] == 'UNITY_LOG' and e['a'] not in range(5)]
        memory = [e for e in java if e['category'] == 'memory']
        native_signal = exit_info.get('confirmed') and exit_info.get('reason') == 2 and exit_info.get('status') == 11
        http = [e for e in java if e['event'] == 'LAUNCHER_HTTP_END']
        return dict(archive=path.name, archive_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                    manifest_hashes_verified=True, session=session, exit=exit_info,
                    confirmed_sigsegv=bool(native_signal),
                    swipe_up_clean=exit_info.get('description') == 'SwipeUpClean',
                    graphics=archive.read('graphics-info.txt').decode('utf-8') if 'graphics-info.txt' in archive.namelist() else None,
                    native_event_count=len(native), java_event_count=len(java),
                    monotonic_range_ns=[min(mono), max(mono)],
                    native_event_types=dict(Counter(e['event'] for e in native)),
                    invalid_unity_log_levels=[dict(seq=e['seq'], level=e['a'], main_thread=e['b']) for e in invalid_levels],
                    launcher_http_results=[e['detail'] for e in http],
                    memory_samples=memory,
                    last_native_events=native[-8:],
                    stages=[e['detail'] or e['event'] for e in java if e['category'] == 'stage'],
                    fatal_trace_available=exit_info.get('trace_status') not in ('unavailable', 'not_applicable'))


def main():
    paths = sorted(DIRECTORY.glob('diagnostico-*.zip'))
    assert len(paths) == 4
    rows = [analyze(path) for path in paths]
    result = dict(apk_version=50219, files=rows,
                  confirmed_sigsegv=sum(r['confirmed_sigsegv'] for r in rows),
                  swipe_up_clean=sum(r['swipe_up_clean'] for r in rows),
                  all_page_sizes_4096=all(r['session']['page_size'] == 4096 for r in rows),
                  no_recorded_low_memory=all('low_memory=false' in e['detail'] for r in rows for e in r['memory_samples']),
                  limitations=['No native tombstone/backtrace in the four exports.',
                               'SwipeUpClean does not prove a spontaneous crash or its absence before manual closure.',
                               'Native hook ABI defects are verified independently in binary/emulation.'])
    target = PROJECT / 'docs/diagnosticos-testers-50219-analise.json'
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n', 'utf-8')
    for r in rows:
        print(r['session']['model'], 'API', r['session']['android_api'], 'SIGSEGV', r['confirmed_sigsegv'],
              'SwipeUpClean', r['swipe_up_clean'], 'invalid_log_levels', len(r['invalid_unity_log_levels']))
    print('PASS: four ZIP manifests verified; summary:', target)


if __name__ == '__main__':
    main()
