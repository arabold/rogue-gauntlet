shader_type spatial;
// A flat, single-colored floor marker (attack warnings, lob landing rings) that stays above every floor tile, rug and
// inlay; see ground_overlay.gdshaderinc. Markers with their own look include that file instead.
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

#include "res://scenes/effects/shaders/ground_overlay.gdshaderinc"

uniform vec4 color : source_color = vec4(1.0, 0.25, 0.2, 0.5);

void vertex() {
	POSITION = ground_overlay_position(MODELVIEW_MATRIX, PROJECTION_MATRIX, VERTEX);
}

void fragment() {
	ALBEDO = color.rgb;
	ALPHA = color.a;
}
