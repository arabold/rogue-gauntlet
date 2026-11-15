extends SceneTree
## Builds the project theme, res://scenes/ui/theme.tres, from the rendered UI kit (assets/ui-kit) and the fonts:
## every default look and Theme Type Variation the interface picks from (see docs/user-interface.md). The theme is
## written whole each time and keeps its UID, so change a look here and rebuild rather than editing the .tres:
##   .agents/skills/godot-mcp/scripts/godot.sh --headless --path "$PWD" --script asset-studio/godot/build_ui_theme.gd
## Run the kit generator first when a look needs a new piece (asset-studio/ui/ui_kit.py), then import.

const PATH := "res://scenes/ui/theme.tres"
const UID := "uid://b0hdi5gdamdfw"
const KIT := "res://assets/ui-kit/"

const INK := Color("120b06")
const CREAM := Color("f7ecd2")
const MUTED := Color("cdbb9a")
const GOLD := Color("e9c06a")
const GOLD_TEXT := Color("3a2408")
const DANGER := Color("ff8f7a")
const SHADOW := Color(0, 0, 0, 0.6)

var display: FontVariation
var display_semi: FontVariation
var body: FontVariation
var body_strong: FontVariation


func _init() -> void:
	var theme := Theme.new()
	var tag := TextServerManager.get_primary_interface().name_to_tag("wght")
	var alegreya: FontFile = load("res://assets/fonts/alegreya/Alegreya-Variable.ttf")
	# Both families default to old-style figures, which read as letters in a HUD ("1" as "I", "0" as "o"): every
	# font asks for lining figures instead.
	var lining := {TextServerManager.get_primary_interface().name_to_tag("lnum"): 1}
	display = FontVariation.new()
	display.base_font = alegreya
	display.variation_opentype = {tag: 800}
	display.opentype_features = lining
	display_semi = FontVariation.new()
	display_semi.base_font = alegreya
	display_semi.variation_opentype = {tag: 700}
	display_semi.opentype_features = lining
	body = FontVariation.new()
	body.base_font = load("res://assets/fonts/alegreya-sans/AlegreyaSans-Bold.ttf")
	body.opentype_features = lining
	body_strong = FontVariation.new()
	body_strong.base_font = load("res://assets/fonts/alegreya-sans/AlegreyaSans-ExtraBold.ttf")
	body_strong.opentype_features = lining

	theme.default_font = body
	theme.default_font_size = 28

	_labels(theme)
	_buttons(theme)
	_panels(theme)
	_bars(theme)
	_scrollbars(theme)
	_popups(theme)
	_windows(theme)

	var error := ResourceSaver.save(theme, PATH)
	# The saver writes no header UID for a fresh resource; project.godot's gui/theme/custom references this one.
	var text := FileAccess.get_file_as_string(PATH)
	var file := FileAccess.open(PATH, FileAccess.WRITE)
	file.store_string(text.replace('[gd_resource type="Theme" format=3]', '[gd_resource type="Theme" format=3 uid="%s"]' % UID))
	file.close()
	print("saved %s: %s" % [PATH, error_string(error)])
	quit()


## A 9-slice from the kit: `slice` is the texture margin, `content` the inner padding (left, top, right, bottom).
func kit(name: String, slice: Vector4, content: Vector4, tint := Color.WHITE) -> StyleBoxTexture:
	var box := StyleBoxTexture.new()
	box.texture = load(KIT + name + ".png")
	box.texture_margin_left = slice.x
	box.texture_margin_top = slice.y
	box.texture_margin_right = slice.z
	box.texture_margin_bottom = slice.w
	box.content_margin_left = content.x
	box.content_margin_top = content.y
	box.content_margin_right = content.z
	box.content_margin_bottom = content.w
	box.modulate_color = tint
	return box


func glow() -> StyleBoxTexture:
	# The kit's glow ring sits 16 px inside its texture; expanding by that lays the ring on the control's edge.
	var box := kit("slot_glow", Vector4(40, 40, 40, 40), Vector4(0, 0, 0, 0))
	box.set_expand_margin_all(16)
	return box


func _labels(theme: Theme) -> void:
	theme.set_color("font_color", "Label", CREAM)
	theme.set_color("font_outline_color", "Label", INK)
	theme.set_color("font_shadow_color", "Label", SHADOW)
	theme.set_constant("outline_size", "Label", 4)
	theme.set_constant("shadow_offset_y", "Label", 2)

	# The type scale: display, title and heading in the serif; body, captions and small caps in the sans.
	_label_variation(theme, "DisplayLabel", display, 120, CREAM, 10)
	_label_variation(theme, "TitleLabel", display, 64, CREAM, 8)
	_label_variation(theme, "HeaderLabel", display, 44, CREAM, 6)
	_label_variation(theme, "AccentLabel", display, 32, GOLD, 5)
	_label_variation(theme, "CaptionLabel", body, 24, MUTED, 4)
	_label_variation(theme, "SmallCapsLabel", body_strong, 20, MUTED, 4)
	_label_variation(theme, "DangerTitleLabel", display, 80, DANGER, 8)
	_label_variation(theme, "WarningLabel", body_strong, 24, DANGER, 4)
	# A stat's value in the run menu, and the gold bonus gear adds to it.
	_label_variation(theme, "ValueLabel", display, 30, CREAM, 5)
	_label_variation(theme, "BonusLabel", body_strong, 22, GOLD, 4)
	# Numbers on the game: bar values, stack counts and costs.
	_label_variation(theme, "CountLabel", body_strong, 22, CREAM, 5)

	# A gold ribbon tag ("Tip") whose swallowtail ends stay fixed while its middle stretches.
	theme.set_type_variation("TagLabel", "Label")
	theme.set_font("font", "TagLabel", display)
	theme.set_font_size("font_size", "TagLabel", 26)
	theme.set_color("font_color", "TagLabel", GOLD_TEXT)
	theme.set_constant("outline_size", "TagLabel", 0)
	theme.set_color("font_shadow_color", "TagLabel", Color(1, 0.95, 0.8, 0.35))
	theme.set_constant("shadow_offset_y", "TagLabel", 1)
	theme.set_stylebox("normal", "TagLabel", kit("ribbon", Vector4(30, 0, 30, 0), Vector4(36, 8, 36, 12)))


func _label_variation(theme: Theme, name: String, font: Font, size: int, color: Color, outline: int) -> void:
	theme.set_type_variation(name, "Label")
	theme.set_font("font", name, font)
	theme.set_font_size("font_size", name, size)
	theme.set_color("font_color", name, color)
	theme.set_constant("outline_size", name, outline)


func _buttons(theme: Theme) -> void:
	_plaque(theme, "Button", "button", CREAM, INK, 5)
	theme.set_type_variation("PrimaryButton", "Button")
	_plaque(theme, "PrimaryButton", "button_primary", GOLD_TEXT, INK, 0)

	# Carousel arrows: the kit's carved dart as the button's icon, with no plaque behind it.
	theme.set_type_variation("ArrowButton", "Button")
	for state in ["normal", "hover", "pressed", "hover_pressed", "disabled"]:
		theme.set_stylebox(state, "ArrowButton", StyleBoxEmpty.new())
	theme.set_stylebox("focus", "ArrowButton", glow())
	theme.set_color("icon_normal_color", "ArrowButton", Color(0.86, 0.86, 0.86))
	theme.set_color("icon_hover_color", "ArrowButton", Color(1.2, 1.2, 1.2))
	theme.set_color("icon_focus_color", "ArrowButton", Color(1.2, 1.2, 1.2))
	theme.set_color("icon_pressed_color", "ArrowButton", Color(0.7, 0.7, 0.7))

	# Item niches: the slot's panel draws the niche and the item reaches over it, so the button on top draws nothing.
	# The white selection glow is a panel of its own (SlotGlowPanel) that the slot shows behind the item.
	theme.set_type_variation("SlotButton", "Button")
	for state in ["normal", "hover", "pressed", "hover_pressed", "disabled", "focus"]:
		theme.set_stylebox(state, "SlotButton", StyleBoxEmpty.new())
	theme.set_type_variation("SlotGlowPanel", "Panel")
	theme.set_stylebox("panel", "SlotGlowPanel", glow())

	# Run-menu tabs on the header band: serif words, the open one cream on the kit's pennant, which hangs from the
	# top edge past the band's scallops: the stylebox draws that far below the button.
	theme.set_type_variation("TabButton", "Button")
	var tab := kit("tab_pennant", Vector4(44, 8, 44, 70), Vector4(44, 14, 44, 18))
	tab.expand_margin_bottom = 96
	var closed := StyleBoxEmpty.new()
	closed.content_margin_left = 44
	closed.content_margin_right = 44
	closed.content_margin_top = 14
	closed.content_margin_bottom = 18
	for state in ["normal", "hover", "disabled"]:
		theme.set_stylebox(state, "TabButton", closed)
	for state in ["pressed", "hover_pressed"]:
		theme.set_stylebox(state, "TabButton", tab)
	theme.set_stylebox("focus", "TabButton", StyleBoxEmpty.new())
	theme.set_font("font", "TabButton", display)
	theme.set_font_size("font_size", "TabButton", 44)
	theme.set_color("font_color", "TabButton", Color("b9a27c"))
	theme.set_color("font_focus_color", "TabButton", Color("b9a27c"))
	for state in ["font_hover_color", "font_pressed_color", "font_hover_pressed_color"]:
		theme.set_color(state, "TabButton", CREAM)
	theme.set_color("font_outline_color", "TabButton", INK)
	theme.set_constant("outline_size", "TabButton", 6)

	# A list row (save slots): a recessed card that brightens on hover and glows when focused.
	theme.set_type_variation("CardButton", "Button")
	var row := Vector4(24, 24, 24, 24)
	var padding := Vector4(28, 14, 28, 16)
	theme.set_stylebox("normal", "CardButton", kit("card", row, padding))
	theme.set_stylebox("hover", "CardButton", kit("card", row, padding, Color(1.35, 1.25, 1.1)))
	theme.set_stylebox("pressed", "CardButton", kit("card", row, padding, Color(0.85, 0.8, 0.75)))
	theme.set_stylebox("hover_pressed", "CardButton", kit("card", row, padding, Color(0.85, 0.8, 0.75)))
	theme.set_stylebox("disabled", "CardButton", kit("card", row, padding, Color(0.7, 0.7, 0.7, 0.8)))
	theme.set_stylebox("focus", "CardButton", glow())
	theme.set_font("font", "CardButton", body_strong)
	theme.set_font_size("font_size", "CardButton", 26)
	for state in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color"]:
		theme.set_color(state, "CardButton", CREAM)
	theme.set_color("font_disabled_color", "CardButton", Color(MUTED, 0.5))
	theme.set_color("font_outline_color", "CardButton", INK)
	theme.set_constant("outline_size", "CardButton", 4)


func _plaque(theme: Theme, type: String, texture: String, text: Color, outline: Color, outline_size: int) -> void:
	var slice := Vector4(34, 26, 34, 26)
	var padding := Vector4(44, 12, 44, 16)
	theme.set_stylebox("normal", type, kit(texture + "_normal", slice, padding))
	theme.set_stylebox("hover", type, kit(texture + "_hover", slice, padding))
	# Pressing sinks the plaque: its texture drops 2 px, so the label drops with it.
	var pressed := kit(texture + "_pressed", slice, Vector4(44, 14, 44, 14))
	theme.set_stylebox("pressed", type, pressed)
	theme.set_stylebox("hover_pressed", type, pressed)
	theme.set_stylebox("disabled", type, kit(texture + "_disabled", slice, padding))
	theme.set_stylebox("focus", type, glow())
	theme.set_font("font", type, display_semi)
	theme.set_font_size("font_size", type, 32)
	for state in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color"]:
		theme.set_color(state, type, text)
	theme.set_color("font_disabled_color", type, Color(MUTED, 0.5))
	theme.set_color("font_outline_color", type, outline)
	theme.set_constant("outline_size", type, outline_size)


func _panels(theme: Theme) -> void:
	# PanelContainer is mostly a layout wrapper here (item slots, HUD groups), so the default is a quiet backing
	# without margins; framed windows opt in to FramePanel.
	var backing := StyleBoxFlat.new()
	backing.bg_color = Color(INK, 0.55)
	backing.set_corner_radius_all(6)
	theme.set_stylebox("panel", "PanelContainer", backing)
	theme.set_stylebox("panel", "Panel", backing)

	var frame := Vector4(112, 112, 112, 112)
	var inside := Vector4(104, 96, 104, 104)
	theme.set_type_variation("FramePanel", "PanelContainer")
	theme.set_stylebox("panel", "FramePanel", kit("panel_frame", frame, inside))
	theme.set_type_variation("DangerPanel", "PanelContainer")
	theme.set_stylebox("panel", "DangerPanel", kit("panel_frame", frame, inside, Color(1.25, 0.62, 0.55)))
	# In-game structure: a feathered dark area, and a thin carved outline that groups without walling in.
	theme.set_type_variation("ShadePanel", "PanelContainer")
	theme.set_stylebox("panel", "ShadePanel", kit("shade", Vector4(56, 56, 56, 56), Vector4(56, 56, 56, 56)))
	theme.set_type_variation("OutlinePanel", "PanelContainer")
	theme.set_stylebox("panel", "OutlinePanel", kit("outline_frame", Vector4(64, 64, 64, 64), Vector4(36, 32, 36, 32)))
	theme.set_type_variation("CardPanel", "PanelContainer")
	theme.set_stylebox("panel", "CardPanel", kit("card", Vector4(24, 24, 24, 24), Vector4(26, 18, 26, 20)))
	# Item slots for the HUD and inventory slices: a recessed niche, lit ochre when it holds something.
	theme.set_type_variation("SlotPanel", "PanelContainer")
	theme.set_stylebox("panel", "SlotPanel", kit("slot", Vector4(26, 26, 26, 26), Vector4(8, 8, 8, 8)))
	theme.set_type_variation("FilledSlotPanel", "PanelContainer")
	theme.set_stylebox("panel", "FilledSlotPanel", kit("slot_filled", Vector4(26, 26, 26, 26), Vector4(8, 8, 8, 8)))

	theme.set_stylebox("separator", "HSeparator", kit("divider", Vector4(80, 0, 80, 0), Vector4(0, 20, 0, 20)))
	theme.set_constant("separation", "HSeparator", 40)


func _bars(theme: Theme) -> void:
	var track := StyleBoxFlat.new()
	track.bg_color = Color("18110b")
	track.set_border_width_all(2)
	track.border_color = Color("6b4a22")
	track.set_corner_radius_all(8)
	theme.set_stylebox("background", "ProgressBar", track)
	theme.set_stylebox("fill", "ProgressBar", _bar_fill(Color("c99a42")))
	theme.set_font("font", "ProgressBar", body_strong)
	theme.set_color("font_color", "ProgressBar", CREAM)
	theme.set_color("font_outline_color", "ProgressBar", INK)
	theme.set_constant("outline_size", "ProgressBar", 4)
	# A hairline bar for XP and small cooldowns: no frame, a gold fill on a dark track.
	theme.set_type_variation("ThinBar", "ProgressBar")
	var thin_track := StyleBoxFlat.new()
	thin_track.bg_color = Color("18110b", 0.85)
	thin_track.set_corner_radius_all(3)
	var thin_fill := StyleBoxFlat.new()
	thin_fill.bg_color = GOLD
	thin_fill.set_corner_radius_all(3)
	theme.set_stylebox("background", "ThinBar", thin_track)
	theme.set_stylebox("fill", "ThinBar", thin_fill)
	# A cooldown drawn over its slot: a dark veil that drains away.
	theme.set_type_variation("CooldownBar", "ProgressBar")
	var veil := StyleBoxFlat.new()
	veil.bg_color = Color("120b06", 0.62)
	veil.set_corner_radius_all(8)
	theme.set_stylebox("background", "CooldownBar", StyleBoxEmpty.new())
	theme.set_stylebox("fill", "CooldownBar", veil)
	for pair in [["HealthBar", Color("c8322b")], ["ManaBar", Color("2f6fd6")]]:
		theme.set_type_variation(pair[0], "ProgressBar")
		theme.set_stylebox("fill", pair[0], _bar_fill(pair[1]))


func _bar_fill(color: Color) -> StyleBoxFlat:
	var fill := StyleBoxFlat.new()
	fill.bg_color = color
	fill.set_border_width_all(2)
	fill.border_color = color.lightened(0.35)
	fill.set_corner_radius_all(8)
	return fill


func _scrollbars(theme: Theme) -> void:
	for type in ["VScrollBar", "HScrollBar"]:
		var track := StyleBoxFlat.new()
		track.bg_color = Color("18110b")
		track.set_corner_radius_all(8)
		track.set_content_margin_all(4)
		theme.set_stylebox("scroll", type, track)
		for state in [["grabber", Color("6b4a22")], ["grabber_highlight", Color("a77a35")], ["grabber_pressed", Color("e9c06a")]]:
			var grabber := StyleBoxFlat.new()
			grabber.bg_color = state[1]
			grabber.set_corner_radius_all(8)
			theme.set_stylebox(state[0], type, grabber)


func _popups(theme: Theme) -> void:
	# The card's rivets sit in its corners, so the entries keep clear of them.
	theme.set_stylebox("panel", "PopupMenu", kit("card", Vector4(24, 24, 24, 24), Vector4(30, 26, 30, 26)))
	# The entry under the pointer or the keys: a lit bronze bar with a gold edge and brighter text.
	var hover := StyleBoxFlat.new()
	hover.bg_color = Color("8a5f24")
	hover.set_border_width_all(2)
	hover.border_color = GOLD
	hover.set_corner_radius_all(8)
	hover.expand_margin_left = 6
	hover.expand_margin_right = 6
	theme.set_stylebox("hover", "PopupMenu", hover)
	theme.set_constant("item_start_padding", "PopupMenu", 10)
	theme.set_constant("item_end_padding", "PopupMenu", 10)
	theme.set_font("font", "PopupMenu", body_strong)
	theme.set_font_size("font_size", "PopupMenu", 26)
	theme.set_color("font_color", "PopupMenu", CREAM)
	theme.set_color("font_hover_color", "PopupMenu", Color("fff6dc"))
	theme.set_color("font_disabled_color", "PopupMenu", Color(MUTED, 0.5))
	theme.set_color("font_outline_color", "PopupMenu", INK)
	theme.set_constant("outline_size", "PopupMenu", 3)
	theme.set_constant("v_separation", "PopupMenu", 10)

	theme.set_stylebox("panel", "TooltipPanel", kit("card", Vector4(24, 24, 24, 24), Vector4(18, 12, 18, 14)))
	theme.set_font("font", "TooltipLabel", body)
	theme.set_font_size("font_size", "TooltipLabel", 24)
	theme.set_color("font_color", "TooltipLabel", CREAM)


func _windows(theme: Theme) -> void:
	# Embedded dialogs (overwrite confirmation): the frame also wraps the title bar, so it extends above the content.
	var frame := kit("panel_frame", Vector4(112, 112, 112, 112), Vector4(0, 0, 0, 0))
	frame.expand_margin_left = 64
	frame.expand_margin_right = 64
	frame.expand_margin_bottom = 60
	frame.expand_margin_top = 114
	theme.set_stylebox("embedded_border", "Window", frame)
	theme.set_stylebox("embedded_unfocused_border", "Window", frame)
	theme.set_font("title_font", "Window", display)
	theme.set_font_size("title_font_size", "Window", 34)
	theme.set_color("title_color", "Window", CREAM)
	theme.set_color("title_outline_modulate", "Window", INK)
	theme.set_constant("title_outline_size", "Window", 6)
	theme.set_constant("title_height", "Window", 52)
	var empty := StyleBoxEmpty.new()
	empty.content_margin_left = 8
	empty.content_margin_right = 8
	empty.content_margin_top = 8
	empty.content_margin_bottom = 8
	theme.set_stylebox("panel", "AcceptDialog", empty)
	theme.set_constant("buttons_separation", "AcceptDialog", 18)
