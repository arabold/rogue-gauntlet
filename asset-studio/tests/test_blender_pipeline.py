"""Blender-authored sources: material-slot ingestion, provenance, concept scoring and host fit.

The Blender runner tests need the pinned Blender (`asset-studio/blender/blender.json`) and skip without it; every
other test reads committed files only.
"""
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core import blender, concept_score, hosts, kit
from core.export import _Writer
from items import model
from items.source_surface import material_piece

STUDIO = Path(__file__).resolve().parents[1]
SHAMAN = STUDIO / "objects/headgear/shaman_antler_headdress"


def _blender_ready():
    try:
        return blender.version(blender.binary()).startswith(blender.pinned())
    except (ValueError, OSError):
        return False


def _triangle_glb(path, material):
    """One triangle with normals and a single material named `material`."""
    w = _Writer()
    position = w.add(np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0]], np.float32), "VEC3", 5126, 34962, bounds=True)
    normal = w.add(np.array([[0, 0, 1]] * 3, np.float32), "VEC3", 5126, 34962)
    indices = w.add(np.array([0, 1, 2], np.uint32), "SCALAR", 5125, 34963)
    w.g["meshes"] = [{"primitives": [{"attributes": {"POSITION": position, "NORMAL": normal}, "indices": indices,
                                       "material": 0}]}]
    w.g["materials"] = [{"name": material, "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1]}}]
    w.save(str(path))


def _disk(size, radius, center):
    yy, xx = np.mgrid[:size, :size]
    return (yy - center[1]) ** 2 + (xx - center[0]) ** 2 <= radius ** 2


class MaterialSlotIngestionTest(unittest.TestCase):
    def test_slots_come_from_material_names_and_authored_normals_survive(self):
        piece = material_piece(SHAMAN / "source.glb")
        self.assertEqual(set(piece.slots), {"leather", "leather_light", "leather_dark", "bone", "bone_dark", "socket",
                                            "feather", "feather_tip"})
        self.assertEqual(piece.normals.shape, (len(piece.faces), 3, 3))
        self.assertTrue(np.allclose(np.linalg.norm(piece.normals, axis=2), 1.0))
        # Faceted parts keep their flat authored normals: every corner of a facet points the facet's way.
        tri = piece.verts[piece.faces]
        facet = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        facet /= np.linalg.norm(facet, axis=1, keepdims=True)
        agree = np.einsum("fcj,fj->fc", piece.normals, facet)
        self.assertGreater(np.mean(agree > 0.999), 0.5)

    def test_names_that_are_not_slots_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "source.glb"
            _triangle_glb(path, "trim.001")
            with self.assertRaisesRegex(ValueError, "not a palette slot"):
                material_piece(path)
            _triangle_glb(path, "trim")
            self.assertEqual(list(material_piece(path, {"trim": "gold"}).slots), ["gold"])


class ModelProvenanceTest(unittest.TestCase):
    def test_a_changed_script_or_host_marks_the_committed_model_stale(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            script, glb, host = tmp / "source.py", tmp / "source.glb", tmp / "host.glb"
            script.write_text("def build(ctx):\n    pass\n")
            shutil.copy2(SHAMAN / "source.glb", glb)
            shutil.copy2(hosts.path("adventurer_heads"), host)
            sidecar = tmp / "source.json"
            sidecar.write_text(__import__("json").dumps(blender.provenance(script, host, glb)))
            self.assertEqual(blender.stale(sidecar, script, host, glb), [])
            script.write_text("def build(ctx):\n    ctx.origin.z = 1\n")
            self.assertEqual(blender.stale(sidecar, script, host, glb), ["source.py changed since `item model`"])
            self.assertEqual(len(blender.stale(tmp / "missing.json", script, host, glb)), 1)


class ConceptScoreTest(unittest.TestCase):
    def test_one_shared_scale_exposes_a_view_modeled_too_large(self):
        size = 300
        concept = _disk(size, 100, (150, 150))
        fits = []
        for radius in (50, 60):  # the second view's form is 20% larger than in the other views
            crop = _disk(2 * radius + 2, radius, (radius, radius))
            rgba = np.dstack([np.repeat(crop[..., None], 3, axis=2).astype(np.uint8) * 200, crop.astype(np.uint8) * 255])
            fits.append((rgba, crop))
        free = [concept_score.align(rgba, m, concept)[3] for rgba, m in fits]
        self.assertGreater(min(f["iou"] for f in free), 0.95)  # rescaling each view on its own hides the error
        shared = concept_score.align(fits[1][0], fits[1][1], concept, scale=free[0]["scale"])[3]
        self.assertLess(shared["iou"], 0.8)


class HostFitTest(unittest.TestCase):
    @staticmethod
    def _ball(center, radius):
        theta, phi = np.meshgrid(np.linspace(0, 2 * np.pi, 24, endpoint=False), np.linspace(0.1, np.pi - 0.1, 12))
        points = np.column_stack([np.sin(phi).ravel() * np.cos(theta).ravel(), np.cos(phi).ravel(),
                                  np.sin(phi).ravel() * np.sin(theta).ravel()]) * radius + center
        points = np.vstack([points, center + [0, radius, 0], center - [0, radius, 0]])
        return kit.hull(list(points), "metal")

    def test_shells_that_enclose_the_head_hide_it_and_smaller_ones_show_it(self):
        verts, _ = hosts.read("adventurer_heads")["barbarian"]
        center = (verts.min(axis=0) + verts.max(axis=0)) / 2
        enclosing = self._ball(center, 1.0)
        self.assertEqual(hosts.show_through("adventurer_heads", enclosing.verts, enclosing.faces)["barbarian"]["vertices"], 0)
        small = self._ball(center, 0.3)
        shown = hosts.show_through("adventurer_heads", small.verts, small.faces)["barbarian"]
        self.assertGreater(shown["vertices"], 100)
        self.assertGreater(shown["depth"], 0.1)


@unittest.skipUnless(_blender_ready(), "needs the pinned Blender")
class BlenderRunnerTest(unittest.TestCase):
    def _job(self, tmp, script):
        path = Path(tmp) / "source.py"
        path.write_text(script)
        return {"script": str(path), "host": str(hosts.path("adventurer_heads")), "palette": {"metal": [160, 165, 175]},
                "glb": str(Path(tmp) / "out.glb"), "report": str(Path(tmp) / "report.json")}

    def test_only_model_parts_are_exported_with_slot_materials(self):
        with tempfile.TemporaryDirectory() as tmp:
            job = self._job(tmp, "import bmesh\n\ndef build(ctx):\n    bm = bmesh.new()\n"
                                 "    bmesh.ops.create_cube(bm, size=0.2)\n    ctx.add('Block', bm, ['metal'])\n")
            report = blender.run(job, tmp)
            g, _ = model.read(job["glb"])
            self.assertEqual([n["name"] for n in g["nodes"]], ["Block"])
            self.assertEqual([m["name"] for m in g["materials"]], ["metal"])
            self.assertEqual(report["objects"][0]["open_edges"], 0)

    def test_a_failing_script_fails_the_job_without_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            job = self._job(tmp, "def build(ctx):\n    raise RuntimeError('broken model')\n")
            with self.assertRaisesRegex(ValueError, "broken model"):
                blender.run(job, tmp)
            self.assertFalse(Path(job["glb"]).exists())


if __name__ == "__main__":
    unittest.main()
