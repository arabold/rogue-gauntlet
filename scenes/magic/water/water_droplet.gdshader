shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float effect_fade = 1.0;
varying float particle_age;
void vertex() {
	particle_age = INSTANCE_CUSTOM.y;
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float d = length((UV - 0.5) * 2.0);
	float shape = 1.0 - smoothstep(0.35, 1.0, d);
	float life = smoothstep(0.0, 0.1, particle_age) * (1.0 - smoothstep(0.7, 1.0, particle_age));
	ALBEDO = vec3(0.35, 0.88, 1.0); EMISSION = vec3(0.2, 0.55, 0.7);
	ALPHA = shape * life * COLOR.a * effect_fade;
}
