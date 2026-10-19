extends "res://asset-studio/godot/render_armor_fittings.gd"
## Matched reference/studio captures. Inherit the fitting renderer's environment and viewport setup.
var reference: Node3D
var reference_player: AnimationPlayer
var captures: Array[Dictionary] = []
var member := 0
const BODY_MESHES = ["Knight_Body", "Knight_Head", "Knight_ArmLeft", "Knight_ArmRight", "Knight_LegLeft", "Knight_LegRight"]
const MOTION_FRAMES := 24

func load_model(path: String) -> Node3D:
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	if document.append_from_file(path, state) != OK:
		push_error("Cannot load comparison model: " + path)
		quit(1)
		return null
	return document.generate_scene(state) as Node3D

func reference_body_only() -> void:
	# Presentation-only visibility. Leave source geometry, rig, materials and animations intact.
	for node in reference.find_children("*", "MeshInstance3D", true, false):
		node.visible = String(node.name) in BODY_MESHES

func visible_bounds(node: Node3D) -> AABB:
	var result := AABB()
	var first := true
	for mesh in node.find_children("*", "MeshInstance3D", true, false):
		if not mesh.is_visible_in_tree():
			continue
		var box: AABB = mesh.global_transform * mesh.get_aabb()
		result = box if first else result.merge(box)
		first = false
	return result

func start_job() -> void:
	visual = load_model(jobs[job_index][0])
	reference = load_model(jobs[job_index][2])
	if visual == null or reference == null:
		return
	SurfaceColors.apply(visual)
	viewport.add_child(visual)
	viewport.add_child(reference)
	reference_body_only()
	player = find_player(visual)
	reference_player = find_player(reference)
	if player == null or reference_player == null:
		push_error("Missing comparison animation player")
		quit(1)
		return
	# World origins and skeleton scale are unchanged. Never normalize each model's height separately.
	bounds = visible_bounds(visual).merge(visible_bounds(reference))
	var names := ["front", "three_quarter", "side", "back", "walking", "attack", "game_context"]
	var clips := ["T-Pose", "T-Pose", "T-Pose", "T-Pose", "Walking_A", "1H_Melee_Attack_Chop", "Idle"]
	var phases := [0., 0., 0., 0., .22, .45, 0.]
	var directions := [Vector3(0,.04,1), Vector3(.7,.25,1), Vector3(1,.04,0), Vector3(0,.04,-1),
		Vector3(.7,.25,1), Vector3(.7,.25,1), Vector3(6.8,11.5,6.8)]
	for i in range(names.size()):
		add_capture(names[i], clips[i], phases[i], directions[i])
	for clip in ["Walking_A", "1H_Melee_Attack_Chop"]:
		for i in range(MOTION_FRAMES):
			add_capture("motion_%s_%02d" % [clip, i], clip, float(i)/MOTION_FRAMES, Vector3(.7,.25,1))
	DirAccess.make_dir_recursive_absolute(jobs[job_index][1])
	shot = 0
	frames = 0
	member = 0
	configure()

func add_capture(label: String, clip: String, phase: float, direction: Vector3) -> void:
	if not player.has_animation(clip) or not reference_player.has_animation(clip):
		push_error("Missing comparison clip: " + clip)
		quit(1)
		return
	var duration := reference_player.get_animation(clip).length
	if absf(player.get_animation(clip).length - duration) > .0001:
		push_error("Comparison clips differ in duration: " + clip)
		quit(1)
		return
	captures.append({"label": label, "clip": clip, "time": duration*phase, "direction": direction, "duration": duration})

func configure() -> void:
	var capture := captures[shot]
	for animation_player in [player, reference_player]:
		animation_player.play(capture.clip)
		animation_player.seek(capture.time, true)
		animation_player.pause()
	reference_body_only()
	visual.visible = false
	reference.visible = true
	var center := bounds.get_center()
	var extent := maxf(bounds.size.x, maxf(bounds.size.y, bounds.size.z))
	camera.size = extent * 1.23
	camera.look_at_from_position(center + capture.direction.normalized()*extent*4, center)

func _process(_delta: float) -> bool:
	frames += 1
	if frames < 12:
		return false
	var capture := captures[shot]
	var member_name := "original" if member == 0 else "studio"
	var output: String = jobs[job_index][1].path_join(capture.label + "_" + member_name + ".png")
	if viewport.get_texture().get_image().save_png(output) != OK:
		push_error("Cannot save armor comparison: " + output)
		quit(1)
		return true
	print("ARMOR_COMPARISON ", output)
	frames = 0
	if member == 0:
		member = 1
		reference.visible = false
		visual.visible = true
		return false
	member = 0
	shot += 1
	if shot == captures.size():
		var metadata := FileAccess.open(jobs[job_index][1].path_join("captures.json"), FileAccess.WRITE)
		metadata.store_string(JSON.stringify(captures))
		metadata.close()
		quit()
		return true
	configure()
	return false
