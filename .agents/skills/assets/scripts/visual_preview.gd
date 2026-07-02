extends SceneTree
#
# Visual preview harness: stage one or more scenes (monsters, props, effects) inside a real
# authored dungeon room, next to a KayKit knight as a scale reference, and capture gameplay
# screenshots for LLM/human inspection. For enemies (anything with a HealthComponent) it
# also kills the subject mid-run, capturing the death animation and the corpse — this
# verifies scale, textures, hover altitude, hit reactions, and death-drop in one pass.
#
# MUST run WINDOWED (not --headless): the headless dummy renderer cannot capture frames.
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script .agents/skills/assets/scripts/visual_preview.gd -- <outdir> <subject.tscn> [more...]
#
# e.g. -- /tmp/preview res://scenes/enemies/bat/bat.tscn res://scenes/enemies/orc/orc.tscn
#
# Output per subject: <name>_idle.png, and for killable subjects <name>_dying.png +
# <name>_dead.png. Inspect the PNGs for: feet on floor (or hover altitude for flyers),
# scale vs the knight, texture/material correctness, no T-pose, no clipping into geometry.
#
const ROOM := "res://scenes/levels/dungeon/rooms/barracks.tscn"
const REFERENCE := "res://assets/kaykit-adventurers/Knight.glb"

var _outdir: String
var _subjects: Array = []
var _index := -1
var _frame := 0
var _room: Node3D
var _subject: Node3D
var _center: Vector3
var _killable := false

func _initialize():
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		print("usage: -- <outdir> <subject.tscn> [more subjects...]")
		quit(1)
		return
	_outdir = args[0]
	_subjects = args.slice(1)
	DirAccess.make_dir_recursive_absolute(_outdir)
	root.size = Vector2i(960, 540)
	_next_subject()

func _next_subject():
	if _room:
		_room.queue_free()
	_index += 1
	if _index >= _subjects.size():
		print("done: %d subjects" % _subjects.size())
		quit()
		return
	_frame = 0

	_room = load(ROOM).instantiate()
	root.add_child(_room)

	# Deterministic, readable lighting on top of the room's own torches.
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, 30, 0)
	sun.light_energy = 1.0
	_room.add_child(sun)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.55, 0.57, 0.65)
	e.ambient_light_energy = 0.8
	env.environment = e
	_room.add_child(env)

	# Stage at the room's floor center (average of used floor cells, floor top via ray).
	var floor_map: GridMap = _room.get_node("FloorGridMap")
	var acc := Vector3.ZERO
	var cells := floor_map.get_used_cells()
	for c in cells:
		acc += floor_map.to_global(floor_map.map_to_local(c))
	_center = acc / cells.size()
	_center.y = _floor_top(_center)

	# Match the game camera: orthogonal, pitched ~50 degrees on a 45-degree diagonal
	# (see Camera3D in scenes/main/main.tscn), just with a tighter ortho size for close-ups.
	var focus := _center + Vector3(0.85, 0.8, 0)
	var cam_pos := focus + Vector3(0.4545, 0.766, 0.4545) * 12.0
	var to_cam := Vector3(cam_pos.x - _center.x, 0, cam_pos.z - _center.z)

	_subject = load(_subjects[_index]).instantiate()
	_room.add_child(_subject)
	_subject.global_position = _center
	# glTF models face +Z, so aim -Z away from the camera; enemy scenes additionally have a
	# 180-degree flip baked into their Pivot, which cancels back out.
	_subject.look_at(_center - to_cam, Vector3.UP)
	if _subject.get_node_or_null("Pivot"):
		_subject.rotate_y(PI)
	_killable = _subject.get_node_or_null("HealthComponent") != null
	var bar := _subject.get_node_or_null("FloatingHealthBar")
	if bar:
		bar.queue_free()  # it re-shows itself on damage and renders as a black box here

	var mannequin: Node3D = load(REFERENCE).instantiate()
	_room.add_child(mannequin)
	# Offset perpendicular to the camera so subject and reference never overlap on screen.
	var side := Vector3(to_cam.z, 0, -to_cam.x).normalized()
	mannequin.global_position = _center + side * 1.9
	mannequin.look_at(mannequin.global_position - to_cam, Vector3.UP)

	var cam := Camera3D.new()
	_room.add_child(cam)
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.size = 6.5
	cam.look_at_from_position(cam_pos, focus, Vector3.UP)
	cam.current = true

func _floor_top(above: Vector3) -> float:
	var from := above + Vector3.UP * 3.0
	var q := PhysicsRayQueryParameters3D.create(from, from + Vector3.DOWN * 8.0, 1)
	var hit := root.get_world_3d().direct_space_state.intersect_ray(q)
	return hit["position"].y if hit.has("position") else above.y

func _shot(suffix: String):
	var img := root.get_viewport().get_texture().get_image()
	var name: String = _subjects[_index].get_file().get_basename()
	var path := _outdir.path_join("%s_%s.png" % [name, suffix])
	img.save_png(path)
	print("shot ", path)

func _process(_delta):
	_frame += 1
	match _frame:
		10:
			_shot("spawn")  # for flyers this catches the take-off toward hover altitude
		70:
			_shot("idle")
			if not _killable:
				_next_subject()
		80:
			if _killable:
				_subject.get_node("HealthComponent").call("TakeDamage", 999.0)
		110:
			if _killable:
				_shot("dying")
		200:
			if _killable:
				_shot("dead")
				_next_subject()
	return false
