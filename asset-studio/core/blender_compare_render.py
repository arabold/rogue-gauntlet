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
from mathutils.bvhtree import BVHTree


# ---------------------------------------------------------------------------
# Reusable BMesh Modeling & KayKit Head-Clearance Utilities
# ---------------------------------------------------------------------------


def make_pbr_mat(
	name: str,
	base_color: tuple[float, float, float, float],
	metallic: float = 0.28,
	roughness: float = 0.42,
	spec_ior: float = 0.5,
) -> bpy.types.Material:
	"""Create or update a Principled BSDF material with stylized KayKit PBR settings."""
	mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
	mat.use_nodes = True
	bsdf = mat.node_tree.nodes.get("Principled BSDF")
	if bsdf:
		rgba = (*base_color, 1.0) if len(base_color) == 3 else base_color
		bsdf.inputs["Base Color"].default_value = rgba
		bsdf.inputs["Metallic"].default_value = metallic
		bsdf.inputs["Roughness"].default_value = roughness
		if "Specular IOR Level" in bsdf.inputs:
			bsdf.inputs["Specular IOR Level"].default_value = spec_ior
	return mat


def get_or_create_workbench(name: str = "COL_Workbench") -> bpy.types.Collection:
	"""Return an empty `COL_Workbench` collection, removing any prior draft meshes cleanly."""
	wb = bpy.data.collections.get(name)
	if wb is None:
		wb = bpy.data.collections.new(name)
		bpy.context.scene.collection.children.link(wb)
	for obj in list(wb.objects):
		m = obj.data
		bpy.data.objects.remove(obj, do_unlink=True)
		if m and m.users == 0:
			bpy.data.meshes.remove(m)
	return wb


def apply_auto_smooth_sharp_edges(
	bm: bmesh.types.BMesh,
	angle_deg: float = 28.0,
	preserve_manual_sharp: bool = True,
) -> None:
	"""Enable smooth shading on faces and mark edges sharper than `angle_deg` as sharp."""
	bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
	thresh = math.radians(angle_deg)
	for f in bm.faces:
		f.smooth = True
	for e in bm.edges:
		if preserve_manual_sharp and not e.smooth:
			continue
		if e.is_boundary or len(e.link_faces) != 2:
			e.smooth = False
		else:
			f0, f1 = e.link_faces[0], e.link_faces[1]
			if f0.normal.length > 1e-4 and f1.normal.length > 1e-4:
				ang = f0.normal.angle(f1.normal)
				e.smooth = (ang < thresh)
			else:
				e.smooth = True


def finalize_and_link_mesh(
	wb: bpy.types.Collection,
	name: str,
	bm: bmesh.types.BMesh,
	mats: list[bpy.types.Material],
	sharp_angle_deg: float | None = 28.0,
	split_sharp: bool = True,
) -> bpy.types.Object:
	"""Convert BMesh to a linked Object, physically splitting sharp edges so Blender 4.x/5.x EEVEE renders hard bevels cleanly."""
	bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
	bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
	if sharp_angle_deg is not None:
		apply_auto_smooth_sharp_edges(bm, angle_deg=sharp_angle_deg, preserve_manual_sharp=True)
	if split_sharp:
		sharp_edges = [e for e in bm.edges if not e.smooth]
		if sharp_edges:
			bmesh.ops.split_edges(bm, edges=sharp_edges)
	mesh = bpy.data.meshes.new(name)
	bm.to_mesh(mesh)
	bm.free()
	for m in mats:
		mesh.materials.append(m)
	obj = bpy.data.objects.new(name, mesh)
	wb.objects.link(obj)
	return obj


def enforce_head_clearance_on_grid(
	grid: list[list[Vector]],
	head_name: str = "Mannequin_Medium_Head",
	min_outer_clearance: float = 0.056,
	center: Vector = Vector((0.0, -0.02, 0.05)),
) -> None:
	"""Ensure every outer shell grid point clears `Mannequin_Medium_Head` (including its boxy 135-deg rear corners) by `min_outer_clearance`."""
	head_obj = bpy.data.objects.get(head_name)
	if head_obj is None or head_obj.type != "MESH":
		return
	hbm = bmesh.new()
	hbm.from_mesh(head_obj.data)
	hbm.transform(head_obj.matrix_world)
	bmesh.ops.triangulate(hbm, faces=hbm.faces)
	bvh = BVHTree.FromBMesh(hbm)
	hbm.free()

	for i in range(len(grid)):
		for j in range(len(grid[i])):
			p = grid[i][j]
			d_vec = p - center
			dist_p = d_vec.length
			if dist_p < 1e-4:
				continue
			ray_dir = d_vec / dist_p
			loc, _norm, _idx, _dist = bvh.ray_cast(center, ray_dir, 2.0)
			if loc is not None:
				head_r = (loc - center).length
				req_r = head_r + min_outer_clearance
				if dist_p < req_r:
					grid[i][j] = center + ray_dir * req_r


def solidify_open_shell(
	bm: bmesh.types.BMesh,
	thickness: float = 0.048,
	inner_mat_idx: int = 1,
	horizontal_only_verts: set[bmesh.types.BMVert] | None = None,
) -> None:
	"""Give an open surface mesh solid inward thickness without dipping brow cutout rims below exterior trim."""
	bm.verts.ensure_lookup_table()
	bm.edges.ensure_lookup_table()
	bm.faces.ensure_lookup_table()
	bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

	orig_verts = list(bm.verts)
	orig_faces = list(bm.faces)
	orig_boundary_edges = [e for e in bm.edges if e.is_boundary]

	inner_map = {}
	for v in orig_verts:
		radial = (v.co - Vector((0.0, -0.02, 0.02)))
		radial.z *= 0.30
		if radial.length > 1e-4:
			radial.normalize()
		n = v.normal.copy()
		if n.length > 1e-4:
			dir_in = -(0.60 * n + 0.40 * radial).normalized()
		else:
			dir_in = -radial
		if horizontal_only_verts and v in horizontal_only_verts:
			dir_in.z = max(dir_in.z, 0.02)
			if dir_in.length > 1e-4:
				dir_in.normalize()
		iv = bm.verts.new(v.co + dir_in * thickness)
		if abs(v.co.x) < 1e-4:
			iv.co.x = 0.0
		inner_map[v] = iv

	for f in orig_faces:
		iverts = [inner_map[v] for v in reversed(f.verts)]
		nf = bm.faces.new(iverts)
		nf.material_index = inner_mat_idx
		nf.smooth = True

	for e in orig_boundary_edges:
		v0, v1 = e.verts[0], e.verts[1]
		loop = e.link_loops[0]
		if loop.vert == v0:
			rf = bm.faces.new((v0, inner_map[v0], inner_map[v1], v1))
		else:
			rf = bm.faces.new((v1, inner_map[v1], inner_map[v0], v0))
		rf.material_index = 0
		rf.smooth = True
		e.smooth = False
		inner_e = bm.edges.get((inner_map[v0], inner_map[v1]))
		if inner_e:
			inner_e.smooth = False

	bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def inset_quad_slot_pockets(
	bm: bmesh.types.BMesh,
	face_groups: list[list[bmesh.types.BMFace]],
	border_thickness: float = 0.019,
	depth: float = -0.028,
	bevel_thickness: float = 0.003,
	outer_mat_idx: int = 0,
	slot_mat_idx: int = 1,
) -> None:
	"""Create recessed ventilation slots or panel grooves directly in BMesh without fragile Boolean modifiers."""
	for group in face_groups:
		res_border = bmesh.ops.inset_region(
			bm,
			faces=group,
			use_boundary=True,
			use_even_offset=True,
			thickness=border_thickness,
			depth=0.0,
		)
		for f_b in res_border.get("faces", []):
			f_b.material_index = outer_mat_idx
			f_b.smooth = True

		group_set = set(group)
		slot_verts_outer = {v for f in group for v in f.verts}
		for e in bm.edges:
			if e.verts[0] in slot_verts_outer and e.verts[1] in slot_verts_outer:
				linked_in_group = sum(1 for lf in e.link_faces if lf in group_set)
				e.smooth = (linked_in_group >= 2)

		res_pocket = bmesh.ops.inset_region(
			bm,
			faces=group,
			use_boundary=True,
			use_even_offset=True,
			thickness=bevel_thickness,
			depth=depth,
		)
		for f_wall in res_pocket.get("faces", []):
			f_wall.material_index = slot_mat_idx
			f_wall.smooth = True
		for f_floor in group:
			f_floor.material_index = slot_mat_idx
			f_floor.smooth = True

		slot_verts_inner = {v for f in group for v in f.verts}
		for e in bm.edges:
			if e.verts[0] in slot_verts_inner and e.verts[1] in slot_verts_inner:
				linked_in_group = sum(1 for lf in e.link_faces if lf in group_set)
				e.smooth = (linked_in_group >= 2)


def build_lofted_tube_sharp_rails(
	bm: bmesh.types.BMesh,
	rings: list[list[bmesh.types.BMVert]],
	flip_winding: bool = False,
	cap_start: bool = True,
	cap_end: bool = True,
	sharp_stations: tuple[int, ...] = (),
) -> None:
	"""Loft a sequence of cross-section vertex rings with sharp longitudinal bevel rails and smooth cross-segments."""
	nseg = len(rings[0])
	for s in range(len(rings) - 1):
		r0, r1 = rings[s], rings[s + 1]
		for k in range(nseg):
			kn = (k + 1) % nseg
			verts = (r0[k], r1[k], r1[kn], r0[kn]) if flip_winding else (r0[k], r0[kn], r1[kn], r1[k])
			if len(set(verts)) == 4:
				f = bm.faces.new(verts)
				f.smooth = True

	rail_edges = set()
	for s in range(len(rings) - 1):
		r0, r1 = rings[s], rings[s + 1]
		for k in range(nseg):
			e = bm.edges.get((r0[k], r1[k]))
			if e is not None:
				rail_edges.add(e)

	for s_idx in sharp_stations:
		if 0 <= s_idx < len(rings):
			st = rings[s_idx]
			for k in range(nseg):
				e = bm.edges.get((st[k], st[(k + 1) % nseg]))
				if e is not None:
					rail_edges.add(e)

	if cap_start:
		c0 = list(rings[0]) if flip_winding else list(reversed(rings[0]))
		f0 = bm.faces.new(c0)
		f0.smooth = True
		for k in range(nseg):
			e = bm.edges.get((rings[0][k], rings[0][(k + 1) % nseg]))
			if e:
				rail_edges.add(e)

	if cap_end:
		c1 = list(reversed(rings[-1])) if flip_winding else list(rings[-1])
		f1 = bm.faces.new(c1)
		f1.smooth = True
		for k in range(nseg):
			e = bm.edges.get((rings[-1][k], rings[-1][(k + 1) % nseg]))
			if e:
				rail_edges.add(e)

	for e in bm.edges:
		if e in rail_edges:
			e.smooth = False
		elif not e.is_boundary:
			e.smooth = True


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
	lens: float = 135.0,
	distance: float = 10.2,
	res: int = 1024,
) -> tuple[list[str], list[str]]:
	"""Render `(solid_paths, wire_paths)` for `[front, tq, side]` to `out_dir` using telephoto studio framing (`lens=135, distance=10.2`) to match 2D turnaround sheets."""
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

		targets = [(o, wire_helm_mat) for o in wb_objs if o != head_obj]
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


def render_character_showcase(
	out_dir: str,
	prefix: str = "char",
	workbench_col: str = "COL_Workbench",
	character_glb: str | None = None,
	hide_ears: bool = False,
	hide_hair: bool = True,
	scale_f: float = 1.000,
	y_shift: float = -0.0155,
	z_shift: float = 0.4018,
	turntable_frames: int = 36,
) -> tuple[list[str], list[str], list[str]]:
	"""Render the `COL_Workbench` headgear mounted on a rigged KayKit Adventurer character (full-body views, portraits, and 360 turntable frames)."""
	from mathutils import Matrix

	os.makedirs(out_dir, exist_ok=True)
	sc = bpy.context.scene
	wb = bpy.data.collections.get(workbench_col)
	if wb is None:
		raise ValueError(f"Collection not found: {workbench_col}")
	wb_objs = [o for o in wb.objects if o.type == "MESH" and not o.hide_viewport]
	if not wb_objs:
		raise ValueError(f"No visible meshes in {workbench_col}")

	if character_glb is None:
		repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
		character_glb = os.path.join(repo_root, "assets", "kaykit-adventurers", "Knight.glb")

	orig_hide_render = {o.name: o.hide_render for o in sc.objects}
	for o in sc.objects:
		o.hide_render = True

	orig_state = (
		sc.render.engine,
		sc.render.resolution_x,
		sc.render.resolution_y,
		sc.render.resolution_percentage,
		sc.render.film_transparent,
		sc.camera,
		sc.render.image_settings.file_format,
		sc.render.image_settings.color_mode,
		sc.frame_current,
	)

	before_objs = set(bpy.data.objects)
	before_acts = set(bpy.data.actions)
	before_mats = set(bpy.data.materials)
	before_meshes = set(bpy.data.meshes)

	bpy.ops.import_scene.gltf(filepath=character_glb)

	added_objs = [o for o in bpy.data.objects if o not in before_objs]
	added_acts = [a for a in bpy.data.actions if a not in before_acts]

	rig = next(o for o in added_objs if o.type == "ARMATURE")
	khelm = next(o for o in added_objs if any(k in o.name for k in ("_Helmet", "_Hat", "_Hood")))
	khead = next(o for o in added_objs if "_Head" in o.name and o != khelm)

	keep_tokens = ("_Body", "_ArmLeft", "_ArmRight", "_LegLeft", "_LegRight", "_Cape", "_Head", "_Helmet", "_Hat", "_Hood", "1H_Sword", "Badge_Shield", "1H_Axe", "2H_Axe")
	for o in added_objs:
		if o.type == "MESH":
			if any(tok in o.name for tok in keep_tokens) and "Offhand" not in o.name and o.name != "1H_Axe":
				o.hide_render = False
			else:
				o.hide_render = True

	rig.data.pose_position = "REST"
	bpy.context.view_layer.update()

	hbm = bmesh.new()
	hbm.from_mesh(khead.data)
	hbm.verts.ensure_lookup_table()
	visited = set()
	to_delete = []
	for v in hbm.verts:
		if v.index in visited:
			continue
		comp = []
		stack = [v]
		visited.add(v.index)
		while stack:
			cur = stack.pop()
			comp.append(cur)
			for e in cur.link_edges:
				nv = e.other_vert(cur)
				if nv.index not in visited:
					visited.add(nv.index)
					stack.append(nv)
		zs = [u.co.z for u in comp]
		xs = [u.co.x for u in comp]
		is_hair = min(zs) > 1.42 and max(zs) > 2.02
		is_ear = min(abs(x) for x in xs) > 0.35
		if (hide_hair and is_hair) or (hide_ears and is_ear):
			to_delete.extend(comp)
		elif hide_hair and min(zs) < 1.35 and max(zs) > 2.02:
			# If a character's top hair tuft is welded to the skin cranium (e.g. Barbarian_Head), tuck vertices above z=1.94 inside the cap
			for u in comp:
				if u.co.z > 1.94:
					u.co.z = 1.94
					u.co.x *= 0.85
					u.co.y *= 0.85

	if to_delete:
		bmesh.ops.delete(hbm, geom=to_delete, context="VERTS")
	hbm.to_mesh(khead.data)
	hbm.free()

	combined_bm = bmesh.new()
	mat_list: list[bpy.types.Material] = []
	mat_map: dict[str, int] = {}

	for src_obj in wb_objs:
		tbm = bmesh.new()
		tbm.from_mesh(src_obj.data)
		tbm.transform(src_obj.matrix_world)
		for v in tbm.verts:
			v.co = Vector((v.co.x * scale_f, v.co.y * scale_f + y_shift, v.co.z * scale_f + z_shift))

		local_to_global_mat = {}
		for idx, m in enumerate(src_obj.data.materials):
			if m is None:
				continue
			if m.name not in mat_map:
				mat_map[m.name] = len(mat_list)
				mat_list.append(m)
			local_to_global_mat[idx] = mat_map[m.name]

		for f in tbm.faces:
			f.material_index = local_to_global_mat.get(f.material_index, 0)

		tmp_mesh = bpy.data.meshes.new("_TmpMergePart")
		tbm.to_mesh(tmp_mesh)
		tbm.free()
		combined_bm.from_mesh(tmp_mesh)
		bpy.data.meshes.remove(tmp_mesh)

	new_helm_mesh = bpy.data.meshes.new("_MountedHeadgearMesh")
	combined_bm.to_mesh(new_helm_mesh)
	combined_bm.free()
	for m in mat_list:
		new_helm_mesh.materials.append(m)
	khelm.data = new_helm_mesh

	idle_act = next((a for a in added_acts if a.name.startswith("Idle.") or a.name == "Idle"), None)
	if idle_act:
		if not rig.animation_data:
			rig.animation_data_create()
		rig.animation_data.action = idle_act
	rig.data.pose_position = "POSE"
	sc.frame_set(1)
	bpy.context.view_layer.update()

	full_target = Vector((0.0, 0.0, 1.38))
	head_target = Vector((0.0, -0.05, 1.88))

	light_specs = [
		("_CharKey", Vector((-2.8, -3.4, 2.8)), 380.0, 2.2, (1.0, 0.97, 0.92)),
		("_CharFill", Vector((3.0, -2.5, 0.8)), 65.0, 3.4, (0.78, 0.87, 1.0)),
		("_CharRim", Vector((1.8, 3.4, 2.4)), 280.0, 1.8, (0.88, 0.94, 1.0)),
		("_CharBackFill", Vector((-1.8, 3.2, 0.9)), 70.0, 2.8, (0.86, 0.90, 0.98)),
	]
	lights = [
		_spawn_area_light(sc, nm, full_target + off, full_target, en, sz, col)
		for nm, off, en, sz, col in light_specs
	]

	def update_lights_for_yaw(yaw_rad: float, target_pt: Vector) -> None:
		rot = Matrix.Rotation(yaw_rad, 4, "Z")
		for (lo, _ld), (_nm, off, _en, _sz, _col) in zip(lights, light_specs):
			loc = target_pt + (rot @ off)
			lo.location = loc
			lo.rotation_euler = (target_pt - loc).normalized().to_track_quat("-Z", "Y").to_euler()

	world_bg = sc.world.node_tree.nodes.get("Background") if (sc.world and sc.world.use_nodes) else None
	orig_bg_str = world_bg.inputs["Strength"].default_value if world_bg else None
	orig_bg_col = tuple(world_bg.inputs["Color"].default_value) if world_bg else None
	if world_bg:
		world_bg.inputs["Color"].default_value = (0.58, 0.64, 0.75, 1.0)
		world_bg.inputs["Strength"].default_value = 0.22

	cam_data = bpy.data.cameras.new("_CharCam")
	cam_data.lens = 85.0
	cam_obj = bpy.data.objects.new("_CharCam", cam_data)
	sc.collection.objects.link(cam_obj)
	sc.camera = cam_obj

	sc.render.engine = "BLENDER_EEVEE"
	sc.render.resolution_percentage = 100
	sc.render.film_transparent = True
	sc.render.image_settings.file_format = "PNG"
	sc.render.image_settings.color_mode = "RGBA"

	full_paths = []
	portrait_paths = []
	turntable_paths = []

	try:
		sc.render.resolution_x = 900
		sc.render.resolution_y = 900
		cam_data.lens = 85.0
		dist_full = 7.4
		pitch_full = math.radians(6.0)
		for vname, yaw_d in [("front", 0.0), ("tq", 34.0), ("side", 90.0), ("back", 180.0)]:
			yaw = math.radians(yaw_d)
			update_lights_for_yaw(yaw, full_target)
			dx = dist_full * math.cos(pitch_full) * math.sin(yaw)
			dy = -dist_full * math.cos(pitch_full) * math.cos(yaw)
			dz = dist_full * math.sin(pitch_full)
			cam_obj.location = full_target + Vector((dx, dy, dz))
			cam_obj.rotation_euler = (full_target - cam_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
			fp = os.path.join(out_dir, f"{prefix}_full_{vname}.png")
			sc.render.filepath = fp
			bpy.ops.render.render(write_still=True)
			full_paths.append(fp)

		dist_port = 4.35
		pitch_port = math.radians(5.0)
		for vname, yaw_d in [("portrait_front", -12.0), ("portrait_tq", 32.0)]:
			yaw = math.radians(yaw_d)
			update_lights_for_yaw(yaw, head_target)
			dx = dist_port * math.cos(pitch_port) * math.sin(yaw)
			dy = -dist_port * math.cos(pitch_port) * math.cos(yaw)
			dz = dist_port * math.sin(pitch_port)
			cam_obj.location = head_target + Vector((dx, dy, dz))
			cam_obj.rotation_euler = (head_target - cam_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
			fp = os.path.join(out_dir, f"{prefix}_{vname}.png")
			sc.render.filepath = fp
			bpy.ops.render.render(write_still=True)
			portrait_paths.append(fp)

		if turntable_frames > 0:
			sc.render.resolution_x = 540
			sc.render.resolution_y = 540
			dist_turn = 7.2
			pitch_turn = math.radians(7.5)
			for i in range(turntable_frames):
				yaw = math.radians(i * (360.0 / turntable_frames))
				update_lights_for_yaw(yaw, full_target)
				dx = dist_turn * math.cos(pitch_turn) * math.sin(yaw)
				dy = -dist_turn * math.cos(pitch_turn) * math.cos(yaw)
				dz = dist_turn * math.sin(pitch_turn)
				cam_obj.location = full_target + Vector((dx, dy, dz))
				cam_obj.rotation_euler = (full_target - cam_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
				fp = os.path.join(out_dir, f"{prefix}_turn_{i:02d}.png")
				sc.render.filepath = fp
				bpy.ops.render.render(write_still=True)
				turntable_paths.append(fp)
	finally:
		if world_bg and orig_bg_str is not None:
			world_bg.inputs["Strength"].default_value = orig_bg_str
			world_bg.inputs["Color"].default_value = orig_bg_col
		for lo, ld in lights:
			bpy.data.objects.remove(lo, do_unlink=True)
			if ld.users == 0:
				bpy.data.lights.remove(ld)
		sc.camera = orig_state[5]
		bpy.data.objects.remove(cam_obj, do_unlink=True)
		bpy.data.cameras.remove(cam_data, do_unlink=True)

		for o in added_objs:
			bpy.data.objects.remove(o, do_unlink=True)
		for a in added_acts:
			bpy.data.actions.remove(a)
		for m in list(bpy.data.meshes):
			if m not in before_meshes and m.users == 0:
				bpy.data.meshes.remove(m)
		for mat in list(bpy.data.materials):
			if mat not in before_mats and mat.users == 0:
				bpy.data.materials.remove(mat)

		for name, was_hidden in orig_hide_render.items():
			obj = bpy.data.objects.get(name)
			if obj:
				obj.hide_render = was_hidden
		(
			sc.render.engine,
			sc.render.resolution_x,
			sc.render.resolution_y,
			sc.render.resolution_percentage,
			sc.render.film_transparent,
		) = orig_state[:5]
		sc.render.image_settings.file_format = orig_state[6]
		sc.render.image_settings.color_mode = orig_state[7]
		sc.frame_set(orig_state[8])

	return full_paths, portrait_paths, turntable_paths
