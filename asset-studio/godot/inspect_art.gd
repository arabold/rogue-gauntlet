extends SceneTree
## Art-only import contract: joints and clips, independent of gameplay scenes and humanoid props.
func _initialize() -> void:
	var job = JSON.parse_string(FileAccess.get_file_as_string(OS.get_cmdline_user_args()[0]))
	var packed = load(job.path) as PackedScene
	if packed == null:
		fail("Cannot load exported GLB")
		return
	var instance = packed.instantiate()
	var skeletons = instance.find_children("*","Skeleton3D",true,false)
	var players = instance.find_children("*","AnimationPlayer",true,false)
	if skeletons.is_empty() or players.is_empty():
		instance.free()
		fail("Missing skeleton or AnimationPlayer")
		return
	var skeleton: Skeleton3D = skeletons[0]
	var player: AnimationPlayer = players[0]
	for bone in job.required_bones:
		if skeleton.find_bone(bone)<0:
			instance.free()
			fail("Missing required joint: "+bone)
			return
	var tracks := PackedStringArray()
	for clip in job.clips:
		if not player.has_animation(clip):
			instance.free()
			fail("Missing clip: "+clip)
			return
		var animation = player.get_animation(clip)
		if (animation.loop_mode!=Animation.LOOP_NONE)!=(clip in job.loops):
			instance.free()
			fail("Incorrect loop mode: "+clip)
			return
		var current := PackedStringArray()
		for i in animation.get_track_count():current.append(str(animation.track_get_type(i))+":"+str(animation.track_get_path(i)))
		current.sort()
		if not tracks.is_empty() and current!=tracks:
			instance.free()
			fail("Constant reset tracks were lost: "+clip)
			return
		tracks=current
	print("ART_IMPORT_OK "+JSON.stringify({"path":job.path,"bones":skeleton.get_bone_count(),"clips":job.clips.size()}))
	instance.free()
	quit()

func fail(message: String) -> void:
	push_error(message)
	quit(1)
