extends "res://asset-studio/godot/render_armor_fittings.gd"
## Dressed character review uses the same environment and camera convention as costume fittings.
var captures: Array[Dictionary] = []
const MOTION_FRAMES := 24

func start_job() -> void:
	super.start_job()
	var names := ["front", "three_quarter", "side", "back", "walking", "attack", "game_context"]
	var clips := ["T-Pose", "T-Pose", "T-Pose", "T-Pose", "Walking_A", "1H_Melee_Attack_Chop", "Idle"]
	var phases := [0., 0., 0., 0., .22, .45, 0.]
	var directions := [Vector3(0,.04,1), Vector3(.7,.25,1), Vector3(1,.04,0), Vector3(0,.04,-1),
		Vector3(.7,.25,1), Vector3(.7,.25,1), Vector3(6.8,11.5,6.8)]
	for i in range(names.size()):
		add_capture(names[i], clips[i], phases[i], directions[i])
	for clip in ["Walking_A", "1H_Melee_Attack_Chop"]:
		for i in range(MOTION_FRAMES):
			add_capture("motion_%s_%02d" % [clip,i], clip, float(i)/MOTION_FRAMES, Vector3(.7,.25,1))
	configure()

func add_capture(label: String, clip: String, phase: float, direction: Vector3) -> void:
	if not player.has_animation(clip):
		push_error("Missing dressed character clip: " + clip)
		quit(1)
		return
	var duration := player.get_animation(clip).length
	captures.append({"label":label, "clip":clip, "time":duration*phase, "direction":direction, "duration":duration})

func configure() -> void:
	# The inherited start_job calls configure before the capture list is populated.
	if captures.is_empty():
		return
	var capture := captures[shot]
	player.play(capture.clip)
	player.seek(capture.time,true)
	player.pause()
	var center := bounds.get_center()
	var extent := maxf(bounds.size.x,maxf(bounds.size.y,bounds.size.z))
	camera.size = extent*1.23
	camera.look_at_from_position(center+capture.direction.normalized()*extent*4,center)

func _process(_delta: float) -> bool:
	frames += 1
	if frames < 12:
		return false
	var output: String = jobs[job_index][1].path_join(captures[shot].label+".png")
	if viewport.get_texture().get_image().save_png(output) != OK:
		push_error("Cannot save dressed character capture: "+output)
		quit(1)
		return true
	print("COSTUME_CHARACTER ",output)
	frames = 0
	shot += 1
	if shot == captures.size():
		var metadata := FileAccess.open(jobs[job_index][1].path_join("captures.json"),FileAccess.WRITE)
		metadata.store_string(JSON.stringify(captures))
		metadata.close()
		quit()
		return true
	configure()
	return false
