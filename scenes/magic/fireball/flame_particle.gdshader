shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D texture_mask : filter_linear;
uniform float brightness = 1.25;
uniform float effect_fade = 1.0;
varying float particle_age;
void vertex() {
	particle_age = INSTANCE_CUSTOM.y;
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	billboard *= mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0),
		vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
		mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float n = noise2(UV * 3.0 + vec2(TIME * 0.3, -TIME * 1.5));
	float fine = noise2(UV * 6.0 + vec2(-TIME * 0.7, TIME * 0.4));
	float radius = length(p * vec2(1.0, 0.8));
	float edge = 1.0 - smoothstep(0.45, 1.0, radius + (n - 0.5) * 0.5);
	float heat = smoothstep(0.2, 0.8, (1.0 - radius) * 0.75 + n * 0.3 + fine * 0.15);
	vec3 color = mix(vec3(1.0, 0.045, 0.002), vec3(1.0, 0.48, 0.025), heat);
	color = mix(color, COLOR.rgb, heat * 0.55);
	ALBEDO = color;
	EMISSION = color * brightness;
	float life_fade = smoothstep(0.0, 0.12, particle_age) * (1.0 - smoothstep(0.65, 1.0, particle_age));
	ALPHA = edge * COLOR.a * 0.42 * life_fade * effect_fade;
}
