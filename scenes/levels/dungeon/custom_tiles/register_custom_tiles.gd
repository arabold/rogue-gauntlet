extends SceneTree
## Registers the Asset Studio tiles exported to res://assets/custom-tiles/dungeon in the dungeon mesh libraries.
##
## Run headless from the project root after `studio.py tile export`:
##   godot --headless --path . --script res://scenes/levels/dungeon/custom_tiles/register_custom_tiles.gd
##
## Each tile's mesh and collision are saved as resources beside this script (the GLBs stay art only), then added to
## the floor, wall or decoration library under the tile's name. Existing items keep their ids, so GridMap cells and
## the KayKit items are untouched; re-running updates studio items in place. Collision follows the KayKit items:
## floors get a trimesh of their surface and a flat navigation mesh on top, walls a convex hull, rugs none (actors
## walk over them). Water channels and basins keep a KayKit grate's rim, so they take that grate's collision and
## navigation. Pool pieces are laid as props by `PoolStep` over a pit with no floor: they get no navigation and one
## box from their bed to a low curb above the floor, so actors stop at the edge while missiles fly over. Every
## translucent surface (water) is drawn with the water shader. Doorway pieces and the gate leaf only get their meshes
## saved, for door scenes; T-splits, crossings, endcaps and pillars have no generator use yet, so they stay out of the
## libraries.

const SOURCE := "res://assets/custom-tiles/dungeon"
const OUTPUT := "res://scenes/levels/dungeon/custom_tiles"
const FLOORS := "res://scenes/levels/dungeon/FloorMeshLibrary.tres"
const WALLS := "res://scenes/levels/dungeon/WallsMeshLibrary.tres"
const DECORATION := "res://scenes/levels/dungeon/DecorationMeshLibrary.tres"
## Studio floor tops sit at y 0.05 (KayKit's floor footprint).
const FLOOR_TOP := 0.05
const SKIPPED := ["_Tsplit_", "_crossing_", "_half_endcap_", "_pillar_"]
const DOOR_PARTS := ["_doorway_", "_gate_leaf_"]
## Target of pieces that only get their mesh saved.
const DOOR_PART := "door part"
const WATER := "res://scenes/levels/dungeon/water/water.tres"
## Inlays by name prefix and the KayKit grate whose rim, collision and navigation they share.
const GRATES := {"floor_water_channel_": "floor_tile_grate", "floor_water_pool_": "floor_tile_big_grate"}
const POOL := "floor_pool_"
## Pool collision: the whole tile, from below the bed to this height above the floor.
const POOL_CURB := 0.35

var _started := false


func _process(_delta: float) -> bool:
	# Wait one frame: the UID cache is not ready while _init runs.
	if _started:
		return false
	_started = true
	quit(0 if _register() else 1)
	return false


func _register() -> bool:
	var libraries := {FLOORS: load(FLOORS) as MeshLibrary, WALLS: load(WALLS) as MeshLibrary,
		DECORATION: load(DECORATION) as MeshLibrary}
	DirAccess.make_dir_recursive_absolute(OUTPUT + "/meshes")
	DirAccess.make_dir_recursive_absolute(OUTPUT + "/shapes")
	var navigation := {4.0: _floor_navigation("large", 2.0), 2.0: _floor_navigation("small", 1.0)}
	var pool := _pool_shape()
	var count := 0
	for file in DirAccess.get_files_at(SOURCE):
		if file.get_extension() != "glb":
			continue
		var name := file.get_basename()
		var target := _library_for(name)
		if target.is_empty():
			continue
		var library: MeshLibrary = libraries.get(target)
		var id := library.find_item_by_name(name) if library != null else -1
		if id >= 0 and not library.get_item_mesh(id).resource_path.begins_with(OUTPUT):
			push_error("%s would replace the library's own item of that name; rename the tile family." % name)
			return false
		var mesh := _extract_mesh(SOURCE + "/" + file)
		if mesh == null:
			push_error("No mesh in %s" % file)
			return false
		_draw_water(mesh)
		var mesh_path := "%s/meshes/%s.res" % [OUTPUT, name]
		if ResourceSaver.save(mesh, mesh_path, ResourceSaver.FLAG_COMPRESS) != OK:
			return false
		mesh = load(mesh_path)
		count += 1
		if library == null:
			continue
		if id < 0:
			id = library.get_last_unused_item_id()
			library.create_item(id)
		library.set_item_name(id, name)
		library.set_item_mesh(id, mesh)
		library.set_item_mesh_transform(id, Transform3D.IDENTITY)
		library.set_item_mesh_cast_shadow(id, RenderingServer.SHADOW_CASTING_SETTING_ON)
		library.set_item_shapes(id, [])
		library.set_item_navigation_mesh(id, null)
		if name.begins_with(POOL):
			library.set_item_shapes(id, [pool, Transform3D(Basis.IDENTITY, Vector3(0, (POOL_CURB - 1.7) / 2, 0))])
			continue
		var grate := _grate_for(library, name)
		if grate >= 0:
			library.set_item_shapes(id, library.get_item_shapes(grate))
			library.set_item_navigation_mesh(id, library.get_item_navigation_mesh(grate))
			library.set_item_navigation_layers(id, library.get_item_navigation_layers(grate))
			continue
		if target != DECORATION:
			var shape: Shape3D = mesh.create_trimesh_shape() if target == FLOORS else mesh.create_convex_shape(true, true)
			var shape_path := "%s/shapes/%s.res" % [OUTPUT, name]
			if ResourceSaver.save(shape, shape_path, ResourceSaver.FLAG_COMPRESS) != OK:
				return false
			library.set_item_shapes(id, [load(shape_path), Transform3D.IDENTITY])
		if target == FLOORS:
			library.set_item_navigation_mesh(id, navigation[snappedf(mesh.get_aabb().size.x, 1.0)])
			library.set_item_navigation_layers(id, 1)
	for path in libraries:
		if not _save_keeping_uids(libraries[path], path):
			return false
	print("Saved %d custom tiles." % count)
	return true


func _library_for(name: String) -> String:
	for marker in SKIPPED:
		if marker in name:
			return ""
	for marker in DOOR_PARTS:
		if marker in name:
			return DOOR_PART
	if name.begins_with("floor_"):
		return FLOORS
	if name.begins_with("wall_"):
		return WALLS
	if name.begins_with("carpet_"):
		return DECORATION
	return ""


## One box over a whole pool tile, from under its bed (y -1.7) to the curb; saved once so the libraries reference it.
func _pool_shape() -> Shape3D:
	var box := BoxShape3D.new()
	box.size = Vector3(4, POOL_CURB + 1.7, 4)
	var path := "%s/shapes/floor_pool.res" % OUTPUT
	ResourceSaver.save(box, path)
	return load(path)


## The KayKit grate an inlay shares its rim with, or -1.
func _grate_for(library: MeshLibrary, name: String) -> int:
	for prefix in GRATES:
		if name.begins_with(prefix):
			return library.find_item_by_name(GRATES[prefix])
	return -1


## The studio exports water as a translucent stand-in; the game draws it with the water shader.
func _draw_water(mesh: Mesh) -> void:
	var water: Material = null
	for surface in mesh.get_surface_count():
		var material := mesh.surface_get_material(surface) as BaseMaterial3D
		if material != null and material.transparency != BaseMaterial3D.TRANSPARENCY_DISABLED:
			water = water if water != null else load(WATER)
			mesh.surface_set_material(surface, water)


## The tile's single mesh, detached from the imported scene so it saves as its own resource.
func _extract_mesh(path: String) -> Mesh:
	var scene := (load(path) as PackedScene).instantiate()
	var instances := scene.find_children("*", "MeshInstance3D", true, false)
	var mesh: Mesh = null
	if not instances.is_empty():
		mesh = (instances[0] as MeshInstance3D).mesh.duplicate()
	scene.free()
	return mesh


## A flat walkable square on the floor top, saved once per floor size so the libraries reference it.
func _floor_navigation(size: String, half: float) -> NavigationMesh:
	var navigation := NavigationMesh.new()
	navigation.vertices = PackedVector3Array([Vector3(-half, FLOOR_TOP, -half), Vector3(half, FLOOR_TOP, -half),
		Vector3(half, FLOOR_TOP, half), Vector3(-half, FLOOR_TOP, half)])
	navigation.add_polygon(PackedInt32Array([0, 1, 2, 3]))
	var path := "%s/shapes/floor_%s_navigation.res" % [OUTPUT, size]
	ResourceSaver.save(navigation, path)
	return load(path)


## Headless saves drop the file's and its references' UIDs; put them back so uid:// references keep resolving.
func _save_keeping_uids(library: MeshLibrary, path: String) -> bool:
	var before := FileAccess.get_file_as_string(path)
	if ResourceSaver.save(library, path) != OK:
		return false
	var uid_pattern := RegEx.create_from_string('uid="[^"]+"')
	var path_pattern := RegEx.create_from_string('path="([^"]+)"')
	# Keyed by referenced path; the file's own UID by "".
	var uids := {}
	for line in before.split("\n"):
		if not (line.begins_with("[gd_resource ") or line.begins_with("[ext_resource ")):
			continue
		var uid := uid_pattern.search(line)
		var referenced := path_pattern.search(line)
		if uid != null:
			uids[referenced.get_string(1) if referenced != null else ""] = uid.get_string()
	var after := FileAccess.get_file_as_string(path)
	# Preview images get fresh ids on every save; give the unchanged previews back their old ones.
	var image_pattern := RegEx.create_from_string('\\[sub_resource type="Image" id="([^"]+)"\\]')
	var old_images := image_pattern.search_all(before)
	var new_images := image_pattern.search_all(after)
	var count := mini(old_images.size(), new_images.size())
	# Through placeholders: a fresh id may equal another preview's old one.
	for index in count:
		after = after.replace('"%s"' % new_images[index].get_string(1), '"preview_image_%d"' % index)
	for index in count:
		after = after.replace('"preview_image_%d"' % index, '"%s"' % old_images[index].get_string(1))
	var lines := after.split("\n")
	for index in lines.size():
		var line := lines[index]
		if " uid=" in line:
			continue
		if line.begins_with("[gd_resource ") and uids.has(""):
			lines[index] = line.trim_suffix("]") + " " + uids[""] + "]"
		elif line.begins_with("[ext_resource "):
			var referenced := path_pattern.search(line)
			if referenced != null and uids.has(referenced.get_string(1)):
				lines[index] = line.insert(referenced.get_start(), uids[referenced.get_string(1)] + " ")
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string("\n".join(lines))
	return true
