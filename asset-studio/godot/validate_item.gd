extends SceneTree
## Validate imported 3D models, including mesh instantiation, rather than only file existence.

func _init() -> void:
	var failed := false
	for path in OS.get_cmdline_user_args():
		var resource := load(path)
		if resource == null:
			failed = true
			continue
		var packed: PackedScene = resource as PackedScene
		if packed == null:
			push_error("Model is not a PackedScene: " + path)
			failed = true
			continue
		var instance := packed.instantiate()
		if instance == null:
			failed = true
			continue
		var meshes := instance.find_children("*", "MeshInstance3D", true, false)
		if instance is MeshInstance3D:
			meshes.append(instance)
		var surfaces := 0
		for node in meshes:
			if node.mesh != null:
				surfaces += node.mesh.get_surface_count()
				for surface in range(node.mesh.get_surface_count()):
					var material := node.mesh.surface_get_material(surface) as BaseMaterial3D
					if material != null and material.has_meta("extras"):
						var extras: Dictionary = material.get_meta("extras")
						if extras.get("asset_studio_vertex_color", false) and not material.vertex_color_use_as_albedo:
							push_error("Studio surface colors disabled: " + path)
							failed = true
		if surfaces == 0:
			push_error("Item has no mesh surfaces: " + path)
			failed = true
		else:
			print("ITEM_OK ", path, " surfaces=", surfaces)
		instance.free()
	quit(1 if failed else 0)
