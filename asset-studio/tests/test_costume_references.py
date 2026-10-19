"""Complete costume recipes resolve consistently without overriding character anatomy."""
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from prefabs.creatures import characters


class CostumeReferencesTest(unittest.TestCase):
    def test_reference_expands_complete_assembly_and_character_palette_wins(self):
        recipe = dict(body={'torso': 'brute'}, outfit=[
            {'kind': 'costume_ref', 'name': 'knight'}, {'kind': 'helmet', 'style': 'horned'}],
            colors={'armor': [1, 2, 3]})
        resolved = characters.resolve_costumes(recipe)
        self.assertEqual(resolved['body'], recipe['body'])
        self.assertEqual([p['kind'] for p in resolved['outfit']], ['costume', 'steel_shoulders', 'helmet'])
        self.assertEqual(resolved['colors']['armor'], [1, 2, 3])
        self.assertIn('rivet', resolved['colors'])
        self.assertEqual(recipe['outfit'][0]['kind'], 'costume_ref')

    def test_reference_rejects_cycles_and_path_traversal(self):
        with patch('pathlib.Path.read_text', return_value=json.dumps(
                {'outfit': [{'kind': 'costume_ref', 'name': 'loop'}]})):
            with self.assertRaisesRegex(ValueError, 'Cyclic costume'):
                characters.resolve_costumes({'outfit': [{'kind': 'costume_ref', 'name': 'loop'}]})
        with self.assertRaisesRegex(ValueError, 'reference name'):
            characters.resolve_costumes({'outfit': [{'kind': 'costume_ref', 'name': '../knight'}]})

    def test_preserved_legacy_costumes_are_not_offered_as_current_fittings(self):
        import fittings
        current = fittings.studies()
        for name in ('orc_warrior', 'orc_shaman', 'goblin'):
            self.assertNotIn('costume_' + name, current)
            key = 'goblin/goblin' if name == 'goblin' else 'orc/' + name
            self.assertFalse(any(p['kind'] == 'costume_ref' for p in
                                 characters.resolve_costumes(characters.load(key))['outfit']))
