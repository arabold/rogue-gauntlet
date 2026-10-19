@tool
extends EditorScenePostImport
## 3D import correction only: no gameplay resources, stats, or runtime behaviors.

const SurfaceColors = preload("res://asset-studio/godot/item_surface_colors.gd")

func _post_import(scene: Node) -> Object:
	SurfaceColors.apply(scene)
	return scene
