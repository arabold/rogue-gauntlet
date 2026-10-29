shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Crisp diamond sparks stretched along their velocity. Particles enable align_y, so the instance
// Y axis follows velocity; the card turns around that axis to face the camera.
uniform float brightness = 1.8;
uniform float effect_fade = 1.0;
varying vec3 spark_color;

void vertex() {
	spark_color = toon_srgb_to_linear(COLOR.rgb);
	vec3 axis = MODEL_MATRIX[1].xyz;
	float length_y = length(axis);
	axis = length_y > 0.0001 ? axis / length_y : vec3(0.0, 1.0, 0.0);
	vec3 view_axis = normalize(INV_VIEW_MATRIX[2].xyz);
	vec3 side = cross(axis, view_axis);
	side = length(side) > 0.0001 ? normalize(side) : normalize(INV_VIEW_MATRIX[0].xyz);
	float width = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(side * width, 0.0), vec4(axis * length_y, 0.0),
		vec4(view_axis * width, 0.0), MODEL_MATRIX[3]);
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = abs((UV - 0.5) * 2.0);
	ALBEDO = spark_color;
	EMISSION = spark_color * brightness;
	ALPHA = (1.0 - smoothstep(0.86, 1.0, p.x + p.y)) * COLOR.a * effect_fade;
}
