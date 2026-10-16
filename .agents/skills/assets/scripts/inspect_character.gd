extends SceneTree
#
# Checks imported character GLBs against what the game relies on (`player.tscn`, the enemy scenes,
# `CharacterModel`): the `Rig/Skeleton3D` path, the model's own AnimationPlayer and its looping clips, and the
# BoneAttachment3D props by name. Prints one JSON line per model.
#
#   .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" \
#     --script .agents/skills/assets/scripts/inspect_character.gd -- res://assets/custom-heroes/barbarian.glb ...

func _init():
	for path in OS.get_cmdline_user_args():
		var scene: PackedScene = load(path)
		if scene == null:
			print(JSON.stringify({"path": path, "error": "does not load"}))
			continue
		var root: Node3D = scene.instantiate()
		var skel: Skeleton3D = root.get_node_or_null("Rig/Skeleton3D")
		var ap: AnimationPlayer = root.get_node_or_null("AnimationPlayer")
		var looping := []
		if ap:
			for clip in ap.get_animation_list():
				if ap.get_animation(clip).loop_mode != Animation.LOOP_NONE:
					looping.append(clip)
		var props := []
		var meshes := []
		if skel:
			for c in skel.get_children():
				if c is BoneAttachment3D:
					props.append(c.name)
				elif c is MeshInstance3D:
					meshes.append(c.name)
		var rig: Node3D = root.get_node_or_null("Rig")
		print(JSON.stringify({"path": path, "skeleton": skel != null, "bones": skel.get_bone_count() if skel else 0,
			"clips": ap.get_animation_list().size() if ap else 0, "looping": looping, "props": props,
			"meshes": meshes, "rig_scale": rig.scale.x if rig else 0.0}))
		root.free()
	quit()
