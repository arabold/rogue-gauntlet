extends SceneTree
## Art-only skinned armor review. Runtime GLB loading keeps studies inside .gdignore.
var viewport: SubViewport
var camera: Camera3D
var visual: Node3D
var player: AnimationPlayer
var jobs: Array
var job_index := 0
var shot := 0
var frames := 0
var bounds: AABB
const MOTION_FRAMES := 24
const SHOTS = ["front", "three_quarter", "side", "back", "walking", "attack", "game_context"]
const SurfaceColors = preload("res://asset-studio/godot/item_surface_colors.gd")

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		quit(1)
		return
	jobs = JSON.parse_string(FileAccess.get_file_as_string(args[0]))
	viewport = SubViewport.new()
	viewport.size = Vector2i(512, 512)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.current = true
	viewport.add_child(camera)
	var world := WorldEnvironment.new()
	var settings := Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color(.08, .09, .12)
	settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	settings.ambient_light_color = Color(.65, .70, .80)
	settings.ambient_light_energy = .55
	world.environment = settings
	viewport.add_child(world)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-50, -30, 0)
	light.light_energy = 1.5
	light.shadow_enabled = true
	viewport.add_child(light)
	call_deferred("start_job")

func start_job() -> void:
	if visual != null:
		visual.free()
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	if document.append_from_file(jobs[job_index][0], state) != OK:
		push_error("Cannot load armor study")
		quit(1)
		return
	visual = generate_visual(document, state)
	SurfaceColors.apply(visual)
	viewport.add_child(visual)
	player = find_player(visual)
	if player == null:
		push_error("Armor study has no AnimationPlayer")
		quit(1)
		return
	bounds = measure(visual)
	DirAccess.make_dir_recursive_absolute(jobs[job_index][1])
	shot = 0
	frames = 0
	prepare_captures()
	configure()

func generate_visual(document: GLTFDocument, state: GLTFState) -> Node3D:
	return document.generate_scene(state) as Node3D

func prepare_captures() -> void:
	pass

static func capture_schedule(include_motion: bool = false) -> Array[Dictionary]:
	var clips := ["T-Pose", "T-Pose", "T-Pose", "T-Pose", "Walking_A", "1H_Melee_Attack_Chop", "Idle"]
	var phases := [0., 0., 0., 0., .22, .45, 0.]
	var directions := [Vector3(0,.04,1), Vector3(.7,.25,1), Vector3(1,.04,0), Vector3(0,.04,-1),
		Vector3(.7,.25,1), Vector3(.7,.25,1), Vector3(6.8,11.5,6.8)]
	var schedule: Array[Dictionary] = []
	for i in range(SHOTS.size()):
		schedule.append({"label":SHOTS[i], "clip":clips[i], "phase":phases[i], "direction":directions[i]})
	if include_motion:
		for clip in ["Walking_A", "1H_Melee_Attack_Chop"]:
			for i in range(MOTION_FRAMES):
				schedule.append({"label":"motion_%s_%02d" % [clip,i], "clip":clip,
					"phase":float(i)/MOTION_FRAMES, "direction":Vector3(.7,.25,1)})
	return schedule

func find_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var found := find_player(child)
		if found != null:
			return found
	return null

func measure(node: Node3D) -> AABB:
	var box := AABB()
	var first := true
	for mesh in node.find_children("*", "MeshInstance3D", true, false):
		var next: AABB = mesh.global_transform * mesh.get_aabb()
		box = next if first else box.merge(next)
		first = false
	return box

func configure() -> void:
	var capture := capture_schedule()[shot]
	var animation: String = capture.clip
	var phase: float = capture.phase
	if not player.has_animation(animation):
		push_error("Missing armor fitting animation: " + animation)
		quit(1)
		return
	player.play(animation)
	player.seek(player.get_animation(animation).length * phase, true)
	player.pause()
	var center := bounds.get_center()
	var extent := maxf(bounds.size.x, maxf(bounds.size.y, bounds.size.z))
	camera.size = extent * 1.23
	camera.look_at_from_position(center + capture.direction.normalized() * extent * 4, center)

func _process(_delta: float) -> bool:
	frames += 1
	if frames < 12:
		return false
	var output: String = jobs[job_index][1].path_join(SHOTS[shot] + ".png")
	if viewport.get_texture().get_image().save_png(output) != OK:
		push_error("Cannot save armor fitting: " + output)
		quit(1)
		return true
	print("ARMOR_SHOT ", output)
	shot += 1
	frames = 0
	if shot == SHOTS.size():
		job_index += 1
		if job_index == jobs.size():
			quit()
			return true
		start_job()
	else:
		configure()
	return false
