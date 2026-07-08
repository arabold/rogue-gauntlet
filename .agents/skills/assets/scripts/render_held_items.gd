extends SceneTree
#
# Equips a sequence of weapons/shields on a standalone player and screenshots the
# character from three angles per item, to visually verify in-hand scale/position/
# orientation. Complements render_item_catalog.gd, which frames an item alone and
# auto-rotates for a nice thumbnail — that framing can hide exactly the defects that
# only show up once a model is actually bone-attached (wrong local-axis convention,
# scale inconsistent with sibling tiers, etc.). Always verify new weapons/shields here,
# not just in the floating catalog. Windowed only (see render_item_catalog.gd's note).
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script .agents/skills/assets/scripts/render_held_items.gd -- <outdir> <res://item.tres> [more...]
#
# Produces <outdir>/<item>_front.png (2H ranged weapons aim here), <outdir>/<item>_weapon.png
# (3/4 view favoring the weapon hand), <outdir>/<item>_shield.png (3/4 view favoring the
# shield hand) for every item passed.

var _items = []
var _i = 0
var _angle_i = 0
var _wait = 0
const WAIT_FRAMES = 10
var _vp
var _cam
var _player
var _inventory
var _outdir = ""
var _started = false
var _settle = 0
const SETTLE_FRAMES = 30 # let gravity/collision settle the character on the floor first
var _frame_count = 0
const MAX_FRAMES = 6000 # safety cap (~100s @60fps) so a script bug can't hang indefinitely
var _player_aabb_center = Vector3.ZERO
var _player_aabb_size = Vector3.ZERO

# (suffix, direction) — direction is XZ-planar (dir.y == 0) so the camera's up basis stays
# exactly world-Y; any pitch tilts "up" to pick up X/Z components, which combined with
# front-to-back depth differences on the model (e.g. a hat vs. a cape) skews framing
# asymmetrically. See the godot-mcp skill's tres-authoring reference for why.
const ANGLES = [
	["front", Vector3(0, 0, -1)],
	["weapon", Vector3(0.8, 0, 0.9)],
	["shield", Vector3(-0.9, 0, 0.7)],
]

func _initialize():
	var args = OS.get_cmdline_user_args()
	_outdir = args[0]
	for src in args.slice(1):
		_items.append(src)
	print("found %d items" % _items.size())

	_vp = SubViewport.new()
	_vp.size = Vector2i(512, 640)
	_vp.transparent_bg = false
	_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(_vp)

	_cam = Camera3D.new()
	_vp.add_child(_cam)

	var key = DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-45, 35, 0)
	key.light_energy = 1.2
	_vp.add_child(key)

	var we = WorldEnvironment.new()
	var e = Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.12, 0.12, 0.15, 1)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.65, 0.65, 0.7)
	e.ambient_light_energy = 1.1
	we.environment = e
	_vp.add_child(we)

	# Player is a physics-driven CharacterBody3D; without a floor it free-falls under gravity
	# for however many physics ticks elapse (decoupled from our idle _process count, and
	# inflated by first-load shader/asset stalls), landing at an unpredictable Y by the time
	# we screenshot. A floor stops it after a frame or two so framing is stable.
	var floor_body = StaticBody3D.new()
	var floor_shape = CollisionShape3D.new()
	var box = BoxShape3D.new()
	box.size = Vector3(20, 1, 20)
	floor_shape.shape = box
	floor_body.add_child(floor_shape)
	floor_body.position = Vector3(0, -0.5, 0)
	floor_body.collision_layer = 1
	_vp.add_child(floor_body)

	var player_scene = load("res://scenes/player/player.tscn")
	_player = player_scene.instantiate()
	_vp.add_child(_player)

func _compute_aabb(node: Node3D) -> AABB:
	var aabb = AABB()
	# MeshInstance3D only: VisualInstance3D also covers Light3D, whose "AABB" reflects light
	# range (tens of units for the player rig's helper lights), which would blow out framing.
	if node is MeshInstance3D:
		aabb = node.global_transform * node.get_aabb()
	for child in node.get_children():
		if child is Node3D:
			var child_aabb = _compute_aabb(child)
			if aabb.size == Vector3.ZERO:
				aabb = child_aabb
			elif child_aabb.size != Vector3.ZERO:
				aabb = aabb.merge(child_aabb)
	return aabb

func _measure_player():
	var aabb = _compute_aabb(_player)
	print("player aabb center=%s size=%s" % [aabb.get_center(), aabb.size])
	_player_aabb_center = aabb.get_center()
	_player_aabb_size = aabb.size

func _point_camera(dir: Vector3):
	# Orthogonal avoids perspective distance/FOV math entirely (same approach as
	# render_item_catalog.gd) — `size` alone controls framing, direction only picks the angle.
	_cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	_cam.size = max(_player_aabb_size.x, _player_aabb_size.z, _player_aabb_size.y) * 1.4
	var distance = max(_player_aabb_size.x, _player_aabb_size.y, _player_aabb_size.z) * 4.0
	_cam.look_at_from_position(_player_aabb_center + dir * distance, _player_aabb_center, Vector3.UP)

func _process(_d):
	_frame_count += 1
	if _frame_count > MAX_FRAMES:
		push_error("render_held_items: MAX_FRAMES safety cap hit, aborting")
		return true
	if not _started:
		_started = true
		_inventory = _player.Inventory
		return false
	if _settle < SETTLE_FRAMES:
		_settle += 1
		if _settle == SETTLE_FRAMES:
			_measure_player()
		return false

	if _wait == 0:
		if _angle_i == 0:
			var item = load(_items[_i])
			var slot = InventoryItemSlot.new()
			slot.Item = item
			_inventory.Equip(slot)
		_point_camera(ANGLES[_angle_i][1])
		_wait = 1
		return false

	_wait += 1
	if _wait < WAIT_FRAMES:
		return false

	var name = _items[_i].get_file().get_basename()
	var suffix = ANGLES[_angle_i][0]
	var outpath = _outdir.path_join("%s_%s.png" % [name, suffix])
	_vp.get_texture().get_image().save_png(outpath)
	print("shot " + outpath.get_file())

	_angle_i += 1
	if _angle_i < ANGLES.size():
		_wait = 0
		return false

	_angle_i = 0
	_i += 1
	if _i >= _items.size():
		return true
	_wait = 0
	return false
