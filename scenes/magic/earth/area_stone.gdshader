shader_type spatial;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform vec4 stone_color : source_color = vec4(0.33, 0.26, 0.19, 1.0);
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	ALBEDO = stone_color.rgb;
	ROUGHNESS = 0.9;
}
