shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Crisp toon ice wake: a hard-edged ribbon that breaks apart toward its tail, with bright frost
// streaks along its length.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float effect_fade = 1.0;
uniform float brightness = 1.2;

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float t = UV.x, across = abs(UV.y * 2.0 - 1.0);
	float n = texture(noise_texture, vec2(t * 3.0 + TIME * 0.8, UV.y * 1.7 - TIME * 0.4)).r;
	float edge = 1.0 - smoothstep(0.35 + n * 0.1, 0.95, across);
	float fade = pow(1.0 - t, 1.1) * smoothstep(0.0, 0.05, t);
	float streak = pow(max(0.0, sin(UV.y * 40.0 + n * 2.0 + t * 8.0 - TIME * 4.0)), 16.0);
	float mask = fade * edge * (0.35 + n * 0.45 + streak * 0.2) * effect_fade;
	if (mask < 0.18) discard;
	vec3 color = toon_ramp(color_ramp, 0.3 + (1.0 - across) * 0.4 + streak * 0.3);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.18, 0.24, mask);
}
