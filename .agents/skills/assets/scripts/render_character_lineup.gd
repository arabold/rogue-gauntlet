extends SceneTree
#
# Renders character GLBs side by side the way the game shows them (each turned 180 degrees about Y like the
# scenes' `Pivot`), each posed at a clip and time, on white under studio lighting (so metal and glow read as they
# do in game), and saves a screenshot. Must run windowed: the headless renderer draws nothing.
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script .agents/skills/assets/scripts/render_character_lineup.gd -- <out.png> \
#     res://assets/kaykit-adventurers/Barbarian.glb Idle 0.3 res://assets/custom-heroes/barbarian.glb Idle 0.3 ...

var _models := []
var _frames := 0
var _out := ""

func _init():
	var args = OS.get_cmdline_user_args()
	_out = args[0]
	var world = Node3D.new()
	root.add_child(world)
	# Studio lighting: a sky lights and reflects (metal needs something to mirror) behind a plain white backdrop,
	# a shadowing key light from the front left, a soft fill from the right, a rim light from behind; filmic
	# tonemapping, ambient occlusion and the game's glow.
	var env = WorldEnvironment.new()
	var e = Environment.new()
	var sky_mat = ProceduralSkyMaterial.new()
	sky_mat.sky_top_color = Color(0.62, 0.7, 0.82)
	sky_mat.sky_horizon_color = Color(0.86, 0.86, 0.84)
	sky_mat.ground_horizon_color = Color(0.7, 0.66, 0.6)
	sky_mat.ground_bottom_color = Color(0.32, 0.3, 0.28)
	e.sky = Sky.new()
	e.sky.sky_material = sky_mat
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(1, 1, 1)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	e.ambient_light_energy = 0.55
	e.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	e.tonemap_white = 6.0
	e.ssao_enabled = true
	e.ssao_intensity = 1.5
	e.glow_enabled = true
	env.environment = e
	world.add_child(env)
	for spec in [[Vector3(-38, 205, 0), 1.6, true], [Vector3(-12, 140, 0), 0.45, false], [Vector3(-25, 15, 0), 1.0, false]]:
		var light = DirectionalLight3D.new()
		light.rotation_degrees = spec[0]
		light.light_energy = spec[1]
		light.shadow_enabled = spec[2]
		world.add_child(light)
	var n = (args.size() - 1) / 3
	for i in n:
		var scene: PackedScene = load(args[1 + 3 * i])
		var pivot = Node3D.new()
		pivot.rotation_degrees.y = 180
		pivot.position.x = ((n - 1) / 2.0 - i) * 2.0  # first model on the left
		world.add_child(pivot)
		var m = scene.instantiate()
		pivot.add_child(m)
		_models.append([m, args[2 + 3 * i], float(args[3 + 3 * i])])
	var cam = Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	cam.keep_aspect = Camera3D.KEEP_WIDTH
	cam.size = 2.0 * n
	cam.position = Vector3(0, 1.3, -10)
	cam.rotation_degrees = Vector3(0, 180, 0)
	world.add_child(cam)
	cam.make_current()
	root.size = Vector2i(300 * n, 390)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_DISABLED
	root.msaa_3d = Viewport.MSAA_4X
	root.transparent_bg = true  # tonemapping would grey a white backdrop: put white behind it afterwards

func _process(_delta):
	_frames += 1
	if _frames == 3:
		for entry in _models:
			var ap: AnimationPlayer = entry[0].get_node("AnimationPlayer")
			ap.play(entry[1])
			ap.seek(entry[2], true)
			ap.pause()
	if _frames == 8:
		var shot := root.get_texture().get_image()
		shot.convert(Image.FORMAT_RGBA8)
		var out := Image.create(shot.get_width(), shot.get_height(), false, Image.FORMAT_RGBA8)
		out.fill(Color(1, 1, 1))
		out.blend_rect(shot, Rect2i(Vector2i.ZERO, shot.get_size()), Vector2i.ZERO)
		out.save_png(_out)
		quit()
	return false
