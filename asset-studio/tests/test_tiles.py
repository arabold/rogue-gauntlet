"""Regression checks for modular tiles: the seam contract, footprints and the build/export guards.

Run: ./.venv/bin/python -m unittest discover -s asset-studio/tests -v
Builds and exports go to a temporary repository; no Godot run is needed.
"""

import contextlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import kit
from items import solid
from tiles import floors, kaykit, overgrowth, overlays, walls, workflow
from tiles.geometry import clip, rect


def build(family_key, recipe):
    """Every piece of a recipe, built in memory: {stem: (piece, mesh)}."""
    family = workflow.Family(family_key)
    return {stem: (piece, family.build_piece(recipe, piece, variant)) for piece, variant, stem in family.tiles(recipe)}


class TileGeometryTest(unittest.TestCase):
    def test_clip_marks_flush_cuts_but_keeps_joints_lying_on_the_tile_edge(self):
        poly, joints = clip(rect(1.5, 0, 2.5, .4), [True] * 4, (-2, -2), (2, 2))
        self.assertAlmostEqual(poly[:, 0].max(), 2.0)
        self.assertEqual(sorted(joints), [False, True, True, True])
        _, joints = clip(rect(1.5, 0, 2.0, .4), [True] * 4, (-2, -2), (2, 2))
        self.assertTrue(all(joints), "A cell ending exactly on the tile edge is still grouted there")
        self.assertEqual(clip(rect(2.0, 0, 2.5, .4), [True] * 4, (-2, -2), (2, 2)), (None, None))

    def test_every_dungeon_family_is_closed_on_its_footprint_and_seamless(self):
        for path in sorted(workflow.TILESETS.glob("*/*.json")):
            key = f"{path.parent.name}/{path.stem}"
            with self.subTest(key):
                recipe = workflow.Family(key).recipe()
                pieces = build(key, recipe)
                for stem, (piece, mesh) in pieces.items():
                    report = solid.inspect(mesh.verts, mesh.faces)
                    # Derived walls keep KayKit's open panels exactly; everything else is closed.
                    source = kaykit.load(piece)[0] if recipe["style"] in workflow.DERIVED else None
                    allowed = solid.inspect(source.verts, source.faces) if source is not None else {}
                    for defect in ("boundary_edges", "nonmanifold_edges", "inconsistent_edges", "duplicate_faces"):
                        self.assertEqual(report[defect], allowed.get(defect, 0), f"{stem} {defect}")
                    self.assertLessEqual(len(mesh.faces), recipe["budget"]["triangles"], stem)
                self.assertEqual(workflow.seam_report(recipe, pieces)["errors"], [])

    def test_seam_check_rejects_a_floor_whose_edge_drifts(self):
        pieces = {f"t{v}": ("large", floors.planks({}, "large", v, "test/planks")) for v in "AB"}
        pieces["s"] = ("small", floors.planks({}, "small", "A", "test/planks"))
        self.assertEqual(workflow.floor_seams(pieces)["errors"], [])
        broken = pieces["tB"][1]
        lifted = broken.verts.copy()
        edge = np.isclose(lifted[:, 0], 2.0) & (lifted[:, 1] > .04)
        lifted[edge, 1] += .02
        pieces["tB"] = ("large", type(broken)(lifted, broken.faces, broken.slots))
        self.assertTrue(any("tB" in e for e in workflow.floor_seams(pieces)["errors"]))

    def test_small_and_large_tiles_share_one_seam_and_mixed_variants_differ(self):
        large = floors.flagstones({"layout": "running"}, "large", "A", "test/bricks")
        small = floors.flagstones({"layout": "running"}, "small", "A", "test/bricks")
        other = floors.flagstones({"layout": "running"}, "large", "B", "test/bricks")
        self.assertEqual(workflow.floor_seams({"l": ("large", large), "s": ("small", small), "o": ("large", other)})["errors"], [])
        again = floors.flagstones({"layout": "running"}, "large", "A", "test/bricks")
        np.testing.assert_array_equal(large.verts, again.verts)
        self.assertFalse(len(large.verts) == len(other.verts) and np.allclose(large.verts, other.verts),
                         "Variants must differ inside the tile")

    def test_wall_pieces_share_one_symmetric_end_and_fill_the_kaykit_footprint(self):
        design = {"courses": 7}
        pieces = {p: (p, walls.masonry(design, p, "A", "test/wall")) for p in walls.PIECES}
        self.assertEqual(workflow.wall_seams(pieces)["errors"], [])
        for piece, (_, mesh) in pieces.items():
            lo, hi = workflow.FOOTPRINT[piece]
            np.testing.assert_allclose(mesh.verts.min(0), lo, atol=1e-4)
            np.testing.assert_allclose(mesh.verts.max(0), hi, atol=1e-4)
        wall = pieces["wall"][1]
        skewed = wall.verts.copy()
        skewed[np.isclose(skewed[:, 0], 2.0) & (skewed[:, 2] > .4), 2] -= .03
        pieces["wall"] = ("wall", type(wall)(skewed, wall.faces, wall.slots))
        self.assertTrue(workflow.wall_seams(pieces)["errors"], "A one-sided end must fail the mirrored-end check")

    def test_overgrowth_keeps_the_kaykit_wall_and_its_ends_untouched(self):
        design = {"patches": 2, "grass": 3, "vines": 2, "cap_moss": 2, "slime": 2, "pools": 2}
        pieces = {p: (p, overgrowth.overgrown(design, p, "A", "test/moss")) for p in walls.PIECES}
        self.assertEqual(workflow.source_seams(pieces, {})["errors"], [])
        wall, source = pieces["wall"][1], kaykit.load("wall")[0]
        np.testing.assert_array_equal(wall.verts[: len(source.verts)], source.verts)
        self.assertGreater(len(wall.faces), len(source.faces) + 200, "Growth must actually be added")
        grown = wall.verts[len(source.verts):]
        self.assertLess(np.abs(grown[:, 0]).max(), 2.0 - overgrowth.END_MARGIN + .25)
        moved = kit.Piece(wall.verts + [0, 0, 0], wall.faces, wall.slots)
        moved.verts[np.isclose(moved.verts[:, 0], 2.0) & (moved.verts[:, 1] > 1)] += [0, 0, .05]
        self.assertTrue(workflow.source_seams({"wall": ("wall", moved)}, {})["errors"])

    def test_rug_ends_stay_off_the_cell_edge_and_runs_align_by_border(self):
        pieces = {p: (p, overlays.carpet({}, p, "A", "test/rug")) for p in overlays.BORDERS}
        self.assertEqual(workflow.overlay_seams(pieces)["errors"], [])
        edge = pieces["edge"][1]
        self.assertGreater(edge.verts[:, 2].min(), -2.0 + .1, "The border side of a rug is inset from the cell")
        self.assertAlmostEqual(edge.verts[:, 2].max(), 2.0, places=5)


class TileWorkflowTest(unittest.TestCase):
    """Build, freshness and export ownership in an isolated repository."""

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.repo = Path(temp.name).resolve()
        work = self.repo.parent / (self.repo.name + "-tiles-work")
        self.addCleanup(shutil.rmtree, work, True)
        stack = contextlib.ExitStack()
        self.addCleanup(stack.close)
        stack.enter_context(patch.object(workflow, "REPO", self.repo))
        stack.enter_context(patch.object(workflow, "TILESETS", self.repo / "asset-studio/tilesets"))
        stack.enter_context(patch.object(workflow, "EXPORT_ROOT", self.repo / "assets/custom-tiles"))
        stack.enter_context(patch.object(workflow, "work_root", return_value=work))
        stack.enter_context(patch.object(workflow.publication, "fingerprint", return_value="test-inputs"))
        stack.enter_context(patch.object(workflow.Family, "inputs", return_value={"builders": "test"}))
        stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        self.recipe = {"version": 1, "name": "Test planks", "kind": "floor", "style": "planks",
                       "colors": {"plank": [150, 92, 60], "plank_light": [162, 102, 66], "plank_dark": [134, 80, 52],
                                  "plank_warm": [156, 88, 54], "grout": [62, 40, 30]},
                       "pieces": {"large": ["A"], "small": ["A"]}, "budget": {"triangles": 1200},
                       "export": {"folder": "res://assets/custom-tiles/test"}}
        self.path = self.repo / "asset-studio/tilesets/test/floor_test.json"
        self.path.parent.mkdir(parents=True)
        self.path.write_text(json.dumps(self.recipe))

    def test_build_stages_named_tiles_and_rejects_stale_inputs(self):
        family = workflow.Family("test/floor_test")
        report = family.build()
        self.assertEqual(sorted(p.name for p in family.staged.glob("*.glb")),
                         ["floor_test_large_A.glb", "floor_test_small_A.glb"])
        self.assertEqual(report["errors"], [])
        self.assertEqual(len(family.models()), 2)
        self.path.write_text(json.dumps({**self.recipe, "name": "Changed"}))
        with self.assertRaisesRegex(ValueError, "build again"):
            family.models()

    def test_export_copies_into_the_tile_tree_and_protects_foreign_models(self):
        family = workflow.Family("test/floor_test")
        family.build()
        target = self.repo / "assets/custom-tiles/test"
        target.mkdir(parents=True)
        (target / "floor_test_large_A.glb").write_bytes(b"someone else's art")
        with self.assertRaisesRegex(ValueError, "Existing model differs"):
            family.export(no_import=True)
        family.export(no_import=True, force=True)
        self.assertEqual((target / "floor_test_large_A.glb").read_bytes(),
                         (family.staged / "floor_test_large_A.glb").read_bytes())
        family.export(no_import=True)  # Owned by the receipt now: a rebuild may replace it.

    def test_recipes_cannot_export_outside_custom_tiles(self):
        self.path.write_text(json.dumps({**self.recipe, "export": {"folder": "res://assets/custom-items/x"}}))
        with self.assertRaises(ValueError):
            workflow.Family("test/floor_test").recipe()


if __name__ == "__main__":
    unittest.main()
