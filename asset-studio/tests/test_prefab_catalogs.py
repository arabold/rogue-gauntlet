"""Prefab publication cannot silently replace a final sheet with a draft."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import prefab_catalogs, publication


class PrefabCatalogTest(unittest.TestCase):
    def test_publish_requires_current_unchanged_review(self):
        with tempfile.TemporaryDirectory() as folder:
            studio = Path(folder) / 'studio'
            source = studio / 'prefabs' / 'part.py'
            source.parent.mkdir(parents=True)
            source.write_text('original')
            candidate = prefab_catalogs.candidate(studio, 'outfits')
            signature = prefab_catalogs.inputs(studio, 'outfits')
            publication.stage_stamp(candidate, signature)
            (candidate / 'sheet.png').write_bytes(b'reviewed sheet')
            publication.render_stamp(candidate, signature, ('sheet.png',))
            self.assertFalse((studio / 'catalogs').exists())
            output = prefab_catalogs.publish(studio, 'outfits')
            self.assertEqual(output.read_bytes(), b'reviewed sheet')
            (candidate / 'sheet.png').write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'sheet changed'):
                prefab_catalogs.publish(studio, 'outfits')
            (candidate / 'sheet.png').write_bytes(b'reviewed sheet')
            source.write_text('new geometry')
            with self.assertRaisesRegex(ValueError, 'inputs changed'):
                prefab_catalogs.publish(studio, 'outfits')
