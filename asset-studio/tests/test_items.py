"""Regression checks for item authoring boundaries and non-destructive 3D export.

Run: ./.venv/bin/python -m unittest discover -s asset-studio/tests -v
All outputs and publication targets are isolated in a temporary repository.
"""

import contextlib
import io
import json
import shutil
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from items import align, model, workflow
from items.builders import steak
from items import solid
from items.designs import FAMILIES, COLORS
from core import kit, export as character_export


def section(piece, y):
    """Points where the surface crosses the horizontal plane at `y`, including vertices lying on it."""
    a, b = piece.verts[piece.faces], piece.verts[np.roll(piece.faces, -1, axis=1)]
    crossing = (a[..., 1] - y) * (b[..., 1] - y) < 0
    t = (y - a[..., 1][crossing]) / (b[..., 1] - a[..., 1])[crossing]
    on_plane = piece.verts[np.abs(piece.verts[:, 1] - y) < 1e-9]
    return np.vstack([a[crossing] + t[:, None] * (b[crossing] - a[crossing]), on_plane])


class ItemWorkflowTest(unittest.TestCase):
    """Exercise actual GLB round trips and publication guards without paid providers or Godot."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name).resolve()
        self.objects = self.repo / "asset-studio/objects"
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(workflow, "REPO", self.repo))
        self.stack.enter_context(patch.object(workflow, "OBJECTS", self.objects))
        self.stack.enter_context(patch.object(workflow, "work_root", return_value=self.repo.parent / (self.repo.name + "-studio-work")))
        self.addCleanup(shutil.rmtree, self.repo.parent / (self.repo.name + "-studio-work"), True)
        self.stack.enter_context(patch.object(workflow.publication, "fingerprint", return_value="test-inputs"))
        self.stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        self.source = self.repo / "original.glb"
        # Tetrahedron: asymmetric bounds make orientation and pivot errors visible.
        verts = [(0, 0, 0), (1, 0, 0), (0, 2, 0), (0, 0, 1)]
        faces = [(0, 2, 1), (0, 1, 3), (0, 3, 2), (1, 2, 3)]
        packed = align.map_faces_to_palette(verts, faces, [(130, 140, 145)] * 4, str(workflow.PALETTE))
        align.write_item_glb(str(self.source), *packed, str(workflow.PALETTE))

    def item(self):
        item = workflow.Item("tools/test_lamp")
        item.new(source=self.source)
        return item

    def test_prompt_only_is_read_only_and_matches_generation(self):
        from core import prompts
        from PIL import Image
        from unittest.mock import Mock
        item = self.item()
        before = item.path.read_bytes()
        brief = "A squat lantern with a crescent handle"
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            item.reference(brief, prompt_only=True)
        specification = json.loads(output.getvalue())
        self.assertEqual(item.path.read_bytes(), before)
        self.assertFalse((item.folder / "reference.png").exists())
        self.assertEqual(specification["referenced_image_paths"], [str(p) for p in prompts.STYLE_REFERENCES])
        provider = Mock()
        provider.edit_image.return_value = Image.new("RGB", (8, 8))
        with patch("core.ai.get_provider", return_value=provider):
            item.reference(brief)
        args, kwargs = provider.edit_image.call_args
        self.assertEqual(args[0], specification["prompt"])
        self.assertEqual(len(args[1]), len(prompts.STYLE_REFERENCES))
        self.assertEqual(kwargs["aspect"], specification["aspect"])
        self.assertEqual((item.folder / "reference.prompt.txt").read_text().strip(), args[0])

    def test_character_generation_retains_layout_with_shared_style(self):
        from core import match, prompts
        from unittest.mock import Mock
        provider = Mock()
        mannequin = object()
        with patch.object(match.ai, "get_provider", return_value=provider):
            match.inspire(mannequin, "A forest guardian")
        args, kwargs = provider.edit_image.call_args
        self.assertEqual(args[0], prompts.compose(match.CREATURE_PROMPT.format(brief="A forest guardian")))
        self.assertEqual(args[1], [mannequin])
        self.assertEqual(kwargs["aspect"], "16:9")

    def test_sliced_roll_has_closed_contact_and_recessed_details(self):
        bread = FAMILIES["food"]({"kind": "roll", "sliced": True})
        report = solid.inspect(bread.verts, bread.faces)
        import manifold3d as mf
        vertices, faces = solid.welded(bread.verts, bread.faces)
        body = mf.Manifold(mf.Mesh(vertices.astype(np.float32), faces.astype(np.uint32)))
        self.assertEqual(sum(c.volume() > 0 for c in body.decompose()), 1,
                         "The leaning slice must meet the roll; enclosed crumb cavities are valid")
        for key in ["boundary_edges", "nonmanifold_edges", "inconsistent_edges", "duplicate_faces"]:
            self.assertEqual(report[key], 0, key)
        self.assertGreater(report["signed_volume"], 0)
        self.assertLess(len(bread.faces), 2400)
        # The loaf's front crumb includes recessed vertices, rather than a flat
        # painted disk or detached overlays. Ignore the slice farther forward.
        crumb = bread.verts[bread.faces[bread.slots == "bread_crumb"]].reshape(-1, 3)
        main_face = crumb[(crumb[:, 2] > .07) & (crumb[:, 2] <= .106)]
        self.assertGreater(np.ptp(main_face[:, 2]), .005)
        self.assertGreater(np.ptp(bread.verts[:, 2]), np.ptp(bread.verts[:, 1]))
        bottom = bread.verts[np.isclose(bread.verts[:, 1], bread.verts[:, 1].min(), atol=1e-6)]
        self.assertGreater(np.ptp(bottom[:, 0]), .20, "Bread must rest on a broad base, not an oval point")
        larger = FAMILIES["food"]({"kind": "roll", "sliced": True, "footprint_scale": 2})
        self.assertEqual(solid.inspect(larger.verts, larger.faces)["boundary_edges"], 0)
        self.assertAlmostEqual(np.ptp(larger.verts[:, 0]), 2 * np.ptp(bread.verts[:, 0]), places=5)
        # A rigidly leaned slice keeps perpendicular edge/face directions after
        # exaggerated sizing. Post-rotation stretching or shear would fail this.
        from items.bread import _lean_slice
        sample = np.array([(0, .15, .077), (.1, .15, .077), (0, .25, .077), (0, .15, .13)])
        posed = _lean_slice(sample, 2, 1.45, 0)
        edge, face_x, face_y = posed[3] - posed[0], posed[1] - posed[0], posed[2] - posed[0]
        self.assertAlmostEqual(float(edge @ face_x), 0, places=8)
        self.assertAlmostEqual(float(edge @ face_y), 0, places=8)

    def test_source_round_trip_is_byte_identical_and_stays_in_studio(self):
        item = self.item()
        item.build()
        self.assertEqual(self.source.read_bytes(), item.built.read_bytes())
        self.assertFalse((self.repo / "assets").exists())
        self.assertFalse((self.repo / "scenes").exists())
        item.export(no_import=True)
        self.assertEqual(self.source.read_bytes(), item.destination(item.recipe()).read_bytes())

    def test_all_primitives_instances_and_node_transforms_count(self):
        g, chunks = model.read(self.source)
        g["meshes"][0]["primitives"] *= 2
        g["nodes"] = [{"mesh": 0, "translation": [3, 0, 0]}, {"mesh": 0, "translation": [-2, 0, 0]}]
        g["scenes"][0]["nodes"] = [0, 1]
        model.write(self.source, g, chunks)
        p, f, _ = model.geometry(self.source)
        self.assertEqual(len(f), 16)
        np.testing.assert_allclose([p[:, 0].min(), p[:, 0].max()], [-2, 4])

    def test_ground_pivot_and_dimensions_preserve_embedded_data(self):
        target = self.repo / "placed.glb"
        model.place(self.source, target, {"dimensions": [2, 2, 2], "pivot": "ground", "rotation_degrees": [0, 0, 90]})
        p, _, _ = model.geometry(target)
        self.assertAlmostEqual(p[:, 1].min(), 0)
        self.assertAlmostEqual((p[:, 0].min() + p[:, 0].max()) / 2, 0)
        self.assertLessEqual(np.ptp(p, axis=0).max(), 2.000001)
        original, binary = model.read(self.source)
        placed, result_binary = model.read(target)
        self.assertEqual(original["materials"], placed["materials"])
        self.assertEqual(binary, result_binary)

    def test_budget_failure_does_not_publish(self):
        item = self.item()
        data = item.recipe()
        data["budget"]["triangles"] = 3
        workflow.save(item.path, data)
        with self.assertRaisesRegex(ValueError, "exceeds budget"):
            item.build()
        self.assertFalse((self.repo / "assets").exists())

    def test_changed_recipe_source_or_build_cannot_export(self):
        item = self.item()
        item.build()
        g, chunks = model.read(item.folder / "source.glb")
        g["nodes"][0]["translation"] = [1, 0, 0]
        model.write(item.folder / "source.glb", g, chunks)
        with self.assertRaisesRegex(ValueError, "source inputs"):
            item.export(no_import=True)
        item.build()
        data = item.recipe()
        data["placement"]["scale"] = 0.5
        workflow.save(item.path, data)
        with self.assertRaisesRegex(ValueError, "changed"):
            item.export(no_import=True)

    def test_unrelated_existing_export_is_protected(self):
        item = self.item()
        item.build()
        target = item.destination(item.recipe())
        target.parent.mkdir(parents=True)
        target.write_bytes(b"existing art")
        with self.assertRaisesRegex(ValueError, "Existing model differs"):
            item.export(no_import=True)
        self.assertEqual(target.read_bytes(), b"existing art")

    def test_adoption_is_idempotent_and_preserves_game_files(self):
        path = self.repo / "assets/custom-items/tools/lamp/old_lantern.glb"
        path.parent.mkdir(parents=True)
        path.write_bytes(self.source.read_bytes())
        workflow.adopt(path)
        item = workflow.Item("tools/old_lantern")
        recipe = item.path.read_bytes()
        workflow.adopt(path)
        self.assertEqual(recipe, item.path.read_bytes())
        item.build()
        item.export(no_import=True)
        self.assertEqual(path.read_bytes(), self.source.read_bytes())
        self.assertFalse((self.repo / "scenes").exists())

    def test_adopted_source_cannot_be_overwritten_with_placement(self):
        path = self.repo / "assets/custom-items/tools/lamp/old_lantern.glb"
        path.parent.mkdir(parents=True)
        path.write_bytes(self.source.read_bytes())
        workflow.adopt(path)
        item = workflow.Item("tools/old_lantern")
        data = item.recipe()
        data["placement"]["scale"] = 2
        workflow.save(item.path, data)
        item.build()
        with self.assertRaisesRegex(ValueError, "adopted original"):
            item.export(no_import=True, force=True)

    def test_paths_and_gameplay_fields_are_rejected(self):
        for key in ["../escape", "tools/../../escape", "tools/Bad Name"]:
            with self.assertRaises(ValueError):
                workflow.Item(key)
        item = self.item()
        data = item.recipe()
        data["damage"] = 42
        workflow.save(item.path, data)
        with self.assertRaisesRegex(ValueError, "unknown"):
            item.recipe()
        del data["damage"]
        data["export"]["glb"] = "res://scenes/items/bad.glb"
        workflow.save(item.path, data)
        with self.assertRaisesRegex(ValueError, "inside"):
            item.recipe()

    def test_external_dependencies_and_invalid_container_are_rejected(self):
        g, chunks = model.read(self.source)
        g["images"][0] = {"uri": "texture.png"}
        model.write(self.source, g, chunks)
        with self.assertRaisesRegex(ValueError, "self-contained"):
            model.read(self.source)
        self.source.write_bytes(struct.pack("<4sII", b"nope", 2, 12))
        with self.assertRaisesRegex(ValueError, "Invalid GLB"):
            model.read(self.source)

    def test_prefab_backend_builds_real_geometry_without_reference(self):
        # Library assets live in the actual checkout; store their paths relative to it for this test.
        item = workflow.Item("potions/test_flask")
        item.new(backend="prefab")
        with patch.object(item, "inputs", return_value={}):
            item.build()
        self.assertGreater(item.validate()["triangles"], 10)
        self.assertFalse((self.repo / "assets").exists())

    def test_procedural_build_never_calls_ai(self):
        item = workflow.Item("potions/test_potion")
        item.new(backend="procedural", archetype="potion_small")
        with patch("core.ai.get_provider", side_effect=AssertionError("unexpected paid call")):
            # Palette lives outside the isolated repo; fingerprints are exercised separately.
            with patch.object(item, "inputs", return_value={}):
                item.build()
        self.assertGreater(item.validate()["triangles"], 10)

    def test_steak_has_one_closed_surface_with_flush_color_regions(self):
        vertices, faces, colors = steak()
        edges = {}
        for a, b, c in faces:
            for edge in [(a, b), (b, c), (c, a)]:
                key = tuple(sorted(edge))
                edges[key] = edges.get(key, 0) + 1
        self.assertTrue(all(count == 2 for count in edges.values()))
        p, f = np.asarray(vertices), np.asarray(faces)
        volume = np.einsum("ij,ij->i", p[f[:, 0]], np.cross(p[f[:, 1]], p[f[:, 2]])).sum() / 6
        self.assertGreater(volume, 0)
        model.colored(self.source, vertices, faces, colors)
        g, _ = model.read(self.source)
        self.assertEqual(len(g["meshes"]), 1)
        self.assertEqual(len(g["materials"]), 1)
        self.assertNotIn("images", g)
        self.assertIn("COLOR_0", g["meshes"][0]["primitives"][0]["attributes"])
        _, round_trip_faces, round_trip_colors = model.geometry(self.source)
        self.assertEqual(len(faces), len(round_trip_faces))
        np.testing.assert_allclose(colors, round_trip_colors, atol=1)

    def test_vertex_color_export_includes_art_import_settings(self):
        model.colored(self.source, *steak())
        item = self.item()
        item.build()
        item.export(no_import=True)
        target = item.destination(item.recipe())
        metadata = Path(str(target) + ".import")
        self.assertIn('import_script/path="res://asset-studio/godot/import_item.gd"', metadata.read_text())
        self.assertFalse((self.repo / "scenes").exists())
        # Keep custom artist import logic intact and do not replace the model on conflict.
        text = metadata.read_text().replace("res://asset-studio/godot/import_item.gd", "res://artist_import.gd")
        metadata.write_text(text)
        existing = target.read_bytes()
        with self.assertRaisesRegex(ValueError, "manual composition"):
            item.export(no_import=True)
        self.assertEqual(existing, target.read_bytes())
        self.assertEqual(text, metadata.read_text())

    def test_real_union_removes_internal_faces_and_rejects_floating_parts(self):
        assembly = solid.Assembly().add(solid.box((1, 1, 1)), "metal")
        assembly.add(solid.box((1, 1, 1), (.5, 0, 0)), "wood")
        joined = assembly.build()
        report = solid.inspect(joined.verts, joined.faces)
        self.assertEqual(report["components"], 1)
        self.assertEqual(report["boundary_edges"], 0)
        self.assertLess(report["signed_volume"], 1.6)  # overlapping volumes are not counted twice
        self.assertGreater(report["signed_volume"], 1.4)
        floating = solid.Assembly().add(solid.box((1, 1, 1)), "metal")
        floating.add(solid.box((.1, .1, .1), (0, 1, 0)), "gem")
        with self.assertRaisesRegex(ValueError, "disconnected solids"):
            floating.build()

    def test_export_seams_are_connected_but_vertex_only_contacts_are_not(self):
        p = kit.bevel_box((1, 1, 1), .05, "metal")
        # Split all vertices at face seams, as happens in glTF exports.
        seam_vertices = p.verts[p.faces].reshape(-1, 3)
        seam_faces = np.arange(len(seam_vertices)).reshape(-1, 3)
        self.assertEqual(solid.inspect(seam_vertices, seam_faces)["components"], 1)
        other = kit.bevel_box((1, 1, 1), 0, "metal", center=(1, 1, 1))
        first = kit.bevel_box((1, 1, 1), 0, "metal")
        merged = kit.Piece.merge(first, other)
        self.assertEqual(solid.inspect(merged.verts, merged.faces)["components"], 2)

    def test_open_duplicate_and_inward_surfaces_fail_topology(self):
        p = kit.bevel_box((1, 1, 1), .05, "metal")
        self.assertGreater(solid.inspect(p.verts, p.faces[1:])["boundary_edges"], 0)
        self.assertEqual(solid.inspect(p.verts, np.vstack([p.faces, p.faces[:1]]))["duplicate_faces"], 1)
        self.assertLess(solid.inspect(p.verts, p.faces[:, ::-1])["signed_volume"], 0)

    def test_static_surface_export_uses_character_palette_normals_and_pbr(self):
        p = solid.Assembly().add(solid.box((1, 1, 1)), "metal").add(
            solid.box((.4, .4, .4), (.5, 0, 0)), "gold").build()
        target = self.repo / "surface.glb"
        character_export.export_static(str(target), "study", p, COLORS)
        g, _ = model.read(target)
        self.assertIn("TEXCOORD_0", g["meshes"][0]["primitives"][0]["attributes"])
        self.assertNotIn("COLOR_0", g["meshes"][0]["primitives"][0]["attributes"])
        self.assertEqual(len(g["images"]), 1)
        actual = sorted((m["pbrMetallicRoughness"]["metallicFactor"], m["pbrMetallicRoughness"]["roughnessFactor"]) for m in g["materials"])
        expected = sorted({(character_export.DEFAULT_MATERIALS[s]["metallic"], character_export.DEFAULT_MATERIALS[s]["roughness"]) for s in ["metal", "gold"]})
        self.assertEqual(actual, expected)
        triangles = p.verts[p.faces]
        normals = character_export.corner_normals(p.verts, p.faces)
        t = character_export.gradient_coordinates(triangles, normals)
        self.assertGreater(np.ptp(t), .1)

    def test_all_fresh_family_designs_have_a_single_closed_surface(self):
        cases = {"potion": [{}], "weapon": [{"kind": k} for k in ["sword", "dagger", "axe", "battleaxe", "hammer", "staff", "crossbow"]],
                 "shield": [{}], "scroll": [{}], "jewelry": [{"kind": "ring"}, {"kind": "amulet"}], "key": [{}],
                 "food": [{"kind": k} for k in ["steak", "pear", "grapes", "drumstick", "beer"]],
                 "lamp": [{"kind": k} for k in ["lantern", "oil", "candelabrum"]],
                 "tool": [{"kind": k} for k in ["pickaxe", "grappling_hook", "bell", "mirror", "horn", "lockpicks"]]}
        for family, designs in cases.items():
            for design in designs:
                with self.subTest(family=family, design=design):
                    p = FAMILIES[family](design)
                    t = solid.inspect(p.verts, p.faces)
                    self.assertEqual(t["components"], 1)
                    self.assertEqual(t["boundary_edges"] + t["nonmanifold_edges"] + t["duplicate_faces"] + t["inconsistent_edges"], 0)
                    self.assertGreater(t["signed_volume"], 0)

    def assertClosedSolid(self, vertices, faces):
        t = solid.inspect(vertices, faces)
        self.assertEqual(t["components"], 1)
        self.assertEqual(t["boundary_edges"] + t["nonmanifold_edges"] + t["duplicate_faces"] + t["inconsistent_edges"], 0)
        self.assertGreater(t["signed_volume"], 0)

    def test_weapon_families_export_closed_and_within_budget(self):
        from items import forge
        from items.designs import WEAPONS
        heights = {}
        for family, module in WEAPONS.items():
            for kind in module.KINDS:
                with self.subTest(weapon=kind):
                    p = FAMILIES[family]({"kind": kind})
                    target = self.repo / (kind + ".glb")
                    character_export.export_static(str(target), kind, p, forge.COLORS, materials=forge.MATERIALS)
                    vertices, faces, _ = model.geometry(target)
                    self.assertLessEqual(len(faces), 2400)
                    triangles = vertices[faces]
                    areas = np.linalg.norm(np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]), axis=1)
                    self.assertTrue(np.all(areas > 1e-12))
                    self.assertClosedSolid(vertices, faces)
                    self.assertLess(vertices[:, 1].min(), 0)
                    self.assertGreater(vertices[:, 1].max(), 0)
                    heights[kind] = np.ptp(vertices[:, 1])
                    # The style guide's chunky KayKit grips: at least 0.10 thick where the hand holds the weapon.
                    self.assertGreaterEqual(np.ptp(section(p, 0.)[:, 2]), .10)
            with self.assertRaisesRegex(ValueError, f"Unknown {family}"):
                FAMILIES[family]({"kind": "unsupported"})
        # A one-handed wand stays well short of any two-handed staff.
        staffs = WEAPONS["staff"].KINDS
        self.assertLess(max(heights[k] for k in staffs if k.endswith("wand")) * 1.5,
                        min(heights[k] for k in staffs if k.endswith("staff")))

    def test_axes_follow_the_kaykit_weapon_style_guide(self):
        from items.axes import axe, KINDS
        for kind in KINDS:
            with self.subTest(axe=kind):
                p = axe({"kind": kind})
                # A chunky haft: KayKit hafts are roughly a tenth of an axe's length thick.
                # Cut the surface at y = 0.4, between the grip bands and the head.
                self.assertGreaterEqual(np.ptp(section(p, .4)[:, 2]), .13 if kind.endswith("double") else .10)
                # Cutting edges are broad bevels, and the bit reaches -X for one-handed mounting.
                self.assertIn("edge", set(map(str, p.slots)))
                self.assertLess(p.verts[:, 0].min(), -.55)

    def test_swords_and_daggers_follow_the_kaykit_weapon_style_guide(self):
        from items.blades import blade, KINDS
        heights = {}
        for kind in KINDS:
            with self.subTest(blade=kind):
                p = blade({"kind": kind})
                heights[kind] = top = p.verts[:, 1].max()
                # KayKit blades are thick slabs, and their guards are chunky blocks rather than plates.
                self.assertGreaterEqual(np.ptp(section(p, top * .6)[:, 2]), .05)
                self.assertGreaterEqual(np.ptp(p.verts[:, 2]), .09)
                self.assertTrue(any(str(s).endswith("edge") or str(s) == "birch" for s in p.slots))
        self.assertLess(heights["short"], heights["dirk"])
        self.assertLess(heights["dirk"], heights["knight"])
        for two_handed in ("claymore", "winged", "greatsword", "practice", "crescent", "curved_great"):
            self.assertGreater(heights[two_handed], heights["knight"] * 1.1)

    def test_fountains_are_closed_and_effect_circles_lie_on_their_liquid(self):
        from items.fountains import fountain, KINDS, POOLS
        for kind in KINDS:
            with self.subTest(fountain=kind):
                basin, parts = fountain({"kind": kind})
                # The basin and its liquid are each one closed solid; the liquid stays a separate part the game hides.
                for p in (basin, parts["Liquid"]):
                    t = solid.inspect(p.verts, p.faces)
                    self.assertEqual(t["components"], 1)
                    self.assertEqual(t["boundary_edges"] + t["nonmanifold_edges"] + t["duplicate_faces"] + t["inconsistent_edges"], 0)
                    self.assertGreater(t["signed_volume"], 0)
                self.assertNotIn("liquid", set(basin.slots))
                self.assertAlmostEqual(basin.verts[:, 1].min(), 0, places=5)
                for level, radius in POOLS[kind]:
                    # Game-side sparkles fill this circle, so straight below its rim lies the pool surface, not stone.
                    angles = np.linspace(0, 2 * np.pi, 48, endpoint=False)
                    above = np.column_stack([np.cos(angles) * radius, np.full(48, level + .01), np.sin(angles) * radius])
                    drop = kit.ray_distance(above, np.tile([0., -1., 0.], (48, 1)), parts["Liquid"].verts, parts["Liquid"].faces)
                    np.testing.assert_allclose(drop, .01, atol=1e-4, err_msg=f"{kind} pool at {level}")
        with self.assertRaisesRegex(ValueError, "Unknown fountain"):
            fountain({"kind": "unsupported"})

    def test_fountain_liquid_exports_as_its_own_node(self):
        from items.fountains import fountain
        basin, parts = fountain({"kind": "tiered"})
        target = self.repo / "fountain.glb"
        character_export.export_static(str(target), "fountain", basin, {"stone": [140, 134, 124],
                                       "stone_dark": [108, 103, 96], "liquid": [200, 24, 44]},
                                       materials={"liquid": {"emissive": True}}, parts=parts)
        g, _ = model.read(target)
        self.assertEqual([node["name"] for node in g["nodes"]], ["Item", "Liquid"])
        self.assertEqual(g["scenes"][0]["nodes"], [0, 1])
        liquid_materials = {p["material"] for p in g["meshes"][g["nodes"][1]["mesh"]]["primitives"]}
        self.assertTrue(all(g["materials"][i].get("emissiveFactor") for i in liquid_materials))
        _, faces, _ = model.geometry(target)
        self.assertEqual(len(faces), len(basin.faces) + len(parts["Liquid"].faces))

    def test_world_props_export_only_into_the_props_tree(self):
        item = self.item()
        data = item.recipe()
        data["export"]["glb"] = "res://assets/custom-props/fountains/test.glb"
        self.assertEqual(item.destination(data), self.repo / "assets/custom-props/fountains/test.glb")
        data["export"]["glb"] = "res://assets/custom-props/../custom-monsters/test.glb"
        with self.assertRaisesRegex(ValueError, "inside"):
            item.destination(data)

    def test_curated_shields_export_with_real_curvature_and_closed_fittings(self):
        from items.shieldcraft import shieldcraft, KINDS, COLORS as SHIELD_COLORS, MATERIALS as SHIELD_MATERIALS
        for kind in KINDS:
            with self.subTest(shield=kind):
                p = shieldcraft({"kind": kind})
                target = self.repo / (kind + ".glb")
                character_export.export_static(str(target), kind, p, SHIELD_COLORS, materials=SHIELD_MATERIALS)
                vertices, faces, _ = model.geometry(target)
                self.assertLessEqual(len(faces), 6000)
                tri = vertices[faces]
                areas = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
                self.assertTrue(np.all(areas > 1e-12))
                topology = solid.inspect(vertices, faces)
                self.assertEqual(topology["components"], 1)
                self.assertEqual(sum(topology[k] for k in ["boundary_edges", "nonmanifold_edges", "duplicate_faces", "inconsistent_edges"]), 0)
                self.assertGreater(topology["signed_volume"], 0)
                # The ring is a constant-thickness structural frame, so its
                # front/back midpoint measures the shield's actual bend.
                rim_slot = "bone" if kind == "bone_kite" else "brass" if kind == "paladin" else "iron"
                rim = p.verts[p.faces[p.slots == rim_slot]].reshape(-1, 3)
                if kind in ("knight", "guard_scutum", "paladin"):
                    # Away from boss/grip, edge geometry recedes behind center.
                    edge = rim[np.abs(rim[:, 0]) > np.abs(rim[:, 0]).max() * .95]
                    self.assertLess(edge[:, 2].max(), .15)
                if kind in ("barbarian", "spiked_buckler"):
                    # Flat round discs retain their front rim on the same plane.
                    edge = rim[(np.abs(rim[:, 0]) > .40)]
                    self.assertAlmostEqual(edge[:, 2].max(), .208, places=6)
                if kind == "bone_kite":
                    self.assertTrue({"wood", "bone", "dark", "leather"}.issubset(set(p.slots)))
        with self.assertRaisesRegex(ValueError, "Unknown shieldcraft"):
            shieldcraft({"kind": "material_matrix"})

    def test_shield_damage_removes_material_and_preserves_closed_backing(self):
        from items.shieldcraft import shieldcraft
        for kind in ["knight", "spiked_buckler"]:
            with self.subTest(shield=kind):
                clean = shieldcraft({"kind": kind, "details": False})
                worn = shieldcraft({"kind": kind, "details": True})
                clean_volume = solid.inspect(clean.verts, clean.faces)["signed_volume"]
                worn_volume = solid.inspect(worn.verts, worn.faces)["signed_volume"]
                self.assertLess(worn_volume, clean_volume - 1e-6)
                self.assertGreater(worn_volume, clean_volume * .97)
                self.assertEqual(solid.inspect(worn.verts, worn.faces)["boundary_edges"], 0)
        wooden = shieldcraft({"kind": "barbarian"})
        self.assertTrue({"wood", "wood_light", "wood_dark", "wood_cut"}.issubset(set(wooden.slots)))
        front = wooden.verts[wooden.faces[np.isin(wooden.slots, ["wood", "wood_light", "wood_dark"])]].reshape(-1, 3)
        front = front[front[:, 2] > .17]
        self.assertGreater(np.ptp(front[:, 2]), .01)
        self.assertEqual(solid.inspect(wooden.verts, wooden.faces)["components"], 1)

    def test_reusable_construction_and_solid_wear_are_closed_and_discoverable(self):
        from prefabs.details.construction import timber_plank, repair_plate, corner_bracket
        from prefabs.details.wear import solid_gash
        from prefabs.registry import get_schema
        for part in [timber_plank(), repair_plate(), corner_bracket(), solid_gash()]:
            topology = solid.inspect(part.verts, part.faces)
            self.assertEqual(topology["boundary_edges"] + topology["nonmanifold_edges"], 0)
            self.assertGreater(topology["signed_volume"], 0)
        schema = get_schema()["details"]
        self.assertIn("timber_plank", schema["construction"])
        self.assertIn("repair_plate", schema["construction"])
        self.assertIn("solid_gash", schema["wear"])

    def test_prefab_catalog_hosts_do_not_require_a_character_skin_palette(self):
        from core import catalog
        host = kit.bevel_box((.2, .4, .2), .01, "skin")
        part = kit.stud(.05, .02, "iron")
        image = catalog.render(part, host=host, colors={"iron": (80, 90, 100)}, size=48)
        self.assertEqual(image.size, (48, 48))

    def test_round_rims_have_a_continuous_front_chamfer_and_repairs_are_distinct(self):
        from items.shieldcraft import shieldcraft
        for kind in ["barbarian", "spiked_buckler"]:
            p = shieldcraft({"kind": kind})
            tri = p.verts[p.faces]
            center = tri.mean(1)
            normal = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
            normal /= np.maximum(np.linalg.norm(normal, axis=1)[:, None], 1e-12)
            radius = np.linalg.norm(center[:, :2], axis=1)
            radial = (normal[:, :2] * center[:, :2]).sum(1) / np.maximum(radius, 1e-12)
            bevel = (radius > .405) & (center[:, 2] > .19) & (normal[:, 2] > .3) & (radial > .3)
            self.assertGreater(np.count_nonzero(bevel), 20)
        guard = shieldcraft({"kind": "guard_scutum"})
        barbarian = shieldcraft({"kind": "barbarian"})
        self.assertIn("wood_repair", guard.slots)
        self.assertNotIn("wood_repair", barbarian.slots)

    def test_heroic_shields_have_surface_quarters_wood_joinery_and_scale_volume(self):
        from items.shieldcraft import shieldcraft
        simple = shieldcraft({"kind": "wood_simple"})
        self.assertTrue(set(simple.slots).issubset({"wood", "wood_dark", "pale_wood", "wood_cut"}))
        quarters = shieldcraft({"kind": "heroic_quarters"})
        for sx, sy, color in [(-1, 1, "azure"), (1, 1, "ivory"), (-1, -1, "ivory"), (1, -1, "azure")]:
            tri = quarters.verts[quarters.faces]
            c = tri.mean(1)
            front = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])[:, 2] > 1e-9
            selected = front & (c[:, 0] * sx > .02) & (c[:, 1] * sy > .02) & np.isin(quarters.slots, ["azure", "ivory"])
            self.assertGreater(np.count_nonzero(selected), 0)
            self.assertEqual(set(quarters.slots[selected]), {color})
        dragon = shieldcraft({"kind": "dragon_long"})
        self.assertGreater(np.ptp(dragon.verts[:, 1]), 1.7)
        self.assertTrue({"dragon", "dragon_light", "dragon_dark"}.issubset(set(dragon.slots)))
        scale = dragon.verts[dragon.faces[np.isin(dragon.slots, ["dragon", "dragon_light", "dragon_dark"])]]
        self.assertGreater(np.ptp(scale[:, :, 2]), .07)
        for kind in ["royal_crown", "royal_lily"]:
            p = shieldcraft({"kind": kind})
            self.assertIn("gold", p.slots)
            self.assertEqual(solid.inspect(p.verts, p.faces)["components"], 1)

    def test_v_shield_panels_keep_their_angle_and_export_a_sharp_shared_ridge(self):
        from items.shieldcraft import shieldcraft, COLORS as SHIELD_COLORS, MATERIALS as SHIELD_MATERIALS
        from core.mesh import read_glb
        for angle in [22, 30]:
            p = shieldcraft({"kind": "royal_crown", "fold_degrees": angle})
            tri = p.verts[p.faces]
            flat_z = tri[:, :, 2] - .15 + np.tan(np.deg2rad(angle)) * np.abs(tri[:, :, 0])
            face_normal = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
            panel = np.all(np.abs(flat_z - .0375) < 1e-6, axis=1) & (face_normal[:, 2] > 1e-9)
            ridge = (np.abs(tri[:, :, 0]) < 1e-6) & panel[:, None]
            self.assertGreater(np.count_nonzero(ridge), 4)
            normals = p.normals[ridge]
            actual = np.degrees(np.arctan2(np.abs(normals[:, 0]), normals[:, 2]))
            np.testing.assert_allclose(actual, angle, atol=1e-4)
            self.assertLess(normals[:, 0].min(), -.3)
            self.assertGreater(normals[:, 0].max(), .3)
            target = self.repo / "creased.glb"
            character_export.export_static(str(target), "creased", p, SHIELD_COLORS, materials=SHIELD_MATERIALS)
            g, _, accessor = read_glb(str(target))
            stored = accessor(g["meshes"][0]["primitives"][0]["attributes"]["NORMAL"])
            np.testing.assert_allclose(stored.reshape(p.normals.shape), p.normals, atol=1e-6)
            self.assertEqual(solid.inspect(p.verts, p.faces)["boundary_edges"], 0)
        with self.assertRaisesRegex(ValueError, "fold"):
            shieldcraft({"kind": "royal_crown", "fold_degrees": 70})

    def test_painted_shield_faces_are_inset_beneath_the_outer_rim(self):
        from items.shieldcraft import shieldcraft
        from items.heroic_shields import outline_for
        for kind in ["royal_crown", "royal_lily", "heroic_quarters", "heroic_sun"]:
            with self.subTest(shield=kind):
                p = shieldcraft({"kind": kind})
                body = p.verts[p.faces[np.isin(p.slots, ["azure", "ivory", "purple", "crimson"])]]
                points = body.reshape(-1, 3)[:, :2]
                outline = outline_for(kind)
                distances = []
                for start, end in zip(outline, np.roll(outline, -1, axis=0)):
                    edge = end - start
                    t = np.clip(((points - start) @ edge) / (edge @ edge), 0, 1)
                    distances.append(np.linalg.norm(points - start - t[:, None] * edge, axis=1))
                self.assertGreater(np.min(distances), .019)

    def test_dragon_has_two_bound_shoulder_horns_and_a_clear_lower_outline(self):
        from items.shieldcraft import shieldcraft
        from prefabs.details.heraldry import bound_horn
        p = shieldcraft({"kind": "dragon_long"})
        bone = p.faces[p.slots == "bone"]
        horn_points = p.verts[bone].reshape(-1, 3)
        for tip in [(-.335, 1.60), (.335, 1.60)]:
            self.assertLess(np.linalg.norm(horn_points[:, :2] - tip, axis=1).min(), .002)
        self.assertGreater(np.abs(horn_points[:, 0]).min(), .12)
        self.assertGreater(horn_points[:, 1].min(), .55)
        self.assertGreater(horn_points[:, 0].max(), .53)
        self.assertLess(horn_points[:, 0].min(), -.53)
        self.assertEqual(solid.inspect(p.verts, p.faces)["components"], 1)
        part = bound_horn()
        self.assertEqual(set(part.slots), {"bone", "leather"})
        topology = solid.inspect(part.verts, part.faces)
        self.assertEqual(topology["components"], 1)
        self.assertEqual(topology["boundary_edges"] + topology["nonmanifold_edges"], 0)

    def test_dragon_fittings_are_closed_wraparound_parts_and_exaggerated_sockets(self):
        from prefabs.details.construction import rim_clamp, horn_socket
        from prefabs.registry import get_schema
        for part in [rim_clamp(), horn_socket()]:
            topology = solid.inspect(part.verts, part.faces)
            self.assertEqual(topology["components"], 1)
            self.assertEqual(topology["boundary_edges"] + topology["nonmanifold_edges"], 0)
            self.assertGreater(topology["signed_volume"], 0)
        clamp = rim_clamp()
        topology = solid.inspect(clamp.verts, clamp.faces)
        box_volume = np.prod(np.ptp(clamp.verts, axis=0))
        self.assertLess(topology["signed_volume"], box_volume * .8)
        self.assertLess(clamp.verts[:, 2].min(), -.09)
        self.assertGreater(clamp.verts[:, 2].max(), .09)
        self.assertIn("horn_socket", get_schema()["details"]["construction"])
        self.assertIn("rim_clamp", get_schema()["details"]["construction"])

    def test_flush_scroll_ink_survives_union_without_becoming_an_overlay(self):
        p = FAMILIES["scroll"]({"variant": "raidho"})
        self.assertIn("gem", p.slots)
        self.assertIn("paper", p.slots)
        self.assertEqual(solid.inspect(p.verts, p.faces)["components"], 1)
        ink = p.verts[p.faces[p.slots == "gem"]]
        np.testing.assert_allclose(ink[:, :, 2], .0125, atol=1e-7)
        self.assertGreaterEqual(float(ink[:, :, 1].min()), .07)
        self.assertLessEqual(float(ink[:, :, 1].max()), .51)

    def test_closed_assembly_preserves_parts_and_reports_attachment_review(self):
        p = kit.Piece.merge(kit.bevel_box((1, 1, 1), .05, "metal"),
                            kit.bevel_box((.1, .1, .1), .01, "metal", center=(0, 2, 0)))
        model.colored(self.source, p.verts, p.faces, [(130, 140, 150)] * len(p.faces))
        item = self.item()
        item.build()
        report = item.validate()
        self.assertEqual(report["topology"]["components"], 2)
        self.assertTrue(report["warnings"])
        self.assertFalse(report["errors"])
        data = item.recipe()
        data["topology"] = "closed_surface"
        workflow.save(item.path, data)
        with self.assertRaisesRegex(ValueError, "disconnected surface"):
            item.validate()
        self.assertFalse((self.repo / "assets").exists())

    def test_source_palette_conversion_preserves_geometry_and_tracks_inputs(self):
        studio = self.repo / "asset-studio"
        for relative in ["rigs/item_palette.png", "items/source_surface.py", "items/model.py", "items/align.py", "core/export.py", "core/mesh.py"]:
            target = studio / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(workflow.STUDIO / relative, target)
        self.stack.enter_context(patch.object(workflow, "STUDIO", studio))
        self.stack.enter_context(patch.object(workflow, "PALETTE", studio / "rigs/item_palette.png"))
        item = self.item()
        data = item.recipe()
        data["surface"] = {"palette": "studio_grid"}
        workflow.save(item.path, data)
        item.build()
        before_p, before_f, _ = model.geometry(item.folder / "source.glb")
        after_p, after_f, _ = model.geometry(item.built)
        np.testing.assert_allclose(after_p[after_f], before_p[before_f], atol=1e-7)
        g, _ = model.read(item.built)
        self.assertTrue(g["images"])
        self.assertIn("surface_palette", g["images"][0]["name"])
        self.assertIn("asset-studio/items/source_surface.py", item.inputs(data))
        item.export(no_import=True)
        self.assertTrue(item.destination(data).exists())

    def test_potion_liquid_preserves_labels_harnesses_and_collars_for_every_tint(self):
        from items.source_surface import palette_piece, potion_liquid
        originals = workflow.STUDIO / "objects/potions"
        for size in ["small", "medium", "large"]:
            reference = originals / f"potion_{size}_crimson"
            data = json.loads((reference / "item.json").read_text())
            piece, _ = palette_piece(reference / "source.glb", workflow.PALETTE, data["surface"]["slots"])
            # Crimson's three source swatches are unique to liquid, providing an
            # independent oracle even when other tints share swatches with hardware.
            expected = np.isin(piece.slots, ["glass_magenta", "glass_crimson", "glass_rose"])
            for folder in sorted(originals.glob(f"potion_{size}_*")):
                with self.subTest(potion=folder.name):
                    data = json.loads((folder / "item.json").read_text())
                    source, _ = palette_piece(folder / "source.glb", workflow.PALETTE, data["surface"]["slots"])
                    painted, report = potion_liquid(source, size)
                    np.testing.assert_array_equal(painted.verts, source.verts)
                    np.testing.assert_array_equal(painted.faces, source.faces)
                    np.testing.assert_array_equal(painted.slots == "liquid", expected)
                    np.testing.assert_array_equal(painted.slots[~expected], source.slots[~expected])
                    self.assertEqual(report["liquid_triangles"], int(expected.sum()))
                    self.assertEqual(data["surface"]["paint"], "potion_liquid")
                    self.assertIn("liquid", data["colors"])
                    self.assertEqual(data["materials"]["liquid"]["metallic"], 0)

    def test_potion_liquids_stay_distinguishable_and_match_across_sizes(self):
        """A liquid tint is the run's only visual cue for a potion type, so two tints that
        read alike (the former golden/orange) make an unknown potion pass for a known one."""
        def lab(rgb):
            linear = [((c / 255 + .055) / 1.055) ** 2.4 if c / 255 > .04045 else c / 255 / 12.92 for c in rgb]
            xyz = np.array([[.4124, .3576, .1805], [.2126, .7152, .0722], [.0193, .1192, .9505]]) @ linear
            f = [t ** (1 / 3) if t > .008856 else 7.787 * t + 16 / 116 for t in xyz / [.95047, 1, 1.08883]]
            return np.array([116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])])
        liquids = {}
        for folder in sorted((workflow.STUDIO / "objects/potions").glob("potion_*")):
            _, size, tint = folder.name.split("_")
            liquids.setdefault(tint, {})[size] = json.loads((folder / "item.json").read_text())["colors"]["liquid"]
        self.assertEqual(len(liquids), 12)
        for tint, sizes in liquids.items():
            with self.subTest(tint=tint):
                self.assertEqual(set(sizes), {"small", "medium", "large"})
                self.assertEqual(len({tuple(rgb) for rgb in sizes.values()}), 1)
        tints = sorted(liquids)
        for i, a in enumerate(tints):
            for b in tints[i + 1:]:
                with self.subTest(pair=(a, b)):
                    # CIE76 distance; the replaced palette had pairs at 8.5 and 12.5.
                    self.assertGreaterEqual(np.linalg.norm(lab(liquids[a]["small"]) - lab(liquids[b]["small"])), 30)

    def test_surface_conversion_rejects_unknown_color_encoding(self):
        from items.source_surface import palette_piece
        p, f, _ = model.geometry(self.source)
        model.colored(self.source, p, f, [(120, 80, 40)] * len(f))
        with self.assertRaisesRegex(ValueError, "palette UV colors"):
            palette_piece(self.source, workflow.PALETTE)

    def test_source_repair_closes_missing_face_without_changing_positions(self):
        from items.source_repair import repair
        p = kit.bevel_box((1, 1, 1), .05, "metal")
        broken = kit.Piece(p.verts, p.faces[1:], p.slots[1:])
        fixed, report = repair(broken)
        self.assertEqual(report["topology"]["boundary_edges"], 0)
        self.assertEqual(report["topology"]["duplicate_faces"], 0)
        self.assertGreater(report["added_cap_triangles"], 0)
        self.assertEqual({tuple(v) for v in fixed.verts}, {tuple(v) for v in p.verts})
        self.assertAlmostEqual(solid.inspect(p.verts, p.faces)["signed_volume"], report["topology"]["signed_volume"], places=7)

    def test_source_repair_preserves_nested_ring_opening(self):
        from items.source_repair import repair
        shape = solid.loop((0, 0, 0), (.5, .5), .1, sides=16)
        mesh = shape.to_mesh()
        p, f = mesh.vert_properties[:, :3], mesh.tri_verts
        front = np.all(np.isclose(p[f, 2], p[:, 2].max()), axis=1)
        fixed, report = repair(kit.Piece(p, f[~front], "metal"))
        self.assertEqual(report["topology"]["boundary_edges"], 0)
        self.assertAlmostEqual(report["topology"]["signed_volume"], shape.volume(), places=6,
                               msg="Capping an annulus must preserve its opening, not create a disk")

    def test_transparent_material_does_not_merge_with_opaque_same_finish(self):
        p = kit.Piece.merge(kit.bevel_box((1, 1, 1), .05, "glass"),
                            kit.bevel_box((.2, .2, .2), .01, "solid", center=(0, 1, 0)))
        destination = self.repo / "transparent.glb"
        character_export.export_static(str(destination), "test", p,
            {"glass": (80, 170, 190), "solid": (180, 180, 160)},
            materials={"glass": {"metallic": 0, "roughness": .2, "alpha": .5, "double_sided": True},
                       "solid": {"metallic": 0, "roughness": .2}})
        g, _ = model.read(destination)
        self.assertEqual(len(g["materials"]), 2)
        transparent = [m for m in g["materials"] if m.get("alphaMode") == "BLEND"]
        self.assertEqual(len(transparent), 1)
        self.assertEqual(transparent[0]["pbrMetallicRoughness"]["baseColorFactor"][3], .5)
        self.assertTrue(transparent[0]["doubleSided"])

    def test_catalog_material_roles_keep_food_matte_and_keys_neutral(self):
        from items.preserve import appearance
        swatches = {"gold": (234, 194, 83), "crimson": (210, 34, 39)}
        slots, colors, materials = appearance("food", "food_cheese", swatches)
        self.assertTrue(all(m["metallic"] == 0 for m in materials.values()))
        slots, colors, materials = appearance("keys", "key_iron", swatches)
        self.assertTrue(all(max(rgb) - min(rgb) < 30 for rgb in colors.values()))

    def test_scroll_markings_become_surface_color_without_raised_shells(self):
        from items.surface_marks import scroll_ink
        paper = kit.bevel_box((.48, .5, .02), 0, "paper", center=(0, .32, .012))
        glyph = kit.bevel_box((.05, .3, .01), .001, "ink", center=(0, .32, .06))
        painted, report = scroll_ink(kit.Piece.merge(paper, glyph))
        self.assertGreater(report["removed_mark_triangles"], 0)
        self.assertGreater(report["painted_area"], .014)
        self.assertAlmostEqual(painted.verts[:, 2].max(), paper.verts[:, 2].max(), places=7)
        ink = painted.verts[painted.faces[painted.slots == "ink"]]
        np.testing.assert_allclose(ink[:, :, 2], paper.verts[:, 2].max(), atol=1e-7)
        topology = solid.inspect(painted.verts, painted.faces)
        self.assertEqual(topology["components"], 1)
        self.assertEqual(topology["boundary_edges"], 0)
        self.assertEqual(topology["nonmanifold_edges"], 0)

    def test_scroll_ink_covers_the_bowed_parchment_center(self):
        from items.surface_marks import scroll_ink
        paper = kit.bevel_box((.48, .5, .02), 0, "paper", center=(0, .32, .012))
        # Raise the central supporting surface beyond the old .030 cutoff.
        paper.verts[:, 2] += .025
        glyph = kit.bevel_box((.05, .3, .01), .001, "ink", center=(0, .32, .07))
        painted, report = scroll_ink(kit.Piece.merge(paper, glyph))
        self.assertGreater(report["painted_area"], .014)
        self.assertAlmostEqual(report["missing_symbol_area"], 0, places=8)
        ink = painted.verts[painted.faces[painted.slots == "ink"]]
        self.assertLess(ink[:, :, 1].min(), .18)
        self.assertGreater(ink[:, :, 1].max(), .46)
        np.testing.assert_allclose(ink[:, :, 2], paper.verts[:, 2].max(), atol=1e-7)

    def test_original_scroll_symbols_survive_projection_and_float32_export(self):
        from items.surface_marks import scroll_ink
        from items.source_surface import palette_piece
        from items.source_repair import repair
        studio = Path(__file__).resolve().parents[1]
        sources = sorted((studio / "objects/scrolls").glob("*/source.glb"))
        self.assertEqual(len(sources), 12)
        for source in sources:
            with self.subTest(scroll=source.parent.name):
                piece, _ = palette_piece(source, studio / "rigs/item_palette.png")
                painted, report = scroll_ink(piece)
                self.assertAlmostEqual(report["missing_symbol_area"], 0, places=8)
                painted, _ = repair(painted)
                tri = painted.verts.astype(np.float32).astype(float)[painted.faces]
                areas = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
                self.assertTrue(np.all(areas > 1e-12))
                center = tri.mean(axis=1)
                # The black rune shares its source swatch with frame accents.
                on_paper = (np.abs(center[:, 0]) < .23) & (center[:, 1] > .08) & (center[:, 1] < .56)
                ink = tri[(painted.slots == report["ink_slot"]) & on_paper]
                projected_area = np.abs(np.cross(ink[:, 1] - ink[:, 0], ink[:, 2] - ink[:, 0])[:, 2]).sum() / 2
                self.assertAlmostEqual(projected_area, report["symbol_area"], places=7)
                topology = solid.inspect(painted.verts, painted.faces)
                self.assertEqual(topology["boundary_edges"], 0)
                self.assertEqual(topology["nonmanifold_edges"], 0)

    def test_cheese_spots_are_removed_and_replaced_with_real_holes(self):
        from items.cheese import carve_holes
        base = kit.bevel_box((.48, .28, .28), .005, "surface_amber", center=(0, .14, 0))
        spots = []
        for center in [(-.08, .28, -.02), (.01, .28, .01), (-.03, .14, .14), (.1, .17, .14)]:
            mesh = solid.mf.Manifold.sphere(.03, 12).translate(center).to_mesh()
            spots.append(kit.Piece(mesh.vert_properties[:, :3], mesh.tri_verts, "surface_rust"))
        result, report = carve_holes(kit.Piece.merge(base, *spots))
        self.assertEqual(report["recesses"], 4)
        self.assertEqual(report["through_holes"], 1)
        self.assertGreater(report["carved_volume"], 0)
        topology = solid.inspect(result.verts, result.faces)
        self.assertEqual(topology["components"], 1)
        self.assertEqual(topology["boundary_edges"], 0)
        self.assertLess(topology["signed_volume"], solid.inspect(base.verts, base.faces)["signed_volume"])

    def test_lineup_composition_keeps_candidates_outside_authoring_and_catalog(self):
        from PIL import Image
        item = self.item()
        item.build()
        frames = item.built.parent / "lineup"
        frames.mkdir()
        for name in ("front", "three_quarter", "side", "game_context"):
            Image.new("RGB", (32, 32), "green").save(frames / (name + ".png"))
        item.compose_lineup()
        self.assertTrue((item.built.parent / "review.json").exists())
        self.assertFalse(item.built.is_relative_to(self.repo))
        self.assertFalse((item.folder / "lineup.png").exists())
        self.assertFalse((self.repo / "asset-studio/catalogs").exists())

    def test_current_catalog_uses_lineups_and_replaces_stale_outputs(self):
        from items.catalogs import write_catalogs
        from PIL import Image, ImageDraw
        item = self.item()
        item.build()
        self.assertFalse((item.built.parent / "preview.png").exists())
        item.preview()
        before = item.path.read_bytes()
        image = Image.new("RGB", (1440, 400), "black")
        ImageDraw.Draw(image).rectangle((360, 40, 719, 399), fill=(30, 170, 90))
        image.save(item.built.parent / "lineup.png")
        from core import publication
        publication.render_stamp(item.built.parent, "test-inputs", (item.built.name, "lineup.png"))
        destination = self.repo / "catalogs/items"
        destination.mkdir(parents=True)
        Image.new("RGB", (8, 8)).save(destination / "superseded.png")
        publication.publish(item.built.parent, destination / item.key, item.key, "test-inputs",
                            (item.built.name, "lineup.png"), {"recipe": item.recipe(), "report": item.validate()})
        write_catalogs([item], destination)
        self.assertEqual(item.path.read_bytes(), before)
        self.assertFalse((destination / "superseded.png").exists())
        self.assertEqual({p.name for p in destination.iterdir()}, {"overview.png", "tools.png", "tools", "README.md", "status.json"})
        with Image.open(destination / "tools.png") as sheet:
            self.assertEqual(sheet.getpixel((140, 190)), (30, 170, 90))
        status = json.loads((destination / "status.json").read_text())
        self.assertEqual(status["items"], 1)
        self.assertEqual(status["godot_verified_exports"], 0)
        self.assertTrue((item.built.parent / "validation.json").exists())
        self.assertTrue((item.built.parent / "preview.png").exists())
        self.assertFalse((item.folder / "validation.json").exists())
        self.assertFalse((item.folder / "preview.png").exists())
        # Final catalogs remain usable when all disposable products have vanished.
        shutil.rmtree(item.built.parent)
        write_catalogs([item], destination)
        self.assertEqual(json.loads((destination / "status.json").read_text())["items"], 1)

    def test_recreation_persists_design_without_using_or_overwriting_old_geometry(self):
        from items.recreate import recreate
        path = self.repo / "assets/custom-items/tools/lamp/tool_lantern_brass.glb"
        path.parent.mkdir(parents=True)
        path.write_bytes(self.source.read_bytes())
        workflow.adopt(path)
        item = workflow.Item("tools/tool_lantern_brass")
        self.assertTrue(recreate(item))
        data = item.recipe()
        self.assertEqual(data["builder"], "lamp")
        self.assertEqual(data["design"], {"kind": "lantern"})
        self.assertNotIn("source", data)
        self.assertEqual(data["export"]["glb"], "res://assets/custom-items/tools/lamp/tool_lantern_brass.glb")
        self.assertEqual(self.source.read_bytes(), path.read_bytes())
        self.assertFalse(recreate(item))


if __name__ == "__main__":
    unittest.main()
