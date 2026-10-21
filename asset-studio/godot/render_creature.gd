extends "res://asset-studio/godot/render_armor_fittings.gd"
## Review independent creature rigs using the studio's established GPU environment.
var captures: Array[Dictionary] = []

func generate_visual(document: GLTFDocument, state: GLTFState) -> Node3D:
	# Constant rest tracks are required when switching between partial source clips.
	return document.generate_scene(state, 30.0, false, false) as Node3D

func prepare_captures() -> void:
	var expected_tracks := PackedStringArray()
	for clip in jobs[job_index][2]:
		if not player.has_animation(clip):
			push_error("Missing creature clip: " + clip)
			quit(1)
			return
		var animation := player.get_animation(clip)
		var tracks := PackedStringArray()
		for i in range(animation.get_track_count()):
			tracks.append(str(animation.track_get_type(i)) + ":" + str(animation.track_get_path(i)))
		tracks.sort()
		if expected_tracks.is_empty():
			expected_tracks = tracks
		elif tracks != expected_tracks:
			push_error("Creature clips lost constant reset tracks during import: " + clip)
			quit(1)
			return
		var duration := animation.length
		for i in range(12):
			captures.append({"label":"%s_%02d" % [clip, i], "clip":clip,
				"time":duration * float(i) / 12.0, "duration":duration})

func configure() -> void:
	var capture := captures[shot]
	player.play(capture.clip)
	player.seek(capture.time, true)
	player.pause()
	var limits: Array = jobs[job_index][3]
	var low := Vector3(limits[0][0], limits[0][1], limits[0][2])
	var high := Vector3(limits[1][0], limits[1][1], limits[1][2])
	bounds = AABB(low, high - low)
	var center := bounds.get_center()
	var extent := maxf(bounds.size.x, maxf(bounds.size.y, bounds.size.z))
	camera.size = extent * 1.18
	camera.look_at_from_position(center + Vector3(.8, .5, 1).normalized() * extent * 4, center)

func _process(_delta: float) -> bool:
	frames += 1
	if frames < 5:
		return false
	var output: String = jobs[job_index][1].path_join(captures[shot].label + ".png")
	if viewport.get_texture().get_image().save_png(output) != OK:
		push_error("Cannot save creature frame: " + output)
		quit(1)
		return true
	frames = 0
	shot += 1
	if shot == captures.size():
		var metadata := FileAccess.open(jobs[job_index][1].path_join("captures.json"), FileAccess.WRITE)
		metadata.store_string(JSON.stringify(captures))
		metadata.close()
		print("CREATURE_REVIEW_COMPLETE")
		quit()
		return true
	configure()
	return false
