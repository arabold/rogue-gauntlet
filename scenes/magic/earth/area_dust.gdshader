shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform vec4 dust_color : source_color = vec4(0.65, 0.52, 0.38, 0.4);
void vertex() {
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float fade = 1.0 - smoothstep(0.0, 1.0, length((UV - 0.5) * 2.0));
	ALBEDO = dust_color.rgb * COLOR.rgb;
	ALPHA = dust_color.a * COLOR.a * fade;
}
