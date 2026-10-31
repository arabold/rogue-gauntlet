extends SceneTree
## Renders models in one row on a neutral floor under the Studio lineup lighting, for side-by-side style and scale
## checks of new props against the pack's own art. Run windowed (headless renders are blank):
##   godot --path <project> --script compare_models.gd -- <out.png> <yaw degrees> <model> [<model> ...]
## Models are res:// scenes (.gltf, .glb, .tscn, .fbx) or absolute .glb paths, such as a staged Studio build.

var frames := 0
var out := ""

func load_model(path: String) -> Node3D:
	if path.begins_with("res://"):
		return (load(path) as PackedScene).instantiate()
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	doc.append_from_file(path, state)
	return doc.generate_scene(state)

func bounds(node: Node) -> AABB:
	var box := AABB()
	var first := true
	for child in node.find_children("*", "MeshInstance3D", true, false):
		var mesh := child as MeshInstance3D
		var b: AABB = mesh.global_transform * mesh.get_aabb()
		box = b if first else box.merge(b)
		first = false
	return box

func setup() -> void:
	var args := OS.get_cmdline_user_args()
	out = args[0]
	var yaw := float(args[1])
	root.size = Vector2i(1800, 900)
	var env := WorldEnvironment.new()
	var settings := Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color(.11, .12, .15)
	settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	settings.ambient_light_color = Color(.65, .70, .80)
	settings.ambient_light_energy = .55
	env.environment = settings
	root.add_child(env)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-50, -30, 0)
	key.light_energy = 1.5
	key.shadow_enabled = true
	root.add_child(key)
	var holder := Node3D.new()
	root.add_child(holder)
	var x := 0.0
	for path in args.slice(2):
		var model := load_model(path)
		model.rotation_degrees.y = yaw
		holder.add_child(model)
		var b := bounds(model)
		model.position.x = x - b.position.x
		x += b.size.x + .6
	var all := bounds(holder)
	var floor := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(all.size.x + 6, 10)
	floor.mesh = plane
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(.36, .34, .33)
	floor.material_override = mat
	floor.position = Vector3(all.get_center().x, 0, 0)
	root.add_child(floor)
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = max(all.size.x * .72, all.size.y * 1.6)
	root.add_child(camera)
	var center := all.get_center()
	camera.look_at_from_position(center + Vector3(0, all.size.y * 1.2 + 3, 9), center)
	camera.current = true

func _process(_delta: float) -> bool:
	frames += 1
	if frames == 2:
		setup()
	if frames == 20:
		root.get_viewport().get_texture().get_image().save_png(out)
		print("SAVED ", out)
		quit()
	return false
