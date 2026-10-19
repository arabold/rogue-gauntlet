extends SceneTree
## GPU-rendered art-only item study and game-camera scale context. No gameplay resources are loaded.
## Windowed only: --script ... -- <staged.glb> <mannequin.glb> <output_dir> <ground|right_hand|left_hand|shield>

var viewport: SubViewport
var camera: Camera3D
var holder: Node3D
var visual: Node3D
var mannequin: Node3D
var floor_mesh: MeshInstance3D
var outdir: String
var mount: String
var bounds: AABB
var frame := 0
var shot := 0
var wait_frames := 0
var jobs: Array = []
var job_index := 0
const SHOTS = ["front", "three_quarter", "side", "game_context"]
const SurfaceColors = preload("res://asset-studio/godot/item_surface_colors.gd")

func load_glb(path: String) -> Node3D:
	# Runtime loading works inside .gdignore, so a lineup never needs to publish to assets/.
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	if document.append_from_file(path, state) != OK:
		push_error("Cannot load GLB: " + path)
		quit(1)
		return null
	var scene := document.generate_scene(state) as Node3D
	SurfaceColors.apply(scene)
	return scene

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() == 1:
		jobs = JSON.parse_string(FileAccess.get_file_as_string(args[0]))
	elif args.size() == 4:
		jobs.append(Array(args))
	if jobs.is_empty():
		quit(1)
		return
	viewport = SubViewport.new()
	viewport.size = Vector2i(512, 512)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	camera = Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.current = true
	viewport.add_child(camera)
	var environment := WorldEnvironment.new()
	var settings := Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color(.08, .09, .12)
	settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	settings.ambient_light_color = Color(.65, .70, .80)
	settings.ambient_light_energy = .55
	environment.environment = settings
	viewport.add_child(environment)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-50, -30, 0)
	key.light_energy = 1.5
	key.shadow_enabled = true
	viewport.add_child(key)
	floor_mesh = MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(40, 40)
	floor_mesh.mesh = plane
	floor_mesh.position.y = -.003
	var floor_material := StandardMaterial3D.new()
	floor_material.albedo_color = Color(.16, .17, .20)
	floor_material.roughness = 1
	floor_mesh.material_override = floor_material
	floor_mesh.visible = false
	viewport.add_child(floor_mesh)
	holder = Node3D.new()
	viewport.add_child(holder)
	mannequin = load_glb(jobs[0][1])
	if mannequin == null:
		return
	viewport.add_child(mannequin)
	start_job()

func start_job() -> void:
	if visual != null:
		visual.free()
	holder.transform = Transform3D.IDENTITY
	mannequin.transform = Transform3D.IDENTITY
	mannequin.visible = false
	floor_mesh.visible = false
	outdir = jobs[job_index][2]
	mount = jobs[job_index][3]
	visual = load_glb(jobs[job_index][0])
	if visual == null:
		return
	holder.add_child(visual)
	frame = 0
	shot = 0
	wait_frames = 0
	DirAccess.make_dir_recursive_absolute(outdir)

func measure(node: Node3D) -> AABB:
	var result := AABB()
	var has_bounds := false
	var meshes := node.find_children("*", "MeshInstance3D", true, false)
	if node is MeshInstance3D:
		meshes.append(node)
	for mesh_node in meshes:
		var box: AABB = mesh_node.global_transform * mesh_node.get_aabb()
		result = result.merge(box) if has_bounds else box
		has_bounds = true
	return result

func find_skeleton(node: Node) -> Skeleton3D:
	if node is Skeleton3D:
		return node as Skeleton3D
	for child in node.get_children():
		var found := find_skeleton(child)
		if found != null:
			return found
	return null

func configure_shot() -> void:
	var center := bounds.get_center()
	var extent := maxf(bounds.size.x, maxf(bounds.size.y, bounds.size.z))
	camera.size = maxf(extent * 1.5, .1)
	var directions := [Vector3(0, .1, 1), Vector3(.75, .5, 1), Vector3(1, .1, 0)]
	if shot < 3:
		camera.look_at_from_position(center + directions[shot].normalized() * maxf(extent * 4, 2), center)
	else:
		floor_mesh.visible = true
		mannequin.visible = true
		var man_bounds := measure(mannequin)
		mannequin.position += Vector3(-man_bounds.get_center().x, -man_bounds.position.y, -man_bounds.get_center().z)
		if mount == "ground":
			holder.position = Vector3(man_bounds.size.x * .65 + bounds.size.x * .5 + .15 - center.x,
				-bounds.position.y, -center.z)
		else:
			var skeleton := find_skeleton(mannequin)
			var bone := skeleton.find_bone("hand.r" if mount == "right_hand" else "hand.l") if skeleton else -1
			if bone < 0:
				push_error("Mannequin has no requested hand bone")
				quit(1)
				return
			# Static pose attachment for art inspection: no inventory, player scripts, or equipment rules.
			var hand_transform := skeleton.global_transform * skeleton.get_bone_global_pose(bone)
			# Rig unit conversions must not shrink the independently authored item.
			# Shield inspection keeps its authored Y axis upright. The grip
			# remains at the left hand; this is a static art pose, not equipment logic.
			var basis := Basis.IDENTITY if mount == "shield" else hand_transform.basis.orthonormalized()
			holder.global_transform = Transform3D(basis, hand_transform.origin)
		var context := measure(mannequin).merge(measure(visual))
		center = context.get_center()
		camera.size = maxf(context.size.x, maxf(context.size.y, context.size.z)) * 1.35
		# Same elevated orthographic direction as the gameplay camera (main.tscn).
		var direction := Vector3(6.8, 11.5, 6.8)
		if mount == "right_hand":
			direction.x = -6.8
		camera.look_at_from_position(center + direction, center)

func _process(_delta: float) -> bool:
	frame += 1
	if frame > 600:
		push_error("Item lineup timed out")
		quit(1)
		return true
	if frame < 3:
		return false
	if wait_frames == 0:
		if shot == 0:
			bounds = measure(visual)
		configure_shot()
		wait_frames = 1
		return false
	wait_frames += 1
	if wait_frames < 10:
		return false
	var output := outdir.path_join(SHOTS[shot] + ".png")
	if viewport.get_texture().get_image().save_png(output) != OK:
		push_error("Cannot save lineup frame: " + output)
		quit(1)
		return true
	print("ITEM_SHOT ", output)
	shot += 1
	if shot == SHOTS.size():
		job_index += 1
		if job_index == jobs.size():
			quit()
			return true
		start_job()
		return false
	wait_frames = 0
	return false
