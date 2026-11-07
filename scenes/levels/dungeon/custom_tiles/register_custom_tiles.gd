extends SceneTree
## Registers the Asset Studio tiles exported to res://assets/custom-tiles/dungeon in the dungeon mesh libraries.
##
## Run headless from the project root after `studio.py tile export`:
##   godot --headless --path . --script res://scenes/levels/dungeon/custom_tiles/register_custom_tiles.gd
##
## Each tile's mesh and collision are saved as resources beside this script (the GLBs stay art only), then added to
## the floor, wall or decoration library under the tile's name. Existing items keep their ids, so GridMap cells and
## the KayKit items are untouched; re-running updates studio items in place. Walls get a convex hull, rugs no
## collision (actors walk over them). Every walkable floor item in the floor library, KayKit's included, collides as
## one flat box at its walkable height: surface detail stays visual, because a trimesh's lips and seams stop small
## bodies that monster navigation treats as free to walk. No item carries a navigation mesh: monsters navigate on the
## floor's clearance grid. Pool pieces are laid as props by `PoolStep` over a pit with no floor: they get one box from
## their bed to a low curb above the floor, so actors stop at the edge while missiles fly over. Raised foundations keep
## their own collision. Every
## translucent surface (water) is drawn with the water shader. Doorway pieces and the gate leaf only get their meshes
## saved, for door scenes; T-splits, crossings, endcaps and pillars have no generator use yet, so they stay out of the
## libraries.

const SOURCE := "res://assets/custom-tiles/dungeon"
const OUTPUT := "res://scenes/levels/dungeon/custom_tiles"
const FLOORS := "res://scenes/levels/dungeon/FloorMeshLibrary.tres"
const WALLS := "res://scenes/levels/dungeon/WallsMeshLibrary.tres"
const DECORATION := "res://scenes/levels/dungeon/DecorationMeshLibrary.tres"
const SKIPPED := ["_Tsplit_", "_crossing_", "_half_endcap_", "_pillar_"]
const DOOR_PARTS := ["_doorway_", "_gate_leaf_"]
## Target of pieces that only get their mesh saved.
const DOOR_PART := "door part"
const WATER := "res://scenes/levels/dungeon/water/water.tres"
const POOL := "floor_pool_"
## Pool collision: the whole tile, from below the bed to this height above the floor.
const POOL_CURB := 0.35
## Raised structure rather than walkable floor; keeps its own collision.
const FOUNDATION := "floor_foundation_"
## Flat floor collision: the top follows the tile's surface, capped where scattered rocks or rubble would otherwise
## lift it (MapGenerator.GetFloorSurfaceHeight uses the same cap), down to the bottom of the floor slab.
const FLOOR_MAX_TOP := 0.12
const FLOOR_BOTTOM := -0.1

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
		if target == WALLS:
			var shape_path := "%s/shapes/%s.res" % [OUTPUT, name]
			if ResourceSaver.save(mesh.create_convex_shape(true, true), shape_path, ResourceSaver.FLAG_COMPRESS) != OK:
				return false
			library.set_item_shapes(id, [load(shape_path), Transform3D.IDENTITY])
	if not _flatten_floors(libraries[FLOORS]):
		return false
	for path in libraries:
		if not _save_keeping_uid(libraries[path], path):
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
	if name.begins_with("carpet_") or name.begins_with("candle_"):
		return DECORATION
	return ""


## One box over a whole pool tile, from under its bed (y -1.7) to the curb; saved once so the libraries reference it.
func _pool_shape() -> Shape3D:
	var box := BoxShape3D.new()
	box.size = Vector3(4, POOL_CURB + 1.7, 4)
	var path := "%s/shapes/floor_pool.res" % OUTPUT
	ResourceSaver.save(box, path)
	return load(path)


## Gives every walkable floor item one flat box over its footprint, studio and KayKit items alike, and clears every
## floor item's navigation mesh. Boxes are shared per footprint and top height, so the library references a handful of
## resources.
func _flatten_floors(library: MeshLibrary) -> bool:
	var boxes := {}
	for id in library.get_item_list():
		var name := library.get_item_name(id)
		var mesh := library.get_item_mesh(id)
		library.set_item_navigation_mesh(id, null)
		if mesh == null or name.begins_with(POOL) or name.begins_with(FOUNDATION):
			continue
		var bounds := mesh.get_aabb()
		var top := snappedf(minf(bounds.end.y, FLOOR_MAX_TOP), 0.01)
		var size := Vector3(snappedf(bounds.size.x, 0.01), top - FLOOR_BOTTOM, snappedf(bounds.size.z, 0.01))
		var path := "%s/shapes/floor_flat_%dx%d_top%d.res" % [OUTPUT, roundi(size.x * 100), roundi(size.z * 100), roundi(top * 100)]
		if not boxes.has(path):
			var box := BoxShape3D.new()
			box.size = size
			if ResourceSaver.save(box, path) != OK:
				return false
			boxes[path] = load(path)
		var center := bounds.get_center()
		library.set_item_shapes(id, [boxes[path], Transform3D(Basis.IDENTITY, Vector3(center.x, (top + FLOOR_BOTTOM) / 2, center.z))])
	return true


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


## Headless saves drop the file's UID; restore it so uid:// references to the library keep resolving.
func _save_keeping_uid(library: MeshLibrary, path: String) -> bool:
	var uid := ResourceLoader.get_resource_uid(path)
	if ResourceSaver.save(library, path) != OK:
		return false
	return uid == ResourceUID.INVALID_ID or ResourceSaver.set_uid(path, uid) == OK
