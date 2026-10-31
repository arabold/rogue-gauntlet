extends SceneTree
## GPU-rendered art-only review of modular tiles laid out on the game grid, so seams, repeats and
## rotations read as they would in a level. No gameplay resources, mesh libraries or scenes are loaded.
## Windowed only: --script ... -- <jobs.json>
## Each job: {"tiles": [{"glb", "position": [x,y,z], "rotation": degrees}], "lights": [...],
##   "ambient": energy, "sun": energy, "resolution": [w, h],
##   "shots": [{"out", "target": [x,y,z], "direction": [x,y,z], "size": ortho size}]}

const SurfaceColors = preload("res://asset-studio/godot/item_surface_colors.gd")

var viewport: SubViewport
var camera: Camera3D
var settings: Environment
var sun: DirectionalLight3D
var holder: Node3D
var cache := {}
var jobs: Array = []
var job_index := 0
var shot := 0
var frame := 0
var wait_frames := 0

func load_glb(path: String) -> Node3D:
	# Runtime loading works inside .gdignore, so a review never needs to export to assets/.
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	if document.append_from_file(path, state) != OK:
		push_error("Cannot load GLB: " + path)
		quit(1)
		return null
	var scene := document.generate_scene(state) as Node3D
	SurfaceColors.apply(scene)
	return scene

func vec(values: Array) -> Vector3:
	return Vector3(values[0], values[1], values[2])

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		push_error("Usage: render_tiles.gd -- <jobs.json>")
		quit(1)
		return
	jobs = JSON.parse_string(FileAccess.get_file_as_string(args[0]))
	if jobs == null or jobs.is_empty():
		push_error("No tile render jobs")
		quit(1)
		return
	viewport = SubViewport.new()
	viewport.own_world_3d = true
	viewport.msaa_3d = Viewport.MSAA_4X
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.far = 400
	camera.current = true
	viewport.add_child(camera)
	var environment := WorldEnvironment.new()
	settings = Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color(.05, .055, .07)
	settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	settings.ambient_light_color = Color(.65, .70, .80)
	environment.environment = settings
	viewport.add_child(environment)
	sun = DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-55, -35, 0)
	sun.shadow_enabled = true
	viewport.add_child(sun)
	start_job()

func start_job() -> void:
	if holder != null:
		holder.free()
	holder = Node3D.new()
	viewport.add_child(holder)
	var job: Dictionary = jobs[job_index]
	var resolution: Array = job.get("resolution", [960, 720])
	viewport.size = Vector2i(resolution[0], resolution[1])
	settings.ambient_light_energy = job.get("ambient", .55)
	sun.light_energy = job.get("sun", 1.4)
	sun.visible = sun.light_energy > 0
	for tile in job["tiles"]:
		var path: String = tile["glb"]
		if not cache.has(path):
			cache[path] = load_glb(path)
			if cache[path] == null:
				return
		var instance: Node3D = cache[path].duplicate()
		instance.position = vec(tile["position"])
		instance.rotation_degrees.y = tile.get("rotation", 0.0)
		holder.add_child(instance)
	for spec in job.get("lights", []):
		var light := OmniLight3D.new()
		light.position = vec(spec["position"])
		light.omni_range = spec.get("range", 8.0)
		light.light_energy = spec.get("energy", 2.0)
		light.light_color = Color(spec.get("color", [1.0, .72, .42])[0], spec.get("color", [1.0, .72, .42])[1], spec.get("color", [1.0, .72, .42])[2])
		light.shadow_enabled = true
		holder.add_child(light)
	shot = 0
	frame = 0
	wait_frames = 0

func configure_shot() -> void:
	var spec: Dictionary = jobs[job_index]["shots"][shot]
	var target := vec(spec["target"])
	var direction := vec(spec.get("direction", [6.8, 11.5, 6.8])).normalized()
	camera.size = spec["size"]
	var up := Vector3.FORWARD if absf(direction.y) > .999 else Vector3.UP
	camera.look_at_from_position(target + direction * 120.0, target, up)
	DirAccess.make_dir_recursive_absolute(String(spec["out"]).get_base_dir())

func _process(_delta: float) -> bool:
	frame += 1
	if frame > 900:
		push_error("Tile render timed out")
		quit(1)
		return true
	if frame < 3:
		return false
	if wait_frames == 0:
		configure_shot()
		wait_frames = 1
		return false
	wait_frames += 1
	if wait_frames < 8:
		return false
	var output: String = jobs[job_index]["shots"][shot]["out"]
	if viewport.get_texture().get_image().save_png(output) != OK:
		push_error("Cannot save tile frame: " + output)
		quit(1)
		return true
	print("TILE_SHOT ", output)
	shot += 1
	wait_frames = 0
	if shot == jobs[job_index]["shots"].size():
		job_index += 1
		if job_index == jobs.size():
			# Templates never enter the tree; free them so exit reports no leaked resources.
			for template in cache.values():
				template.free()
			quit()
			return true
		start_job()
	return false
