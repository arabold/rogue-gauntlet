shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float effect_fade = 1.0;
uniform float progress = 0.0;
void vertex() {
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float glow = pow(max(0.0, 1.0 - r), 2.5);
	float cross = pow(max(0.0, 1.0 - abs(p.x) * 6.0), 4.0) * max(0.0, 1.0 - abs(p.y));
	cross += pow(max(0.0, 1.0 - abs(p.y) * 6.0), 4.0) * max(0.0, 1.0 - abs(p.x));
	vec3 color = mix(vec3(0.08, 0.35, 1.0), vec3(0.8, 0.95, 1.0), clamp(cross, 0.0, 1.0));
	ALBEDO = color; EMISSION = color * 2.0;
	ALPHA = clamp((glow * 0.5 + cross * 0.35) * effect_fade, 0.0, 1.0);
}
