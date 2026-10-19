"""Publication boundaries: drafts stay external and stale review cannot certify new inputs."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch, call

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import publication
from core.paths import work_root


class PublicationTest(unittest.TestCase):
    def test_build_storage_is_outside_checkout_and_checkout_specific(self):
        with tempfile.TemporaryDirectory() as folder:
            first = Path(folder) / "one/asset-studio"
            second = Path(folder) / "two/asset-studio"
            self.assertFalse(work_root(first).is_relative_to(first.parent))
            self.assertNotEqual(work_root(first), work_root(second))
            self.assertEqual(work_root(first), work_root(first))

    def test_publication_requires_matching_review_and_survives_cache_removal(self):
        with tempfile.TemporaryDirectory() as folder:
            candidate, final = Path(folder)/"candidate", Path(folder)/"catalog/item"
            publication.stage_stamp(candidate, "source-one")
            (candidate/"model.glb").write_bytes(b"model")
            (candidate/"lineup.png").write_bytes(b"review")
            files = ("model.glb", "lineup.png")
            publication.render_stamp(candidate, "source-one", files)
            self.assertFalse(final.exists(), "Rendering a review must never publish")
            with self.assertRaisesRegex(ValueError, "inputs changed"):
                publication.publish(candidate, final, "item", "source-two", files)
            (candidate/"model.glb").write_bytes(b"unreviewed change")
            with self.assertRaisesRegex(ValueError, "changed artifact"):
                publication.publish(candidate, final, "item", "source-one", files)
            (candidate/"model.glb").write_bytes(b"model")
            publication.publish(candidate, final, "item", "source-one", files)
            import shutil
            shutil.rmtree(candidate)
            self.assertTrue(publication.read(final, "source-one")["source_current"])
            self.assertFalse(publication.read(final, "source-two")["source_current"])
            (final/"model.glb").write_bytes(b"damaged")
            with self.assertRaisesRegex(ValueError, "Published artifact changed"):
                publication.read(final, "source-one")

    def test_fingerprint_tracks_recipe_shared_shading_and_source(self):
        with tempfile.TemporaryDirectory() as folder:
            studio = Path(folder)/"asset-studio"
            recipe = studio/"objects/tools/test/item.json"
            recipe.parent.mkdir(parents=True)
            recipe.write_text(json.dumps({"source":"source.glb"}))
            source = recipe.parent/"source.glb"
            source.write_bytes(b"source")
            (studio/"core").mkdir()
            shader = studio/"core/export.py"
            shader.write_text("first shading")
            first = publication.fingerprint(studio, recipe)
            shader.write_text("updated shading")
            second = publication.fingerprint(studio, recipe)
            self.assertNotEqual(first, second)
            source.write_bytes(b"updated source")
            self.assertNotEqual(second, publication.fingerprint(studio, recipe))

    def test_armor_inventory_excludes_legacy_and_draft_studies(self):
        import fittings
        inventory = fittings.studies()
        self.assertTrue({"costume_barbarian", "costume_barbarian_primitive", "costume_orc_king"} <= inventory.keys())
        self.assertNotIn("costume_gorget_study", inventory)
        self.assertTrue(all(key.startswith("costume_") for key in inventory))
        self.assertNotIn("armor_barbarian", inventory)
        self.assertNotIn("cuirass_orc", inventory)

    def test_shared_hashes_reuse_inputs_without_caching_recipe_or_source_edits(self):
        with tempfile.TemporaryDirectory() as folder:
            studio=Path(folder)/"asset-studio"
            (studio/"core").mkdir(parents=True)
            shader=studio/"core/export.py"
            shader.write_text("shading")
            recipes=[]
            for name in ("one","two"):
                recipe=studio/"objects/tools"/name/"item.json"
                recipe.parent.mkdir(parents=True)
                recipe.write_text(json.dumps({"source":"source.glb"}))
                recipe.with_name("source.glb").write_bytes(name.encode())
                recipes.append(recipe)
            expected=[publication.fingerprint(studio,p) for p in recipes]
            shared=publication.shared_hashes(studio)
            original=dict(shared)
            self.assertEqual(expected,[publication.fingerprint(studio,p,shared) for p in recipes])
            self.assertEqual(shared,original,"Per-recipe inputs must not leak into the shared map")
            recipes[0].with_name("source.glb").write_bytes(b"changed source")
            self.assertNotEqual(expected[0],publication.fingerprint(studio,recipes[0],shared))
            self.assertEqual(expected[1],publication.fingerprint(studio,recipes[1],shared))
            shader.write_text("changed shading")
            self.assertNotEqual(expected[1],publication.fingerprint(studio,recipes[1]))

    def test_costume_batch_refreshes_once_after_success(self):
        import fittings
        with patch.object(fittings,"publish_costume") as publish, patch.object(fittings,"catalog") as catalog:
            fittings.publish_costumes(["one","two"])
            self.assertEqual(publish.call_args_list,[call("one",refresh=False),call("two",refresh=False)])
            catalog.assert_called_once_with()

    def test_costume_batch_refreshes_partial_success_and_preserves_failure(self):
        import fittings
        failure=ValueError("stale second costume")
        with patch.object(fittings,"publish_costume",side_effect=[None,failure]), patch.object(fittings,"catalog") as catalog:
            with self.assertRaises(ValueError) as caught:
                fittings.publish_costumes(["one","two"])
            self.assertIs(caught.exception,failure)
            catalog.assert_called_once_with()
        with patch.object(fittings,"publish_costume",side_effect=failure), patch.object(fittings,"catalog") as catalog:
            with self.assertRaises(ValueError):
                fittings.publish_costumes(["one"])
            catalog.assert_not_called()

    def test_costume_batch_keeps_original_error_when_refresh_also_fails(self):
        import fittings
        failure=ValueError("publication failed")
        with patch.object(fittings,"publish_costume",side_effect=[None,failure]), patch.object(
                fittings,"catalog",side_effect=OSError("catalog failed")):
            with self.assertRaises(ValueError) as caught:
                fittings.publish_costumes(["one","two"])
            self.assertIs(caught.exception,failure)
            self.assertIn("catalog failed",failure.__notes__[0])
