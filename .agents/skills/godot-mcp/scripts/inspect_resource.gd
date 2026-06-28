extends SceneTree
## Loads one or more resources headless and prints their script properties, so a
## hand-authored or freshly-edited `.tres` can be validated without opening the editor
## or writing a one-off script. Confirms the file parses, the script binds, exported
## enums/arrays read back as expected, and (for C# resources) computed properties resolve.
##
## Usage:
##   scripts/godot.sh --headless --path "$PWD" --script \
##     .agents/skills/godot-mcp/scripts/inspect_resource.gd -- res://path/a.tres res://path/b.tres
##
## Run `--import` once first if loads fail with missing `.godot/imported/...` errors.

func _init() -> void:
	var paths := OS.get_cmdline_user_args()
	if paths.is_empty():
		print("usage: ... --script inspect_resource.gd -- <res://path.tres> [more...]")
		quit(1)
		return

	var failures := 0
	for path in paths:
		if not _inspect(path):
			failures += 1

	quit(1 if failures > 0 else 0)

func _inspect(path: String) -> bool:
	if not ResourceLoader.exists(path):
		print("MISSING  ", path, "  (unknown path, or run `--import` to build the cache first)")
		return false

	var res := ResourceLoader.load(path)
	if res == null:
		print("FAIL     ", path, "  (failed to load — check the errors above for the cause)")
		return false

	print("OK       ", path, "  [", res.get_class(), "]")
	for prop in res.get_property_list():
		if (prop.usage & PROPERTY_USAGE_SCRIPT_VARIABLE) == 0:
			continue
		print("           ", prop.name, " = ", _summarize(res.get(prop.name)))
	return true

func _summarize(value) -> String:
	if value is Array:
		return "Array(%d)" % value.size()
	if value is Dictionary:
		return "Dictionary(%d)" % value.size()
	if value is Resource:
		var sub := value as Resource
		return "%s(%s)" % [sub.get_class(), sub.resource_path if sub.resource_path else "inline"]
	return str(value)
