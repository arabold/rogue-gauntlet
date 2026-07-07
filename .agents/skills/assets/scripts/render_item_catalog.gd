extends SceneTree
#
# Render a transparent-background PNG of every item's world model, for a visual item
# catalog (mirrors render_catalog.gd's role for the monster bestiary). Loads each item
# .tres, instances its `Scene`, and frames it with the same auto-center/auto-rotate logic
# Preview.cs uses for the in-game inventory preview, so catalog thumbnails match what
# players actually see. Shows items in their true (untinted) appearance — no per-run
# identity disguise tint — since this is a developer-facing reference, not gameplay.
#
# MUST run WINDOWED (not --headless): the headless dummy renderer produces blank images
# and hangs. A real window briefly opens (uses the machine's GPU).
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script .agents/skills/assets/scripts/render_item_catalog.gd -- <outdir> <res://item.tres> [more...]
#
# e.g. -- docs/item-catalog res://scenes/items/weapons/axe_common.tres res://scenes/items/armor/shield_common.tres

var _items = []
var _i = 0
var _wait = 0
var _vp
var _cam
var _holder

func _initialize():
	var args = OS.get_cmdline_user_args()
	var outdir = args[0]
	for src in args.slice(1):
		var name = src.get_file().get_basename()
		_items.append([src, outdir.path_join(name + ".png")])
	print("found %d items" % _items.size())

	_vp = SubViewport.new()
	_vp.size = Vector2i(512, 512)
	_vp.transparent_bg = true
	_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(_vp)

	_cam = Camera3D.new()
	_vp.add_child(_cam)
	_cam.transform = Transform3D(Basis(), Vector3(0, 0, 5))
	_cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	_cam.size = 3.0

	var key = DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-45, 35, 0)
	key.light_energy = 1.2
	_vp.add_child(key)

	var we = WorldEnvironment.new()
	var e = Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0, 0, 0, 0)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.65, 0.65, 0.7)
	e.ambient_light_energy = 1.1
	we.environment = e
	_vp.add_child(we)
	# _load_current is deferred to the first _process tick: computing AABBs on nodes added
	# to the tree in the same frame as the SubViewport itself can report a stale/identity
	# global_transform for nested children before Godot's first frame settles.

func _load_current():
	if _holder:
		_holder.free()
	var item = load(_items[_i][0])
	var model = item.Scene.instantiate()
	# Frame via a wrapper pivot rather than the model itself: a few item scenes bake a
	# non-identity scale into their own root (e.g. the warhammers), and
	# rotate_object_local/translate_object_local move a node along its own basis — on a
	# scaled root that basis scales the correction too, overshooting and clipping the model.
	# The pivot always starts at identity, so framing is correct regardless of the loaded
	# scene's own root transform. Mirrors Preview.cs's identical fix for the in-game preview.
	_holder = Node3D.new()
	_vp.add_child(_holder)
	_holder.add_child(model)
	_center_object_to_camera(model, _holder)
	_wait = 0

func _compute_aabb(node: Node3D) -> AABB:
	var aabb = AABB()
	if node is VisualInstance3D:
		aabb = node.global_transform * node.get_aabb()
	for child in node.get_children():
		if child is Node3D:
			var child_aabb = _compute_aabb(child)
			aabb = aabb.merge(child_aabb)
	return aabb

func _center_object_to_camera(model: Node3D, pivot: Node3D):
	var aabb = _compute_aabb(model)
	var distance = max(aabb.size.x, aabb.size.y)
	if aabb.size.x > aabb.size.y:
		pivot.rotate_object_local(Vector3.RIGHT, PI / 2)
		distance = max(aabb.size.z, aabb.size.y)
	elif aabb.size.z > aabb.size.y:
		pivot.rotate_object_local(Vector3.FORWARD, PI / 2)
		distance = max(aabb.size.x, aabb.size.z)
	var center = aabb.get_center()
	pivot.translate_object_local(-center)
	_cam.size = max(distance * 1.25, 0.35)

func _process(_d):
	if _holder == null and _wait == 0:
		_load_current()
		return false
	_wait += 1
	if _wait < 8:
		return false
	_vp.get_texture().get_image().save_png(_items[_i][1])
	print("shot " + _items[_i][1].get_file())
	_i += 1
	if _i >= _items.size():
		return true
	_load_current()
	return false
