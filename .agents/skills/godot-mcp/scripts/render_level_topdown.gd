extends SceneTree
#
# Renders a top-down screenshot of a generated level (or a single room), with an
# overlay marking every doorway/connector's per-direction open/sealed status -- lets
# you visually verify doorway/wall alignment instead of reasoning about coordinates,
# and without depending on DoorwayMarker's arrow gizmo (editor-only, invisible outside
# the actual Godot editor, so it can't be captured by a headless/windowed script run).
#
# MUST run WINDOWED (not --headless): the headless dummy renderer produces blank
# images and hangs. A real window briefly opens (uses the machine's GPU).
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script .agents/skills/godot-mcp/scripts/render_level_topdown.gd -- <outfile.png> [seed] [size]
#
# e.g. -- /tmp/level_preview.png 23 1600
#
# Overlay legend (drawn as flat, unshaded geometry lifted above the floor):
#   yellow disc = explicit doorway connector (guaranteed-connected)
#   cyan disc   = inferred/optional connector
#   green line  = that direction leads to an open passage
#   red line    = that direction is SEALED by a generated wall (a bug if unexpected --
#                 e.g. this is exactly what MapGenerator.FinalizeMarkers guards against)
#
# Uses MapGenerator.GetConnectorDebugInfo() for the overlay data, since MapData (a
# plain C# class) can't marshal to GDScript directly -- see that method's doc comment.

const TILE_SIZE = 4.0

var _viewport: SubViewport
var _map_generator
var _seed_value = 0
var _image_size = 0
var _outfile = ""
var _state = "waiting_for_ready"
var _wait_frames = 0

func _initialize():
	var args = OS.get_cmdline_user_args()
	_outfile = args[0] if args.size() > 0 else "/tmp/level_preview.png"
	_seed_value = int(args[1]) if args.size() > 1 else 23
	_image_size = int(args[2]) if args.size() > 2 else 1600

	var map_generator_scene = load("res://scenes/levels/generators/map_generator.tscn")
	_map_generator = map_generator_scene.instantiate()

	_map_generator.RoomLayout = load("res://scenes/levels/generators/layouts/PackedRoomLayout.cs").new()
	_map_generator.CorridorConnector = load("res://scenes/levels/generators/connectors/AStarCorridorConnector.cs").new()
	_map_generator.RoomFactory = load("res://scenes/levels/dungeon/dungeon_mixed_room_factory.tres")
	_map_generator.MobFactory = load("res://scenes/levels/dungeon/dungeon_mob_factory.tres")
	_map_generator.TileFactory = load("res://scenes/levels/dungeon/dungeon_tile_factory.tres")
	_map_generator.Seed = _seed_value

	_viewport = SubViewport.new()
	_viewport.transparent_bg = false
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(_viewport)
	_viewport.add_child(_map_generator)
	# _ready() on freshly-added nodes (including autoloads) is deferred to the next
	# frame, not called synchronously by add_child -- GenerateMap must wait for it,
	# since MapGenerator's own _Ready() is what wires up its GridMap/child references.

func _generate_and_frame():
	# Skip navmesh bake + spawns (includeGameplay=false): this is a static geometry
	# preview, and headless navmesh baking is a known no-op (see SKILL.md).
	_map_generator.call("GenerateMap", false)

	var map_width = float(_map_generator.MapWidth)
	var map_depth = float(_map_generator.MapDepth)
	var world_width = map_width * TILE_SIZE
	var world_depth = map_depth * TILE_SIZE
	var aspect = world_width / world_depth
	_viewport.size = Vector2i(_image_size, int(_image_size / aspect)) if aspect >= 1.0 \
		else Vector2i(int(_image_size * aspect), _image_size)

	var camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.keep_aspect = Camera3D.KEEP_HEIGHT
	# KEEP_HEIGHT ties camera.size to the vertical (world Z) extent always; the
	# horizontal extent is derived from the viewport's aspect ratio, already matched to
	# the world's aspect ratio above -- so this must never branch on aspect, or a map
	# taller than it is wide gets its vertical extent underestimated and cropped.
	# A 5% margin so edge-room walls aren't cropped at the frame border.
	camera.size = world_depth * 1.05
	camera.current = true
	_viewport.add_child(camera)
	camera.global_position = Vector3(0, 60, 0)
	camera.global_rotation_degrees = Vector3(-90, 0, 0)

	var key_light = DirectionalLight3D.new()
	_viewport.add_child(key_light)
	key_light.global_rotation_degrees = Vector3(-90, 0, 0)
	key_light.light_energy = 1.1

	var world_environment = WorldEnvironment.new()
	var environment = Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.05, 0.05, 0.07)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.55, 0.57, 0.62)
	environment.ambient_light_energy = 1.0
	world_environment.environment = environment
	_viewport.add_child(world_environment)

	_add_connector_overlay(_map_generator)

	print("Rendering %dx%d preview of seed %d to %s" % [_viewport.size.x, _viewport.size.y, _seed_value, _outfile])

func _add_connector_overlay(map_generator):
	var surface_tool = SurfaceTool.new()
	surface_tool.begin(Mesh.PRIMITIVE_TRIANGLES)
	var overlay_height = 4.5  # above wall-top height so it never gets occluded
	var vertex_count = 0

	for connector in map_generator.call("GetConnectorDebugInfo"):
		var position = connector["worldPosition"]
		position.y = overlay_height
		var disc_color = Color(1.0, 0.95, 0.05) if connector["isDoorway"] else Color(0.1, 0.85, 0.95)
		vertex_count += _add_flat_quad(surface_tool, position, 0.9, disc_color)

		for direction_info in connector["directions"]:
			var direction: Vector3 = direction_info["direction"]
			var line_color = Color(0.15, 0.9, 0.15) if direction_info["open"] else Color(0.95, 0.1, 0.1)
			var line_end = position + direction * (TILE_SIZE * 0.9)
			vertex_count += _add_line_quad(surface_tool, position, line_end, 0.35, line_color)

	if vertex_count == 0:
		return

	var mesh_instance = MeshInstance3D.new()
	mesh_instance.mesh = surface_tool.commit()
	var material = StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.vertex_color_use_as_albedo = true
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	mesh_instance.material_override = material
	_viewport.add_child(mesh_instance)

func _add_flat_quad(surface_tool: SurfaceTool, center: Vector3, half_size: float, color: Color) -> int:
	var a = center + Vector3(-half_size, 0, -half_size)
	var b = center + Vector3(half_size, 0, -half_size)
	var c = center + Vector3(half_size, 0, half_size)
	var d = center + Vector3(-half_size, 0, half_size)
	var vertices = [a, b, c, a, c, d]
	for vertex in vertices:
		surface_tool.set_color(color)
		surface_tool.add_vertex(vertex)
	return vertices.size()

func _add_line_quad(surface_tool: SurfaceTool, from: Vector3, to: Vector3, half_width: float, color: Color) -> int:
	var along = (to - from)
	if along.length_squared() <= 0.0001:
		return 0
	var side = along.normalized().cross(Vector3.UP) * half_width
	var a = from + side
	var b = from - side
	var c = to - side
	var d = to + side
	var vertices = [a, b, c, a, c, d]
	for vertex in vertices:
		surface_tool.set_color(color)
		surface_tool.add_vertex(vertex)
	return vertices.size()

func _process(_delta):
	_wait_frames += 1
	match _state:
		"waiting_for_ready":
			if _wait_frames < 2:
				return false
			_generate_and_frame()
			_state = "waiting_for_render"
			_wait_frames = 0
			return false
		"waiting_for_render":
			if _wait_frames < 6:
				return false
			_viewport.get_texture().get_image().save_png(_outfile)
			print("Saved " + _outfile)
			return true
	return false
