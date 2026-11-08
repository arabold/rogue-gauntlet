@tool
extends EditorScenePostImport
## Post-import script for every KayKit model (`import_script/path` in each KayKit `.import`). KayKit exports its stone,
## wood and cloth semi-glossy (roughness 0.3-0.5), which shines like plastic under the dungeon's close point lights.
## Those non-metal materials take the project's matte default instead, so lights can keep their specular for metal.
## Metal and deliberate gloss (KayKit's gems at 0.2) keep theirs. asset-studio's `matte_roughness` (`core/export.py`)
## applies the same rule to the KayKit surfaces it reuses.

const MATTE_ROUGHNESS := 0.85
## Rougher non-metal than this is KayKit's semi-gloss default; glossier is a deliberate gem or glass.
const DELIBERATE_GLOSS := 0.25


func _post_import(scene: Node) -> Object:
	for node in scene.find_children("*", "MeshInstance3D", true, false):
		var mesh: Mesh = node.mesh
		if mesh == null:
			continue
		for surface in mesh.get_surface_count():
			var material := mesh.surface_get_material(surface) as BaseMaterial3D
			if material != null and material.metallic == 0.0 and material.roughness >= DELIBERATE_GLOSS:
				material.roughness = maxf(material.roughness, MATTE_ROUGHNESS)
	return scene
