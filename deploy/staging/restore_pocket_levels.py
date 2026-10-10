"""Recover verified legacy pocket levels while the staging server is stopped."""
from collections import defaultdict
import hashlib
import json
import os
from pathlib import Path
import re
import tarfile
import tempfile


def sha(payload):
    return hashlib.sha256(payload).hexdigest()


def items(value, path='$'):
    if isinstance(value, dict):
        if isinstance(value.get('Id'), str) and 'Prototype' in value and 'Tags' in value:
            yield path, value
        for key, child in value.items():
            yield from items(child, path + '.' + key)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from items(child, path + '[' + str(index) + ']')


def pocket(item):
    tags = [tag for tag in item.get('Tags') or [] if tag.get('Id') == 'pocket']
    if len(tags) != 1:
        raise ValueError('Expected exactly one pocket tag: ' + item['Id'])
    return tags[0]['Level']


def default_pocket(item, prototypes):
    rows = prototypes.get(item['Prototype'], [])
    row = next((r for r in rows if r.get('min_level', 1) <= item['Level'] <= r.get('max_level', 60)), rows[0] if rows else {})
    expression = str((row.get('tags') or {}).get('pocket', ''))
    if re.fullmatch(r'\d+', expression):
        return int(expression)
    points = sorted((int(a), int(b)) for a, b in re.findall(r'\(\s*(\d+)\s*,\s*(\d+)\s*\)', expression))
    if points:
        return next((v for level, v in reversed(points) if level <= item['Level']), points[0][1])
    raise ValueError('Unsupported native pocket expression: ' + item['Prototype'])


def differences(old, new, path='$'):
    if isinstance(old, dict) and isinstance(new, dict) and old.keys() == new.keys():
        for key in old:
            yield from differences(old[key], new[key], path + '.' + key)
    elif isinstance(old, list) and isinstance(new, list) and len(old) == len(new):
        for index, (a, b) in enumerate(zip(old, new)):
            yield from differences(a, b, path + '[' + str(index) + ']')
    elif old != new:
        yield path


def atomic_write(path, payload):
    metadata = path.stat()
    fd, temporary = tempfile.mkstemp(prefix='.pocket-recovery-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as output:
            output.write(payload)
            output.flush()
            os.fsync(output.fileno())
        os.chmod(temporary, metadata.st_mode & 0o777)
        if hasattr(os, 'chown'):
            os.chown(temporary, metadata.st_uid, metadata.st_gid)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def recover(state, reference_backup, reference_sha256, prototypes, selected_ids, apply=False):
    state = Path(state).resolve()
    reference_backup = Path(reference_backup)
    if sha(reference_backup.read_bytes()) != reference_sha256:
        raise ValueError('Historical backup checksum mismatch')
    previous = defaultdict(list)
    with tarfile.open(reference_backup) as archive:
        for member in archive:
            if not member.isfile() or member.name.endswith('.bak'):
                continue
            try:
                data = json.load(archive.extractfile(member))
            except (ValueError, UnicodeError):
                continue
            for _, item in items(data):
                if item['Id'] in selected_ids:
                    previous[item['Id']].append(item)
    originals = {}
    documents = {}
    current = defaultdict(list)
    for path in sorted(state.rglob('*')):
        if not path.is_file() or path.name.endswith('.bak'):
            continue
        if path.is_symlink() or not path.resolve().is_relative_to(state):
            raise ValueError('Unexpected persistent-state path')
        payload = path.read_bytes()
        try:
            data = json.loads(payload)
        except (ValueError, UnicodeError):
            continue
        originals[path] = payload
        documents[path] = data
        for location, item in items(data):
            if item['Id'] in selected_ids:
                current[item['Id']].append((path, location, item))
    restored = []
    already_restored = []
    absent = []
    allowed = defaultdict(set)
    for item_id in sorted(selected_ids):
        old_values = {(i['Prototype'], i['Level'], pocket(i)) for i in previous[item_id]}
        if len(old_values) != 1:
            raise ValueError('Historical item missing or ambiguous: ' + item_id)
        prototype, level, old_pocket = next(iter(old_values))
        if old_pocket != level or not 1 <= old_pocket <= 100:
            raise ValueError('Historical item does not match migration: ' + item_id)
        if not current[item_id]:
            absent.append(item_id)
            continue
        for path, location, item in current[item_id]:
            if item['Prototype'] != prototype or item['Level'] != level:
                raise ValueError('Item identity/level changed since audit: ' + item_id)
            now = pocket(item)
            if now >= old_pocket:
                already_restored.append({'id': item_id, 'file': str(path.relative_to(state)), 'pocket': now})
                continue
            if now != default_pocket(item, prototypes) or any(t.get('Id') == 'pocket' for t in item.get('TagModifications') or []):
                raise ValueError('Item no longer matches migration: ' + item_id)
            index = next(i for i, tag in enumerate(item['Tags']) if tag['Id'] == 'pocket')
            item['Tags'][index]['Level'] = old_pocket
            allowed[path].add(location + '.Tags[' + str(index) + '].Level')
            restored.append({'id': item_id, 'prototype': prototype, 'item_level': level,
                             'file': str(path.relative_to(state)), 'path': location,
                             'pocket_before_recovery': now, 'pocket_restored': old_pocket})
    replacements = {}
    for path, permitted in allowed.items():
        if set(differences(json.loads(originals[path]), documents[path])) != permitted:
            raise ValueError('Recovery changed unrelated fields')
        replacements[path] = (json.dumps(documents[path], ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    if any(path.read_bytes() != originals[path] for path in replacements):
        raise ValueError('Persistent state changed during recovery')
    completed = []
    if apply:
        try:
            for path, payload in replacements.items():
                atomic_write(path, payload)
                completed.append(path)
                if path.read_bytes() != payload:
                    raise ValueError('Recovery write verification failed')
        except Exception:
            # Caller keeps the server stopped throughout this transaction.
            for path in reversed(completed):
                atomic_write(path, originals[path])
            raise
    return {'applied': apply, 'reference_backup': str(reference_backup),
            'reference_backup_sha256_verified': True, 'requested_item_ids': len(selected_ids),
            'restored_unique_items': len({r['id'] for r in restored}),
            'changed_files': len(replacements), 'only_pocket_level_changes_verified': True,
            'already_restored': already_restored, 'absent_item_ids': absent, 'items': restored}


def verify_restored(state, report):
    expected = {r['id']: r for r in report['items']}
    seen = set()
    for path in sorted(Path(state).rglob('*')):
        if not path.is_file() or path.name.endswith('.bak'):
            continue
        try:
            data = json.loads(path.read_bytes())
        except (ValueError, UnicodeError):
            continue
        for _, item in items(data):
            record = expected.get(item['Id'])
            if record:
                if item['Prototype'] != record['prototype'] or pocket(item) != record['pocket_restored']:
                    raise ValueError('Restored pocket did not persist: ' + item['Id'])
                seen.add(item['Id'])
    if seen != set(expected):
        raise ValueError('Restored items absent after restart')
    return {'verified_item_ids': len(seen), 'restored_levels_persisted': True}
