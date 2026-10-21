@tool
extends EditorPlugin

const DEFAULT_BUILDER_PATH = "res://scenes/levels/dungeon/dungeon_procedural_room_builder.tres"
const DEFAULT_ROOMS_DIR = "res://scenes/levels/dungeon/rooms/"

const SHAPE_NAMES: PackedStringArray = [
	"Random Shape",
	"Rectangle",
	"L-Shape",
	"Stepped Z",
	"4-Way Cross",
	"T-Junction",
	"U-Shape"
]

const MATERIAL_NAMES: PackedStringArray = [
	"Random Material",
	"Stone Slab",
	"Cobble Grid",
	"Wood Planks",
	"Stone & Wood",
	"Earthen",
	"Ruined Mix"
]

const PURPOSE_NAMES: PackedStringArray = [
	"Random Purpose",
	"Chamber",
	"Great Hall",
	"Temple",
	"Colonnade",
	"Sewer Cistern",
	"Sewer Aqueduct",
	"Ruined Collapse"
]

# 3D Toolbar UI
var _toolbar: HBoxContainer
var _shape_dropdown: OptionButton
var _material_dropdown: OptionButton
var _purpose_dropdown: OptionButton

# "Generate Room Scene" Menu Dialog
var _new_room_dialog: ConfirmationDialog
var _name_edit: LineEdit
var _new_shape_dropdown: OptionButton
var _new_material_dropdown: OptionButton
var _new_purpose_dropdown: OptionButton
var _dir_edit: LineEdit
var _dir_dialog: EditorFileDialog
var _open_scene_check: CheckBox

# Configuration Dialog Controls
var _config_dialog: AcceptDialog
var _cfg_builder_edit: LineEdit
var _cfg_min_size: SpinBox
var _cfg_max_size: SpinBox
var _cfg_generate_walls_check: CheckBox
var _cfg_min_openings: SpinBox
var _cfg_max_openings: SpinBox
var _cfg_min_doors: SpinBox
var _cfg_max_doors: SpinBox
var _cfg_wear_chance: SpinBox
var _cfg_weeds_density: SpinBox
var _cfg_open_grate_chance: SpinBox
var _cfg_corner_overlays_check: CheckBox
var _cfg_column_chance: SpinBox
var _cfg_enable_chasms_check: CheckBox
var _cfg_chasm_chance: SpinBox
var _cfg_random_seed_check: CheckBox
var _cfg_seed_spin: SpinBox


func _enter_tree() -> void:
	_create_toolbar()
	_create_config_dialog()
	_create_new_room_dialog()

	add_tool_menu_item("Generate Room Scene...", _on_menu_generate_room_scene)
	scene_changed.connect(_on_scene_changed)

	_update_toolbar_visibility()


func _exit_tree() -> void:
	remove_tool_menu_item("Generate Room Scene...")

	if scene_changed.is_connected(_on_scene_changed):
		scene_changed.disconnect(_on_scene_changed)

	if _toolbar:
		remove_control_from_container(CustomControlContainer.CONTAINER_SPATIAL_EDITOR_MENU, _toolbar)
		_toolbar.queue_free()

	if _new_room_dialog:
		_new_room_dialog.queue_free()
	if _dir_dialog:
		_dir_dialog.queue_free()
	if _config_dialog:
		_config_dialog.queue_free()


func _handles(_object: Object) -> bool:
	return _is_editing_room()


func _make_visible(visible: bool) -> void:
	if _toolbar:
		_toolbar.visible = visible and _is_editing_room()


func _is_editing_room() -> bool:
	return EditorInterface.get_edited_scene_root() is Room


func _on_scene_changed(_root: Node) -> void:
	_update_toolbar_visibility()


func _update_toolbar_visibility() -> void:
	if _toolbar:
		_toolbar.visible = _is_editing_room()


# -----------------------------------------------------------------------------
# Shared UI Helpers
# -----------------------------------------------------------------------------

static func _create_dropdown(items: PackedStringArray, tooltip: String = "") -> OptionButton:
	var dropdown = OptionButton.new()
	for i in range(items.size()):
		dropdown.add_item(items[i], i)
	dropdown.tooltip_text = tooltip
	return dropdown


static func _add_labeled_control(grid: GridContainer, label_text: String, control: Control) -> void:
	var lbl = Label.new()
	lbl.text = label_text
	grid.add_child(lbl)
	grid.add_child(control)


static func _add_spin_box(min_val: float, max_val: float, init_val: float, suffix: String = "") -> SpinBox:
	var spin = SpinBox.new()
	spin.min_value = min_val
	spin.max_value = max_val
	spin.value = init_val
	spin.suffix = suffix
	return spin


static func _add_spin_range_row(
	grid: GridContainer,
	label_text: String,
	min_val: float,
	max_val: float,
	init_min: float,
	init_max: float
) -> Array[SpinBox]:
	var row = HBoxContainer.new()
	var min_spin = _add_spin_box(min_val, max_val, init_min)
	var lbl_to = Label.new()
	lbl_to.text = " to "
	var max_spin = _add_spin_box(min_val, max_val, init_max)
	row.add_child(min_spin)
	row.add_child(lbl_to)
	row.add_child(max_spin)
	_add_labeled_control(grid, label_text, row)
	return [min_spin, max_spin]


# -----------------------------------------------------------------------------
# 3D Viewport Toolbar
# -----------------------------------------------------------------------------

func _create_toolbar() -> void:
	_toolbar = HBoxContainer.new()
	_toolbar.name = "RoomGeneratorToolbar"
	_toolbar.add_child(VSeparator.new())

	var label = Label.new()
	label.text = "Room:"
	_toolbar.add_child(label)

	_shape_dropdown = _create_dropdown(SHAPE_NAMES, "Select room footprint shape archetype.")
	_toolbar.add_child(_shape_dropdown)

	_material_dropdown = _create_dropdown(MATERIAL_NAMES, "Select primary surface material palette.")
	_toolbar.add_child(_material_dropdown)

	_purpose_dropdown = _create_dropdown(PURPOSE_NAMES, "Select architectural room purpose (walkways, grates, columns).")
	_toolbar.add_child(_purpose_dropdown)

	var reroll_btn = Button.new()
	reroll_btn.text = "🎲 Re-Roll"
	reroll_btn.tooltip_text = "Regenerate the floor, walls, grates, columns, and openings for this room scene."
	reroll_btn.pressed.connect(_on_reroll_pressed)
	_toolbar.add_child(reroll_btn)

	var config_btn = Button.new()
	config_btn.text = "⚙ Config"
	config_btn.tooltip_text = "Configure room size, walls, openings, doors, wear, weeds, columns, chasms, and seed."
	config_btn.pressed.connect(_on_config_pressed)
	_toolbar.add_child(config_btn)

	add_control_to_container(CustomControlContainer.CONTAINER_SPATIAL_EDITOR_MENU, _toolbar)
	_toolbar.hide()


func _load_configured_builder(shape_idx: int, material_idx: int, purpose_idx: int) -> Resource:
	var builder_path = _cfg_builder_edit.text.strip_edges()
	if builder_path.is_empty():
		builder_path = DEFAULT_BUILDER_PATH

	var builder = load(builder_path)
	if not builder:
		printerr("RoomGenerator: Could not load builder at: ", builder_path)
		return null

	var temp_builder = builder.duplicate()
	var min_tiles = int(_cfg_min_size.value)
	var min_openings = int(_cfg_min_openings.value)
	var min_doors = int(_cfg_min_doors.value)

	temp_builder.ShapeArchetype = shape_idx
	temp_builder.Material = material_idx
	temp_builder.Purpose = purpose_idx
	temp_builder.MinTiles = min_tiles
	temp_builder.MaxTiles = max(min_tiles, int(_cfg_max_size.value))
	temp_builder.GenerateWalls = _cfg_generate_walls_check.button_pressed
	temp_builder.DoorwayChance = 1.0
	temp_builder.MinDoorways = min_openings
	temp_builder.MaxDoorways = max(min_openings, int(_cfg_max_openings.value))
	temp_builder.MinDoors = min_doors
	temp_builder.MaxDoors = max(min_doors, int(_cfg_max_doors.value))
	temp_builder.WearAndRubbleChance = _cfg_wear_chance.value / 100.0
	temp_builder.WeedsDensity = _cfg_weeds_density.value / 100.0
	temp_builder.OpenGrateChance = _cfg_open_grate_chance.value / 100.0
	temp_builder.EnableCornerOverlays = _cfg_corner_overlays_check.button_pressed
	temp_builder.ColumnChance = _cfg_column_chance.value / 100.0
	temp_builder.EnableChasms = _cfg_enable_chasms_check.button_pressed
	temp_builder.ChasmChance = _cfg_chasm_chance.value / 100.0
	return temp_builder


func _on_reroll_pressed() -> void:
	var edited_root = EditorInterface.get_edited_scene_root()
	if not (edited_root is Room):
		print("RoomGenerator: Currently open scene is not a Room.")
		return

	var temp_builder = _load_configured_builder(
		_shape_dropdown.selected,
		_material_dropdown.selected,
		_purpose_dropdown.selected
	)
	if not temp_builder:
		return

	var rng = RandomNumberGenerator.new()
	if _cfg_random_seed_check.button_pressed:
		rng.randomize()
	else:
		rng.seed = int(_cfg_seed_spin.value)

	if temp_builder.PopulateRoom(edited_root, rng):
		print("RoomGenerator: Re-rolled room '%s' [Shape: %s, Material: %s, Purpose: %s, Walls: %s, Seed: %d]." % [
			edited_root.name,
			SHAPE_NAMES[_shape_dropdown.selected],
			MATERIAL_NAMES[_material_dropdown.selected],
			PURPOSE_NAMES[_purpose_dropdown.selected],
			str(temp_builder.GenerateWalls),
			rng.seed
		])
	else:
		printerr("RoomGenerator: Failed to re-roll room layout.")


func _on_config_pressed() -> void:
	if _config_dialog:
		_config_dialog.popup_centered(Vector2i(480, 520))


# -----------------------------------------------------------------------------
# Configuration Dialog
# -----------------------------------------------------------------------------

func _create_config_dialog() -> void:
	_config_dialog = AcceptDialog.new()
	_config_dialog.title = "Room Generator Configuration"

	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 8)
	_config_dialog.add_child(vbox)

	var grid = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 6)
	vbox.add_child(grid)

	_cfg_builder_edit = LineEdit.new()
	_cfg_builder_edit.text = DEFAULT_BUILDER_PATH
	_cfg_builder_edit.custom_minimum_size = Vector2(260, 0)
	_add_labeled_control(grid, "Builder Preset:", _cfg_builder_edit)

	var size_spins = _add_spin_range_row(grid, "Min / Max Room Size (Tiles):", 3, 12, 3, 6)
	_cfg_min_size = size_spins[0]
	_cfg_max_size = size_spins[1]

	_cfg_generate_walls_check = CheckBox.new()
	_cfg_generate_walls_check.text = "Render Walls & Corners"
	_cfg_generate_walls_check.button_pressed = true
	_add_labeled_control(grid, "Perimeter Walls:", _cfg_generate_walls_check)

	var open_spins = _add_spin_range_row(grid, "Min / Max Openings:", 1, 8, 1, 3)
	_cfg_min_openings = open_spins[0]
	_cfg_max_openings = open_spins[1]

	var door_spins = _add_spin_range_row(grid, "Min / Max Doors (in Openings):", 0, 8, 0, 2)
	_cfg_min_doors = door_spins[0]
	_cfg_max_doors = door_spins[1]

	_cfg_wear_chance = _add_spin_box(0, 100, 15, "%")
	_add_labeled_control(grid, "Wear & Rubble:", _cfg_wear_chance)

	_cfg_weeds_density = _add_spin_box(0, 100, 15, "%")
	_add_labeled_control(grid, "Weeds & Moss:", _cfg_weeds_density)

	_cfg_open_grate_chance = _add_spin_box(0, 100, 20, "%")
	_add_labeled_control(grid, "Open Grate Variant:", _cfg_open_grate_chance)

	_cfg_corner_overlays_check = CheckBox.new()
	_cfg_corner_overlays_check.text = "Blend Transitions & Corners"
	_cfg_corner_overlays_check.button_pressed = true
	_add_labeled_control(grid, "Corner Overlay Tiles:", _cfg_corner_overlays_check)

	_cfg_column_chance = _add_spin_box(0, 100, 25, "%")
	_add_labeled_control(grid, "Column Chance:", _cfg_column_chance)

	var chasm_row = HBoxContainer.new()
	_cfg_enable_chasms_check = CheckBox.new()
	_cfg_enable_chasms_check.text = "Enable"
	_cfg_enable_chasms_check.button_pressed = false
	_cfg_chasm_chance = _add_spin_box(0, 100, 20, "%")
	_cfg_chasm_chance.editable = false
	_cfg_enable_chasms_check.toggled.connect(func(pressed: bool): _cfg_chasm_chance.editable = pressed)
	chasm_row.add_child(_cfg_enable_chasms_check)
	chasm_row.add_child(_cfg_chasm_chance)
	_add_labeled_control(grid, "Interior Chasms:", chasm_row)

	_cfg_random_seed_check = CheckBox.new()
	_cfg_random_seed_check.text = "Randomize Seed on Re-Roll"
	_cfg_random_seed_check.button_pressed = true
	vbox.add_child(_cfg_random_seed_check)

	var seed_box = HBoxContainer.new()
	var lbl_seed = Label.new()
	lbl_seed.text = "Manual Seed:"
	_cfg_seed_spin = _add_spin_box(0, 999999999, 0)
	_cfg_seed_spin.editable = false
	_cfg_random_seed_check.toggled.connect(func(pressed: bool): _cfg_seed_spin.editable = not pressed)
	seed_box.add_child(lbl_seed)
	seed_box.add_child(_cfg_seed_spin)
	vbox.add_child(seed_box)

	EditorInterface.get_base_control().add_child(_config_dialog)


# -----------------------------------------------------------------------------
# "Generate Room Scene" Menu Dialog
# -----------------------------------------------------------------------------

func _create_new_room_dialog() -> void:
	_new_room_dialog = ConfirmationDialog.new()
	_new_room_dialog.title = "Generate New Room Scene"
	_new_room_dialog.ok_button_text = "Create Scene"

	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 10)
	_new_room_dialog.add_child(vbox)

	var grid = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 8)
	vbox.add_child(grid)

	_name_edit = LineEdit.new()
	_name_edit.text = "new_room"
	_name_edit.custom_minimum_size = Vector2(260, 0)
	_add_labeled_control(grid, "Room Scene Name:", _name_edit)

	_new_shape_dropdown = _create_dropdown(SHAPE_NAMES)
	_add_labeled_control(grid, "Shape Archetype:", _new_shape_dropdown)

	_new_material_dropdown = _create_dropdown(MATERIAL_NAMES)
	_add_labeled_control(grid, "Material Palette:", _new_material_dropdown)

	_new_purpose_dropdown = _create_dropdown(PURPOSE_NAMES)
	_add_labeled_control(grid, "Room Purpose:", _new_purpose_dropdown)

	var dir_row = HBoxContainer.new()
	_dir_edit = LineEdit.new()
	_dir_edit.text = DEFAULT_ROOMS_DIR
	_dir_edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	dir_row.add_child(_dir_edit)

	var browse_btn = Button.new()
	browse_btn.text = "Browse..."
	browse_btn.pressed.connect(_on_browse_dir_pressed)
	dir_row.add_child(browse_btn)
	_add_labeled_control(grid, "Destination Folder:", dir_row)

	_open_scene_check = CheckBox.new()
	_open_scene_check.text = "Open scene in editor after creation"
	_open_scene_check.button_pressed = true
	vbox.add_child(_open_scene_check)

	_dir_dialog = EditorFileDialog.new()
	_dir_dialog.file_mode = EditorFileDialog.FILE_MODE_OPEN_DIR
	_dir_dialog.title = "Select Room Output Folder"
	_dir_dialog.dir_selected.connect(func(dir: String):
		_dir_edit.text = dir if dir.ends_with("/") else dir + "/"
	)
	EditorInterface.get_base_control().add_child(_dir_dialog)

	_new_room_dialog.confirmed.connect(_on_new_room_confirmed)
	EditorInterface.get_base_control().add_child(_new_room_dialog)


func _on_menu_generate_room_scene() -> void:
	if _new_room_dialog:
		_name_edit.text = "new_room"
		_new_room_dialog.popup_centered(Vector2i(460, 300))


func _on_browse_dir_pressed() -> void:
	if _dir_dialog:
		_dir_dialog.current_dir = _dir_edit.text
		_dir_dialog.popup_file_dialog()


func _on_new_room_confirmed() -> void:
	var scene_name = _name_edit.text.strip_edges()
	if scene_name.is_empty():
		printerr("RoomGenerator: Room scene name cannot be empty.")
		return
	if not scene_name.ends_with(".tscn"):
		scene_name += ".tscn"

	var dir_path = _dir_edit.text.strip_edges()
	if not dir_path.ends_with("/"):
		dir_path += "/"

	var file_path = dir_path + scene_name
	var temp_builder = _load_configured_builder(
		_new_shape_dropdown.selected,
		_new_material_dropdown.selected,
		_new_purpose_dropdown.selected
	)
	if not temp_builder:
		return

	if not temp_builder.SaveRoomScene(file_path, 0):
		printerr("RoomGenerator: Failed to create room scene at: ", file_path)
		return

	print("RoomGenerator: Successfully created room scene at: ", file_path)
	EditorInterface.get_resource_filesystem().scan()

	if _open_scene_check.button_pressed:
		call_deferred("_open_scene_deferred", file_path)


func _open_scene_deferred(file_path: String) -> void:
	EditorInterface.open_scene_from_path(file_path)
