extends "res://asset-studio/godot/render_armor_fittings.gd"
## Dressed character review uses the same environment and camera convention as costume fittings.
var captures: Array[Dictionary] = []

func prepare_captures() -> void:
	for capture in capture_schedule(true):
		add_capture(capture.label,capture.clip,capture.phase,capture.direction)

func add_capture(label: String, clip: String, phase: float, direction: Vector3) -> void:
	if not player.has_animation(clip):
		push_error("Missing dressed character clip: " + clip)
		quit(1)
		return
	var duration := player.get_animation(clip).length
	captures.append({"label":label, "clip":clip, "time":duration*phase, "direction":direction, "duration":duration})

func configure() -> void:
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
