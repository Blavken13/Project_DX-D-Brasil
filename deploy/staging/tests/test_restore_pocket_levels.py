import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('restore_pocket_levels', Path(__file__).resolve().parents[1] / 'restore_pocket_levels.py')
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.state = self.root / 'state'
        self.state.mkdir()
        self.item = {'Id': 'legacy', 'Prototype': 'bag_back', 'Level': 60,
                     'Tags': [{'Id': 'pocket', 'Level': 60}], 'TagModifications': None,
                     'Durability': {'Value': 123}, 'ReformSlots': [{'Index': 0, 'Tags': [{'Id': 'reform_pocket', 'Level': 60}]}]}
        payload = json.dumps({'inventory_items': [self.item]}).encode()
        self.backup = self.root / 'before.tar.gz'
        with tarfile.open(self.backup, 'w:gz') as archive:
            member = tarfile.TarInfo('AppData-nx/offline/cluster/1.player')
            member.size = len(payload)
            archive.addfile(member, io.BytesIO(payload))
        self.checksum = recovery.sha(self.backup.read_bytes())
        self.prototypes = {'bag_back': [{'min_level': 1, 'max_level': 60, 'tags': {'pocket': '15'}}]}
        self.current = json.loads(payload)
        self.current['inventory_items'][0]['Tags'][0]['Level'] = 15
        self.current['inventory_items'][0]['Durability']['Value'] = 99
        self.current['progress_after_patch'] = {'exp': 5000}
        self.path = self.state / '1.player'
        self.path.write_text(json.dumps(self.current), encoding='utf-8')

    def tearDown(self):
        self.temporary.cleanup()

    def run_recovery(self, apply=False):
        return recovery.recover(self.state, self.backup, self.checksum, self.prototypes, {'legacy'}, apply=apply)

    def test_dry_run_never_writes(self):
        original = self.path.read_bytes()
        self.assertEqual(self.run_recovery()['restored_unique_items'], 1)
        self.assertEqual(self.path.read_bytes(), original)

    def test_restore_preserves_current_progress_and_is_idempotent(self):
        report = self.run_recovery(apply=True)
        after = json.loads(self.path.read_bytes())
        self.assertEqual(list(recovery.differences(self.current, after)), ['$.inventory_items[0].Tags[0].Level'])
        self.assertEqual(after['inventory_items'][0]['Tags'][0]['Level'], 60)
        self.assertTrue(recovery.verify_restored(self.state, report)['restored_levels_persisted'])
        self.assertEqual(self.run_recovery(apply=True)['restored_unique_items'], 0)

    def test_relocates_item_by_id_into_world_storage(self):
        item = self.current['inventory_items'].pop()
        self.path.write_text(json.dumps(self.current), encoding='utf-8')
        world = self.state / 'island.world'
        world.write_text(json.dumps({'warehouse': [item]}), encoding='utf-8')
        report = self.run_recovery(apply=True)
        self.assertEqual(report['items'][0]['file'], 'island.world')
        self.assertEqual(json.loads(world.read_bytes())['warehouse'][0]['Tags'][0]['Level'], 60)

    def test_modified_pocket_or_bad_backup_aborts_without_writes(self):
        self.current['inventory_items'][0]['TagModifications'] = [{'Id': 'pocket', 'Level': 15}]
        self.path.write_text(json.dumps(self.current), encoding='utf-8')
        original = self.path.read_bytes()
        with self.assertRaises(ValueError):
            self.run_recovery(apply=True)
        self.assertEqual(self.path.read_bytes(), original)
        self.checksum = 'invalid'
        with self.assertRaises(ValueError):
            self.run_recovery(apply=True)
        self.assertEqual(self.path.read_bytes(), original)

    def test_partial_write_failure_rolls_back_changed_files(self):
        second = self.state / '2.player'
        second.write_bytes(self.path.read_bytes())
        originals = {p: p.read_bytes() for p in (self.path, second)}
        real_write = recovery.atomic_write
        def failing_write(path, payload):
            if path == second:
                raise OSError('simulated disk failure')
            real_write(path, payload)
        with patch.object(recovery, 'atomic_write', side_effect=failing_write):
            with self.assertRaises(OSError):
                self.run_recovery(apply=True)
        self.assertTrue(all(p.read_bytes() == data for p, data in originals.items()))


if __name__ == '__main__':
    unittest.main()
