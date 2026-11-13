"""Headless entry point that runs one model script inside Blender and exports its model (started by `core/blender.py`).

    blender -b --factory-startup --python-exit-code 1 --python blender/runner.py -- job.json

The job names the script, an optional host GLB, the slot palette and the output paths. The scene starts empty; the
host is imported into `COL_Host` and only `COL_Model` is exported. Any error exits nonzero before writing output.
"""

import importlib.util
import json
import sys
from pathlib import Path

import bmesh
import bpy

sys.dont_write_bytecode = True  # no __pycache__ beside committed model scripts and helpers
sys.path.insert(0, str(Path(__file__).resolve().parent))

from modeling.context import Context  # noqa: E402


def _empty_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)


def _load(script):
    spec = importlib.util.spec_from_file_location("asset_model", script)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    if not callable(getattr(module, "build", None)):
        raise ValueError(f"{script} must define build(ctx)")
    return module


def _inspect(obj, palette):
    """Triangle count, open/non-manifold edges and slots of a part as it will be exported."""
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    bm = bmesh.new()
    bm.from_mesh(evaluated.to_mesh())
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    non_manifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    triangles = sum(len(f.verts) - 2 for f in bm.faces)
    bm.free()
    evaluated.to_mesh_clear()
    slots = [m.name for m in obj.data.materials if m]
    unknown = [s for s in slots if s not in palette]
    if unknown:
        raise ValueError(f"{obj.name}: materials {unknown} are not palette slots")
    return {"name": obj.name, "triangles": triangles, "open_edges": open_edges, "non_manifold_edges": non_manifold,
            "slots": slots}


def _export(objects, path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
        export_normals=True, export_texcoords=False, export_tangents=False, export_materials="EXPORT",
        export_vertex_color="NONE", export_cameras=False, export_extras=False, export_animations=False,
        export_skins=False)


def main():
    job = json.loads(Path(sys.argv[sys.argv.index("--") + 1]).read_text())
    _empty_scene()
    ctx = Context(job["palette"], job.get("host"))
    _load(job["script"]).build(ctx)
    objects = ctx.objects()
    if not objects:
        raise ValueError("The model script added no parts")
    report = {"blender": bpy.app.version_string, "objects": [_inspect(obj, job["palette"]) for obj in objects]}
    _export(objects, job["glb"])
    Path(job["report"]).write_text(json.dumps(report, indent=1))


main()
