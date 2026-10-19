extends RefCounted
## Godot 4.7 sets the vertex-color material flag before detecting COLOR_0 on the first primitive.
## Keep this workaround restricted to studio-authored surface-color materials, preserving other shaders.
## https://github.com/godotengine/godot/blob/4.7-stable/modules/gltf/gltf_document.cpp

static func apply(node: Node) -> void:
	if node is MeshInstance3D and node.mesh != null:
		for surface in range(node.mesh.get_surface_count()):
			var material := node.mesh.surface_get_material(surface) as BaseMaterial3D
			if material == null or not material.has_meta("extras"):
				continue
			var extras: Dictionary = material.get_meta("extras")
			if extras.get("asset_studio_vertex_color", false):
				material.vertex_color_use_as_albedo = true
				material.vertex_color_is_srgb = false
	for child in node.get_children():
		apply(child)
