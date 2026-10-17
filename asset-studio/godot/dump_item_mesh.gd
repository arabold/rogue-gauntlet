extends SceneTree
#
# Export an item's mesh (a Weapon `.tres` or any scene) as flat-colored triangles in the scene's own frame, so
# `asset-studio/studio.py` can bake an existing weapon into a character preview. Each triangle gets one color: the
# albedo texture sampled at its UV centroid, times the material color and the average vertex color when the material
# uses them (items color faces from small palette textures, so one sample per face is faithful).
#
#   .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" \
#     --script asset-studio/godot/dump_item_mesh.gd -- <out.json> <res://item.tres|.tscn|.glb>
#
# Output: {"tris": [[x0,y0,z0, x1,y1,z1, x2,y2,z2, r,g,b, metallic, roughness], ...]} with colors in 0-255 and
# the surface material's metallic and roughness (so a baked weapon keeps its gold or steel look).

func _initialize():
	var args = OS.get_cmdline_user_args()
	var res = load(args[1])
	var packed: PackedScene = res if res is PackedScene else res.get("Scene")
	if packed == null:
		push_error("no scene for " + args[1])
		quit(1)
		return
	var root_node: Node3D = packed.instantiate()
	var tris := []
	_collect(root_node, Transform3D.IDENTITY, tris)
	root_node.free()
	var f = FileAccess.open(args[0], FileAccess.WRITE)
	f.store_string(JSON.stringify({"tris": tris}))
	f.close()
	print("dumped %d triangles" % tris.size())
	quit()

func _collect(n: Node, parent_xf: Transform3D, tris: Array):
	var xf = parent_xf
	if n is Node3D:
		xf = parent_xf * (n as Node3D).transform
	if n is MeshInstance3D and n.mesh:
		for s in n.mesh.get_surface_count():
			_surface(n, s, xf, tris)
	for c in n.get_children():
		_collect(c, xf, tris)

func _surface(mi: MeshInstance3D, s: int, xf: Transform3D, tris: Array):
	var arr = mi.mesh.surface_get_arrays(s)
	var verts: PackedVector3Array = arr[Mesh.ARRAY_VERTEX]
	var uvs = arr[Mesh.ARRAY_TEX_UV]
	var cols = arr[Mesh.ARRAY_COLOR]
	var idx = arr[Mesh.ARRAY_INDEX]
	if idx == null or idx.size() == 0:
		idx = PackedInt32Array(range(verts.size()))
	var mat = mi.get_active_material(s)
	var albedo := Color.WHITE
	var img: Image = null
	var use_vc := false
	var metallic := 0.0
	var roughness := 1.0
	if mat is BaseMaterial3D:
		albedo = mat.albedo_color
		metallic = mat.metallic
		roughness = mat.roughness
		use_vc = mat.vertex_color_use_as_albedo
		if mat.albedo_texture:
			img = mat.albedo_texture.get_image()
			if img and img.is_compressed():
				img.decompress()
	for i in range(0, idx.size(), 3):
		var a = idx[i]
		var b = idx[i + 1]
		var c = idx[i + 2]
		var col := albedo
		if img and uvs != null and uvs.size() > 0:
			var uv: Vector2 = (uvs[a] + uvs[b] + uvs[c]) / 3.0
			var px := int(clamp(fposmod(uv.x, 1.0) * img.get_width(), 0, img.get_width() - 1))
			var py := int(clamp(fposmod(uv.y, 1.0) * img.get_height(), 0, img.get_height() - 1))
			col *= img.get_pixel(px, py)
		if use_vc and cols != null and cols.size() > 0:
			col *= (cols[a] + cols[b] + cols[c]) / 3.0
		var p0 = xf * verts[a]
		var p1 = xf * verts[b]
		var p2 = xf * verts[c]
		tris.append([p0.x, p0.y, p0.z, p1.x, p1.y, p1.z, p2.x, p2.y, p2.z,
			int(col.r8), int(col.g8), int(col.b8), snapped(metallic, 0.01), snapped(roughness, 0.01)])
