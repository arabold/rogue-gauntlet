extends SceneTree
#
# Render a transparent-background PNG of every model in a directory, for a visual catalog.
# Loads each .glb/.gltf, plays its Idle clip, frames it with a 3/4 camera + key light +
# ambient, and captures a SubViewport to <outdir>/<name>.png.
#
# MUST run WINDOWED (not --headless): the headless dummy renderer produces blank images and
# hangs. A real window briefly opens (uses the machine's GPU).
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script .agents/skills/assets/scripts/render_catalog.gd -- <outdir> <res://indir>
#
# e.g. -- docs/monster-catalog res://assets/quaternius-monsters
#
var _models = []; var _i = 0; var _wait = 0; var _vp; var _holder

func _initialize():
	var args = OS.get_cmdline_user_args()
	var outdir = args[0]
	for src in args.slice(1):  # each arg is a directory (scanned) or a single model file
		var d = DirAccess.open(src)
		if d:
			for f in d.get_files():
				if f.get_extension() in ["glb", "gltf"]:
					_models.append([src.path_join(f), outdir.path_join(f.get_basename() + ".png")])
		else:
			_models.append([src, outdir.path_join(src.get_file().get_basename() + ".png")])
	_models.sort()
	print("found %d models" % _models.size())
	_vp = SubViewport.new(); _vp.size = Vector2i(512, 512); _vp.transparent_bg = true
	_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS; root.add_child(_vp)
	var cam = Camera3D.new(); _vp.add_child(cam)
	cam.look_at_from_position(Vector3(3.5, 2.6, 4.0), Vector3(0, 1.2, 0), Vector3.UP)
	var key = DirectionalLight3D.new(); key.rotation_degrees = Vector3(-35, 35, 0); key.light_energy = 1.3
	_vp.add_child(key)
	var we = WorldEnvironment.new(); var e = Environment.new()
	e.background_mode = Environment.BG_COLOR; e.background_color = Color(0, 0, 0, 0)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.62, 0.64, 0.72); e.ambient_light_energy = 1.0
	we.environment = e; _vp.add_child(we)
	_load_current()

func _load_current():
	if _holder: _holder.free()
	_holder = load(_models[_i][0]).instantiate(); _vp.add_child(_holder)
	var ap = _ap(_holder)
	if ap:
		var best = ""  # prefer an exact "Idle", then a combat idle, else any idle clip
		for a in ap.get_animation_list():
			var l = a.to_lower()
			if "idle" in l:
				if best == "" or "combat" in l: best = a
				if l == "idle": best = a; break
		if best != "": ap.play(best)
	_wait = 0

func _process(_d):
	_wait += 1
	if _wait < 8: return false  # let the pose + skinning settle before capture
	_vp.get_texture().get_image().save_png(_models[_i][1])
	print("shot " + _models[_i][1].get_file())
	_i += 1
	if _i >= _models.size(): return true
	_load_current(); return false

func _ap(n):
	if n is AnimationPlayer: return n
	for c in n.get_children():
		var r = _ap(c)
		if r: return r
	return null
