"""Render synchronized transparent solid + hidden-line wireframe passes in Blender.

Run inside an active Blender scene (`execute_blender_code` or `blender -P`) to
produce `(solid_paths, wire_paths)` matching white-background 3-view concept
sheets (`front`, `three_quarter`, `side`) for `asset-studio/core/concept_compare.py`.
Restores original materials, camera, lighting, and object visibility afterward.
"""

from __future__ import annotations

from contextlib import contextmanager
import math
import os
import bpy
import bmesh
from mathutils import Vector


def _make_emit_mat(name: str, rgba: tuple[float, float, float, float]) -> bpy.types.Material:
	m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
	m.use_nodes = True
	t = m.node_tree
	t.nodes.clear()
	mo = t.nodes.new("ShaderNodeOutputMaterial")
	em = t.nodes.new("ShaderNodeEmission")
	em.inputs["Color"].default_value = rgba
	em.inputs["Strength"].default_value = 1.0
	t.links.new(em.outputs["Emission"], mo.inputs["Surface"])
	return m


def _make_holdout_mat(name: str = "_WireHoldoutMat") -> bpy.types.Material:
	m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
	m.use_nodes = True
	nt = m.node_tree
	nt.nodes.clear()
	nout = nt.nodes.new("ShaderNodeOutputMaterial")
	nhold = nt.nodes.new("ShaderNodeHoldout")
	nt.links.new(nhold.outputs["Holdout"], nout.inputs["Surface"])
	return m


def _spawn_area_light(
	sc: bpy.types.Scene,
	name: str,
	location: Vector,
	target: Vector,
	energy: float,
	size: float,
	color: tuple[float, float, float],
) -> tuple[bpy.types.Object, bpy.types.Light]:
	ldata = bpy.data.lights.new(name, type="AREA")
	ldata.energy = energy
	ldata.size = size
	ldata.color = color
	lobj = bpy.data.objects.new(name, ldata)
	lobj.location = location
	lobj.rotation_euler = (target - location).normalized().to_track_quat("-Z", "Y").to_euler()
	sc.collection.objects.link(lobj)
	return lobj, ldata


@contextmanager
def _studio_compare_env(
	sc: bpy.types.Scene,
	head_obj: bpy.types.Object | None,
	target: Vector,
	res: int,
	lens: float,
):
	"""Set up deterministic 3-point studio lighting, camera, and charcoal dummy head; restore on exit."""
	orig_state = (
		sc.render.engine,
		sc.render.resolution_x,
		sc.render.resolution_y,
		sc.render.resolution_percentage,
		sc.render.film_transparent,
		sc.camera,
		sc.render.image_settings.file_format,
		sc.render.image_settings.color_mode,
	)

	cam_data = bpy.data.cameras.new("_CmpCam")
	cam_data.lens = lens
	cam_obj = bpy.data.objects.new("_CmpCam", cam_data)
	sc.collection.objects.link(cam_obj)
	sc.camera = cam_obj

	sc.render.engine = "BLENDER_EEVEE"
	sc.render.resolution_x = res
	sc.render.resolution_y = res
	sc.render.resolution_percentage = 100
	sc.render.film_transparent = True
	sc.render.image_settings.file_format = "PNG"
	sc.render.image_settings.color_mode = "RGBA"

	orig_head_mats = list(head_obj.data.materials) if head_obj else []
	if head_obj:
		dummy_mat = bpy.data.materials.get("_CompareDummyMat") or bpy.data.materials.new("_CompareDummyMat")
		dummy_mat.use_nodes = True
		bsdf = dummy_mat.node_tree.nodes.get("Principled BSDF")
		if bsdf:
			bsdf.inputs["Base Color"].default_value = (0.145, 0.148, 0.172, 1.0)
			bsdf.inputs["Roughness"].default_value = 0.72
		if head_obj.data.materials:
			head_obj.data.materials[0] = dummy_mat
		else:
			head_obj.data.materials.append(dummy_mat)

	orig_light_hide = {}
	for obj in sc.objects:
		if obj.type == "LIGHT" and not obj.name.startswith("_Cmp"):
			orig_light_hide[obj.name] = obj.hide_render
			obj.hide_render = True

	# Stylized 3-point rig so facets, bevel planes, and metal rivets separate crisply
	studio_lights = [
		_spawn_area_light(sc, "_CmpKeyLight", target + Vector((-2.3, -2.9, 2.5)), target, 275.0, 1.7, (1.0, 0.97, 0.92)),
		_spawn_area_light(sc, "_CmpFillLight", target + Vector((2.6, -2.0, 0.6)), target, 38.0, 3.0, (0.76, 0.86, 1.0)),
		_spawn_area_light(sc, "_CmpRimLight", target + Vector((1.6, 2.9, 2.1)), target, 210.0, 1.5, (0.88, 0.94, 1.0)),
		_spawn_area_light(sc, "_CmpBackFillLight", target + Vector((-1.4, 2.8, 0.7)), target, 45.0, 2.6, (0.86, 0.90, 0.98)),
	]

	world_bg = sc.world.node_tree.nodes.get("Background") if (sc.world and sc.world.use_nodes) else None
	orig_bg_str = world_bg.inputs["Strength"].default_value if world_bg else None
	orig_bg_col = tuple(world_bg.inputs["Color"].default_value) if world_bg else None
	if world_bg:
		world_bg.inputs["Color"].default_value = (0.55, 0.62, 0.74, 1.0)
		world_bg.inputs["Strength"].default_value = 0.14

	try:
		yield cam_obj
	finally:
		if head_obj:
			head_obj.data.materials.clear()
			for m in orig_head_mats:
				head_obj.data.materials.append(m)
		if world_bg and orig_bg_str is not None:
			world_bg.inputs["Strength"].default_value = orig_bg_str
			world_bg.inputs["Color"].default_value = orig_bg_col
		for lobj, ldata in studio_lights:
			bpy.data.objects.remove(lobj, do_unlink=True)
			if ldata.users == 0:
				bpy.data.lights.remove(ldata)
		for name, was_hidden in orig_light_hide.items():
			obj = bpy.data.objects.get(name)
			if obj:
				obj.hide_render = was_hidden
		dm = bpy.data.materials.get("_CompareDummyMat")
		if dm and dm.users == 0:
			bpy.data.materials.remove(dm)

		sc.camera = orig_state[5]
		bpy.data.objects.remove(cam_obj, do_unlink=True)
		bpy.data.cameras.remove(cam_data, do_unlink=True)
		(
			sc.render.engine,
			sc.render.resolution_x,
			sc.render.resolution_y,
			sc.render.resolution_percentage,
			sc.render.film_transparent,
		) = orig_state[:5]
		sc.render.image_settings.file_format = orig_state[6]
		sc.render.image_settings.color_mode = orig_state[7]


def _render_orbit_views(
	sc: bpy.types.Scene,
	cam_obj: bpy.types.Object,
	view_specs: list[tuple[str, float, float, float, Vector]],
	out_dir: str,
	prefix: str,
	suffix: str,
) -> list[str]:
	paths = []
	for vname, yaw_deg, pitch_deg, dist, target in view_specs:
		yaw = math.radians(yaw_deg)
		pitch = math.radians(pitch_deg)
		dx = dist * math.cos(pitch) * math.sin(yaw)
		dy = -dist * math.cos(pitch) * math.cos(yaw)
		dz = dist * math.sin(pitch)
		cam_obj.location = target + Vector((dx, dy, dz))
		cam_obj.rotation_euler = (target - cam_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
		fp = os.path.join(out_dir, f"{vname}_{prefix}_{suffix}.png")
		sc.render.filepath = fp
		bpy.ops.render.render(write_still=True)
		paths.append(fp)
	return paths


def _collect_workbench(workbench_col: str, head_name: str):
	head_obj = bpy.data.objects.get(head_name) if head_name else None
	wb = bpy.data.collections.get(workbench_col)
	if wb is None:
		raise ValueError(f"Collection not found: {workbench_col}")
	wb_objs = [o for o in wb.objects if not o.hide_viewport and o.type == "MESH"]
	if not wb_objs:
		raise ValueError(f"No visible meshes in {workbench_col}")
	return wb_objs, head_obj


def render_compare_passes(
	out_dir: str,
	prefix: str = "pass",
	workbench_col: str = "COL_Workbench",
	head_name: str = "Mannequin_Medium_Head",
	tq_yaw_deg: float = 36.0,
	side_yaw_deg: float = 90.0,
	pitch_deg: float = 1.5,
	lens: float = 65.0,
	distance: float = 4.9,
	res: int = 1024,
) -> tuple[list[str], list[str]]:
	"""Render `(solid_paths, wire_paths)` for `[front, tq, side]` to `out_dir`."""
	os.makedirs(out_dir, exist_ok=True)
	sc = bpy.context.scene
	wb_objs, head_obj = _collect_workbench(workbench_col, head_name)

	bbox_objs = list(wb_objs) + ([head_obj] if head_obj and not head_obj.hide_viewport else [])
	pts = [o.matrix_world @ Vector(c) for o in bbox_objs for c in o.bound_box]
	zs = [p.z for p in pts]
	center = Vector((0.0, 0.0, 0.5 * (min(zs) + max(zs))))

	view_specs = [
		("front", 0.0, pitch_deg, distance, center),
		("tq", tq_yaw_deg, pitch_deg, distance, center),
		("side", side_yaw_deg, pitch_deg, distance, center),
	]

	with _studio_compare_env(sc, head_obj, center, res=res, lens=lens) as cam_obj:
		# 1) Solid shaded pass with 3-point studio lighting + charcoal dummy head
		solid_paths = _render_orbit_views(sc, cam_obj, view_specs, out_dir, prefix, "solid")

		# 2) Hidden-line wireframe pass (Holdout occludes back faces; cyan/magenta lines glow)
		holdout_mat = _make_holdout_mat()
		wire_helm_mat = _make_emit_mat("_WireHelmMat", (0.0, 0.92, 1.0, 1.0))
		wire_head_mat = _make_emit_mat("_WireHeadMat", (0.85, 0.35, 1.0, 1.0))

		targets = [(o, wire_helm_mat) for o in wb_objs]
		if head_obj and not head_obj.hide_viewport:
			targets.append((head_obj, wire_head_mat))

		tmp_col = bpy.data.collections.new("_CmpWireTemp")
		sc.collection.children.link(tmp_col)
		created = []
		orig_hide = {}
		try:
			for src, wmat in targets:
				orig_hide[src.name] = src.hide_render
				src.hide_render = True

				h_obj = bpy.data.objects.new(f"_H_{src.name}", src.data.copy())
				h_obj.matrix_world = src.matrix_world.copy()
				h_obj.data.materials.clear()
				h_obj.data.materials.append(holdout_mat)
				tmp_col.objects.link(h_obj)
				created.append(h_obj)

				bm = bmesh.new()
				bm.from_mesh(src.data)
				w_mesh = bpy.data.meshes.new(f"_WM_{src.name}")
				bm.to_mesh(w_mesh)
				bm.free()
				w_obj = bpy.data.objects.new(f"_W_{src.name}", w_mesh)
				w_obj.matrix_world = src.matrix_world.copy()
				w_obj.data.materials.clear()
				w_obj.data.materials.append(wmat)
				mod = w_obj.modifiers.new("Wire", "WIREFRAME")
				mod.thickness = 0.006 if wmat == wire_helm_mat else 0.004
				mod.offset = 0.85
				mod.use_replace = True
				mod.use_even_offset = True
				mod.use_relative_offset = False
				tmp_col.objects.link(w_obj)
				created.append(w_obj)

			wire_paths = _render_orbit_views(sc, cam_obj, view_specs, out_dir, prefix, "wire")
		finally:
			for src, _ in targets:
				src.hide_render = orig_hide[src.name]
			for o in created:
				m = o.data
				bpy.data.objects.remove(o, do_unlink=True)
				if m and m.users == 0:
					bpy.data.meshes.remove(m)
			bpy.data.collections.remove(tmp_col)
			for mat_name in ("_WireHoldoutMat", "_WireHelmMat", "_WireHeadMat"):
				mat = bpy.data.materials.get(mat_name)
				if mat and mat.users == 0:
					bpy.data.materials.remove(mat)

	return solid_paths, wire_paths


def render_junction_closeups(
	out_dir: str,
	prefix: str = "pass",
	workbench_col: str = "COL_Workbench",
	head_name: str = "Mannequin_Medium_Head",
	lens: float = 85.0,
	distance: float = 2.65,
	res: int = 768,
) -> list[str]:
	"""Render 4 macro seam/junction closeups `[brow_nasal, left_socket, right_socket, rear_band]`."""
	os.makedirs(out_dir, exist_ok=True)
	sc = bpy.context.scene
	wb_objs, head_obj = _collect_workbench(workbench_col, head_name)

	pts = [o.matrix_world @ Vector(c) for o in wb_objs for c in o.bound_box]
	xs = [p.x for p in pts]
	ys = [p.y for p in pts]
	zs = [p.z for p in pts]
	cx = 0.5 * (min(xs) + max(xs))
	cy = 0.5 * (min(ys) + max(ys))
	cz = 0.5 * (min(zs) + max(zs))
	span_z = max(zs) - min(zs)
	center = Vector((cx, cy, cz))
	upper_mid = Vector((cx, cy, cz + 0.12 * span_z))

	view_specs = [
		("closeup_brow", -24.0, -4.0, distance, Vector((cx, cy, cz - 0.04 * span_z))),
		("closeup_left", -56.0, 18.0, distance, upper_mid),
		("closeup_right", 56.0, 18.0, distance, upper_mid),
		("closeup_rear", 162.0, 8.0, distance, center),
	]

	with _studio_compare_env(sc, head_obj, center, res=res, lens=lens) as cam_obj:
		return _render_orbit_views(sc, cam_obj, view_specs, out_dir, prefix, "closeup")
