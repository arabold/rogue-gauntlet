@tool
extends EditorPlugin
## Room assembly in the editor: reroll an open generated Room scene from its RoomType, or generate a new room scene
## from any RoomType. Both call the production RoomAssembler through RoomType's editor commands.

const DEFAULT_TYPES_DIR := "res://scenes/levels/garrison/room_types/"
const DEFAULT_ROOMS_DIR := "res://scenes/levels/dungeon/rooms/"

var _toolbar: HBoxContainer
var _toolbar_seed: SpinBox
var _toolbar_random: CheckBox

var _dialog: ConfirmationDialog
var _type_edit: LineEdit
var _name_edit: LineEdit
var _dir_edit: LineEdit
var _seed_spin: SpinBox
var _random_check: CheckBox
var _open_check: CheckBox
var _type_dialog: EditorFileDialog
var _dir_dialog: EditorFileDialog


func _enter_tree() -> void:
	_create_toolbar()
	_create_dialog()
	add_tool_menu_item("Generate Room Scene...", _on_menu_generate)
	scene_changed.connect(_on_scene_changed)
	_update_toolbar()


func _exit_tree() -> void:
	remove_tool_menu_item("Generate Room Scene...")
	if scene_changed.is_connected(_on_scene_changed):
		scene_changed.disconnect(_on_scene_changed)
	if _toolbar:
		remove_control_from_container(CustomControlContainer.CONTAINER_SPATIAL_EDITOR_MENU, _toolbar)
		_toolbar.queue_free()
	for node in [_dialog, _type_dialog, _dir_dialog]:
		if node:
			node.queue_free()


func _edited_room() -> Room:
	return EditorInterface.get_edited_scene_root() as Room


func _on_scene_changed(_root: Node) -> void:
	_update_toolbar()


func _update_toolbar() -> void:
	if _toolbar:
		var room := _edited_room()
		_toolbar.visible = room != null and room.Type != null


# -----------------------------------------------------------------------------
# Reroll toolbar: shown for an open Room scene whose Type is assigned.
# -----------------------------------------------------------------------------

func _create_toolbar() -> void:
	_toolbar = HBoxContainer.new()
	_toolbar.add_child(VSeparator.new())
	var label := Label.new()
	label.text = "Room:"
	_toolbar.add_child(label)
	_toolbar_random = CheckBox.new()
	_toolbar_random.text = "Random seed"
	_toolbar_random.button_pressed = true
	_toolbar.add_child(_toolbar_random)
	_toolbar_seed = _make_seed_spin()
	_toolbar.add_child(_toolbar_seed)
	var reroll := Button.new()
	reroll.text = "Re-Roll"
	reroll.tooltip_text = "Reassemble this room's geometry and props from its RoomType. The previous content is replaced."
	reroll.pressed.connect(_on_reroll)
	_toolbar.add_child(reroll)
	add_control_to_container(CustomControlContainer.CONTAINER_SPATIAL_EDITOR_MENU, _toolbar)


func _on_reroll() -> void:
	var room := _edited_room()
	if room == null or room.Type == null:
		printerr("Room generator: assign the room's Type in the Inspector to reroll it.")
		return
	if _toolbar_random.button_pressed:
		_toolbar_seed.value = randi()
	if room.Type.call("Reassemble", room, int(_toolbar_seed.value)):
		EditorInterface.mark_scene_as_unsaved()
		print("Room generator: rerolled '%s' from %s with seed %d." % [room.name, room.Type.resource_path, int(_toolbar_seed.value)])


# -----------------------------------------------------------------------------
# Generate Room Scene dialog (Project > Tools).
# -----------------------------------------------------------------------------

func _create_dialog() -> void:
	_dialog = ConfirmationDialog.new()
	_dialog.title = "Generate Room Scene"
	_dialog.ok_button_text = "Generate"
	_dialog.confirmed.connect(_on_generate_confirmed)
	var grid := GridContainer.new()
	grid.columns = 2
	grid.custom_minimum_size = Vector2(520, 0)
	_dialog.add_child(grid)

	_type_edit = LineEdit.new()
	_type_edit.text = DEFAULT_TYPES_DIR + "barracks.tres"
	_type_dialog = _make_file_dialog(EditorFileDialog.FILE_MODE_OPEN_FILE, func(path): _type_edit.text = path)
	_type_dialog.add_filter("*.tres", "Room type")
	_add_row(grid, "Room type", _with_browse(_type_edit, _type_dialog))

	_name_edit = LineEdit.new()
	_name_edit.text = "generated_room"
	_add_row(grid, "Scene name", _name_edit)

	_dir_edit = LineEdit.new()
	_dir_edit.text = DEFAULT_ROOMS_DIR
	_dir_dialog = _make_file_dialog(EditorFileDialog.FILE_MODE_OPEN_DIR, func(path): _dir_edit.text = path)
	_add_row(grid, "Directory", _with_browse(_dir_edit, _dir_dialog))

	_random_check = CheckBox.new()
	_random_check.text = "Random"
	_random_check.button_pressed = true
	_seed_spin = _make_seed_spin()
	var seed_row := HBoxContainer.new()
	seed_row.add_child(_random_check)
	seed_row.add_child(_seed_spin)
	_add_row(grid, "Seed", seed_row)

	_open_check = CheckBox.new()
	_open_check.text = "Open after generating"
	_open_check.button_pressed = true
	_add_row(grid, "", _open_check)
	EditorInterface.get_base_control().add_child(_dialog)


func _on_menu_generate() -> void:
	_dialog.popup_centered()


func _on_generate_confirmed() -> void:
	var type = load(_type_edit.text)
	if type == null or not type.has_method("SaveRoomScene"):
		printerr("Room generator: assign a RoomType resource.")
		return
	var errors: PackedStringArray = type.call("Validate")
	if not errors.is_empty():
		printerr("Room generator: ", "\n".join(errors))
		return
	var scene_name := _name_edit.text.strip_edges()
	if scene_name.is_empty():
		printerr("Room generator: the scene name cannot be empty.")
		return
	if _random_check.button_pressed:
		_seed_spin.value = randi()
	var path := _dir_edit.text.path_join(scene_name + ".tscn")
	if not type.call("SaveRoomScene", path, int(_seed_spin.value)):
		printerr("Room generator: could not create a room scene at ", path)
		return
	EditorInterface.get_resource_filesystem().scan()
	print("Room generator: created ", path, " with seed ", int(_seed_spin.value))
	if _open_check.button_pressed:
		EditorInterface.open_scene_from_path(path)


# -----------------------------------------------------------------------------
# UI helpers
# -----------------------------------------------------------------------------

func _make_seed_spin() -> SpinBox:
	var spin := SpinBox.new()
	spin.max_value = 4294967295
	spin.step = 1
	spin.custom_minimum_size = Vector2(140, 0)
	return spin


func _make_file_dialog(mode: EditorFileDialog.FileMode, on_selected: Callable) -> EditorFileDialog:
	var dialog := EditorFileDialog.new()
	dialog.file_mode = mode
	dialog.access = EditorFileDialog.ACCESS_RESOURCES
	if mode == EditorFileDialog.FILE_MODE_OPEN_DIR:
		dialog.dir_selected.connect(on_selected)
	else:
		dialog.file_selected.connect(on_selected)
	EditorInterface.get_base_control().add_child(dialog)
	return dialog


func _with_browse(edit: LineEdit, dialog: EditorFileDialog) -> HBoxContainer:
	var row := HBoxContainer.new()
	edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(edit)
	var browse := Button.new()
	browse.text = "…"
	browse.pressed.connect(func(): dialog.popup_file_dialog())
	row.add_child(browse)
	return row


func _add_row(grid: GridContainer, title: String, control: Control) -> void:
	var label := Label.new()
	label.text = title
	grid.add_child(label)
	control.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(control)