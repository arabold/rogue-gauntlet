extends SceneTree
#
# Render a transparent-background PNG of every item's world model, for a visual item
# catalog (mirrors render_catalog.gd's role for the monster bestiary). Loads each item
# .tres and shows it through the in-game inventory icon itself (res://scenes/ui/preview.tscn,
# Preview.cs), so catalog thumbnails match what players actually see. Shows items in their
# true (untinted) appearance — no per-run identity disguise tint — since this is a
# developer-facing reference, not gameplay. Build the C# project first (`dotnet build`).
#
# MUST run WINDOWED (not --headless): the headless dummy renderer produces blank images
# and hangs. A real window briefly opens (uses the machine's GPU).
#
#   .agents/skills/godot-mcp/scripts/godot.sh --path "$PWD" \
#     --script asset-studio/godot/render_item_catalog.gd -- <outdir> <res://item.tres> [more...]
#
# e.g. -- docs/item-catalog res://scenes/items/weapons/axe_common.tres res://scenes/items/armor/shield_common.tres

var _items = []
var _i = 0
var _wait = -1
var _preview

func _initialize():
	var args = OS.get_cmdline_user_args()
	var outdir = args[0]
	for src in args.slice(1):
		# Identity types (potions, scrolls, jewelry) have no model of their own; their looks are
		# the per-run appearances shown in the Asset Studio lineups.
		if load(src).get("Scene") == null:
			print("skip %s: no own model" % src)
			continue
		var name = src.get_file().get_basename()
		_items.append([src, outdir.path_join(name + ".png")])
	print("found %d items" % _items.size())
	if _items.is_empty():
		quit()
		return

	_preview = load("res://scenes/ui/preview.tscn").instantiate()
	_preview.size = Vector2i(512, 512)
	root.add_child(_preview)

func _process(_d):
	if _wait < 0:
		_preview.ShowItem(load(_items[_i][0]))
		_wait = 0
		return false
	# The preview renders once per change; give it a few frames to land before reading it back.
	_wait += 1
	if _wait < 8:
		return false
	_preview.get_texture().get_image().save_png(_items[_i][1])
	print("shot " + _items[_i][1].get_file())
	_i += 1
	if _i >= _items.size():
		return true
	_wait = -1
	return false
