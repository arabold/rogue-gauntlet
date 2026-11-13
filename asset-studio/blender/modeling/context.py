"""The build context a model script's `build(ctx)` receives.

Blender's frame is the asset's frame: Z up, front toward -Y, rig units (the exporter writes glTF's Y up / +Z front).
Headgear uses the hat-prop frame of its host (`core/hosts.py`), so parts sit where the game wears them.
"""

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from modeling.finish import mark_sharp


def _linear(channel):
    """An sRGB channel (0-255) as Blender's linear color."""
    c = channel / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


class Context:
    """The model's collection, its palette-slot materials and the hosts it is fitted to.

    `origin` is where the model's own coordinates start, e.g. the centre of the head a helmet was designed around:
    `add` places parts there and `host_bvh` gives hosts in the same coordinates. Set it before using hosts.
    """

    def __init__(self, palette, host=None):
        scene = bpy.context.scene
        self.origin = Vector((0.0, 0.0, 0.0))
        self.palette = palette
        self.collection = bpy.data.collections.new("COL_Model")
        scene.collection.children.link(self.collection)
        self._materials = {}
        self._bvh = {}
        self.hosts = {}
        if host:
            before = set(bpy.data.objects)
            bpy.ops.import_scene.gltf(filepath=host)
            hosts = bpy.data.collections.new("COL_Host")
            scene.collection.children.link(hosts)
            for obj in set(bpy.data.objects) - before:
                for owner in list(obj.users_collection):
                    owner.objects.unlink(obj)
                hosts.objects.link(obj)
                if obj.type == "MESH":
                    self.hosts[obj.name] = obj
            if not self.hosts:
                raise ValueError(f"Host {host} has no meshes")

    def material(self, slot):
        """The material for palette `slot`, named exactly `slot` so the Studio reads it back as that slot."""
        if slot not in self.palette:
            raise ValueError(f"Slot {slot!r} has no color; add it to the recipe's colors")
        if slot not in self._materials:
            material = bpy.data.materials.new(slot)
            if material.name != slot:
                raise ValueError(f"Material name {material.name!r} is taken; slot names must be unique")
            bsdf = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            bsdf.inputs["Base Color"].default_value = (*[_linear(c) for c in self.palette[slot][:3]], 1.0)
            bsdf.inputs["Metallic"].default_value = 0.0
            bsdf.inputs["Roughness"].default_value = 0.85
            self._materials[slot] = material
        return self._materials[slot]

    def host(self, name):
        """A host mesh object by node name (e.g. `barbarian`)."""
        if name not in self.hosts:
            raise ValueError(f"No host mesh {name!r}; available: {sorted(self.hosts)}")
        return self.hosts[name]

    def host_bvh(self, name):
        """A ray-cast tree over host `name` in the model's coordinates (relative to `origin`)."""
        if name not in self._bvh:
            obj = self.host(name)
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            bm.transform(Matrix.Translation(-self.origin) @ obj.matrix_world)
            bmesh.ops.triangulate(bm, faces=bm.faces)
            self._bvh[name] = BVHTree.FromBMesh(bm)
            bm.free()
        return self._bvh[name]

    def add(self, name, bm, slots, sharp_angle=1.0):
        """Finish `bm` as part `name` at `origin`: weld coincident vertices, shade edges sharper than `sharp_angle`
        degrees hard (None keeps only edges the builder marked) and give face material index i the material of
        `slots[i]`. Frees `bm`."""
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        mark_sharp(bm, sharp_angle)
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        for slot in slots:
            mesh.materials.append(self.material(slot))
        obj = bpy.data.objects.new(name, mesh)
        obj.location = self.origin
        if obj.name != name:
            raise ValueError(f"Part name {name!r} is used twice")
        self.collection.objects.link(obj)
        return obj

    def objects(self):
        """The model's parts, by name."""
        return sorted(self.collection.objects, key=lambda obj: obj.name)
