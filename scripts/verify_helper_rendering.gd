extends SceneTree
## Windowed pixel regression: invisible actor passes must remain colorless in a fogged world.
## Run with --script res://scripts/verify_helper_rendering.gd -- <output-directory>.

const HARNESS := "res://scenes/debug/combat/combat_harness.tscn"
const SKELETON := "res://scenes/enemies/skeleton/skeleton_warrior.tscn"
const TELEPORT_DEPTH := "res://scenes/effects/world/shaders/teleport_depth.gdshader"

var _scene: Node3D
var _monster: Node3D
var _xray: Node
var _depth_helpers: Array[MeshInstance3D] = []
var _reference: Image
var _frame := 0
var _failures := 0
var _checks := 0
var _output: String


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		push_error("This check needs a windowed renderer; headless screenshots are blank.")
		quit(2)
		return
	var args := OS.get_cmdline_user_args()
	_output = args[0] if not args.is_empty() else OS.get_cache_dir().path_join("rogue-helper-rendering")
	if DirAccess.make_dir_recursive_absolute(_output) != OK:
		push_error("Cannot create capture directory: " + _output)
		quit(2)
		return
	call_deferred("_start")


func _start() -> void:
	for action in InputMap.get_actions():
		InputMap.action_erase_events(action)
	_scene = load(HARNESS).instantiate()
	root.add_child(_scene)
	current_scene = _scene
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(960, 540)


func _process(_delta: float) -> bool:
	_frame += 1
	match _frame:
		20:
			_scene.get_node("UI").visible = false
			_scene.get_node("Instructions").visible = false
			var commands = _scene.get_node("Instructions")
			commands.ExecuteCommand("position_player", "", 0, Vector3(-1.4, 0, 2))
			if not commands.ExecuteCommand("spawn", SKELETON, 2, Vector3.ZERO):
				push_error("Could not stage the production skeleton.")
				quit(2)
				return false
			commands.ExecuteCommand("position_monster", "", 0, Vector3.ZERO)
			_monster = get_nodes_in_group("enemy")[0]
			_monster.reset_physics_interpolation()
			_xray = _monster.get_node("OcclusionXrayComponent")
			_scene.get_node("GameCamera").Camera.size = 8.0
			var environment: Environment = _scene.get_node("WorldLighting").environment
			environment.fog_enabled = true
			environment.fog_density = 0.04
			environment.fog_height_density = 0.0
		30:
			paused = true
			_xray.IsEnabled = false
		35:
			_reference = _capture("emerging-reference")
		36:
			_xray.IsEnabled = true
		40:
			_expect_unchanged("emerging-xray")
		41:
			_xray.IsEnabled = false
			paused = false
		45:
			# The ground-emergence pose deliberately puts limbs below the floor; use a standing pose here.
			_monster.get_node("EnemyBehaviorComponent/AnimationTree").active = false
			var animations: AnimationPlayer = _monster.get_node("Pivot/Skeleton_Warrior/AnimationPlayer")
			animations.play("Idle_Combat")
			animations.seek(0.0, true)
			animations.pause()
		55:
			_make_depth_helpers()
			paused = true
			_xray.IsEnabled = false
		60:
			_reference = _capture("visible-reference")
		65:
			_xray.IsEnabled = true
		70:
			_expect_unchanged("visible-xray")
		75:
			_xray.IsEnabled = false
			for helper in _depth_helpers:
				helper.visible = true
		80:
			_expect_unchanged("teleport-depth")
		85:
			for helper in _depth_helpers:
				helper.visible = false
			_make_wall()
		90:
			_reference = _capture("occluded-reference")
		95:
			_xray.IsEnabled = true
		100:
			_expect_xray("occluded-xray")
		105:
			_xray.IsEnabled = false
			_scene.get_node("GameCamera").Camera.far = 80.0
		110:
			_reference = _capture("short-clip-reference")
		115:
			_xray.IsEnabled = true
		120:
			_expect_xray("short-clip-xray")
		125:
			if _checks != 5:
				push_error("Not all rendering checks completed.")
				_failures += 1
			print("Helper rendering: %d failure(s). Captures: %s" % [_failures, _output])
			quit(1 if _failures > 0 else 0)
	return false


func _make_depth_helpers() -> void:
	var material := ShaderMaterial.new()
	material.shader = load(TELEPORT_DEPTH)
	for source in _monster.get_node("Pivot").find_children("*", "MeshInstance3D", true, false):
		if source.has_meta("visual_helper"):
			continue
		var helper := MeshInstance3D.new()
		helper.mesh = source.mesh
		helper.skin = source.skin
		helper.skeleton = source.skeleton
		helper.transform = source.transform
		helper.layers = 1 << 9
		helper.material_override = material
		helper.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		helper.visible = false
		helper.set_meta("visual_helper", true)
		source.get_parent().add_child(helper)
		_depth_helpers.append(helper)


func _make_wall() -> void:
	var camera: Camera3D = _scene.get_node("GameCamera").Camera
	print("Checking x-ray with camera far plane: ", camera.far)
	var toward_camera := Vector3(camera.global_basis.z.x, 0, camera.global_basis.z.z).normalized()
	var mesh := BoxMesh.new()
	mesh.size = Vector3(3.0, 1.2, 0.3)
	var wall := MeshInstance3D.new()
	wall.mesh = mesh
	_scene.add_child(wall)
	wall.position = _monster.global_position + toward_camera * 0.5 + Vector3.UP * 0.6
	wall.rotation.y = atan2(toward_camera.x, toward_camera.z)


func _capture(label: String) -> Image:
	var image := root.get_texture().get_image()
	image.save_png(_output.path_join(label + ".png"))
	image.resize(640, 360, Image.INTERPOLATE_NEAREST)
	image.convert(Image.FORMAT_RGB8)
	return image


func _expect_unchanged(label: String) -> void:
	var actual := _capture(label).get_data()
	var expected := _reference.get_data()
	var changed := 0
	for i in range(0, actual.size(), 3):
		if _pixel_difference(actual, expected, i) > 2:
			changed += 1
	# Allow a few raster-edge rounding differences, never a visible layer over the actor.
	print("%s: %d changed pixels" % [label, changed])
	if changed > 8:
		push_error("%s added visible color to an unobscured actor." % label)
		_failures += 1
	_checks += 1


func _expect_xray(label: String) -> void:
	var actual := _capture(label).get_data()
	var expected := _reference.get_data()
	var red_pixels := 0
	for i in range(0, actual.size(), 3):
		if _pixel_difference(actual, expected, i) > 2 and actual[i] > actual[i + 1] * 1.5 and actual[i] > actual[i + 2] * 1.5:
			red_pixels += 1
	print("%s: %d silhouette pixels" % [label, red_pixels])
	if red_pixels < 20:
		push_error("The occluded skeleton has no readable x-ray silhouette.")
		_failures += 1
	_checks += 1


func _pixel_difference(actual: PackedByteArray, expected: PackedByteArray, i: int) -> int:
	return maxi(absi(actual[i] - expected[i]), maxi(absi(actual[i + 1] - expected[i + 1]), absi(actual[i + 2] - expected[i + 2])))
