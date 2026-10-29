shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float effect_fade = 1.0;
uniform float progress = 0.0;
uniform float brightness = 1.2;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float t = UV.x, across = abs(UV.y * 2.0 - 1.0);
	float n = texture(noise_texture, vec2(t * 3.0 + TIME * 0.8, UV.y * 1.7 - TIME * 0.4)).r;
	float edge = 1.0 - smoothstep(0.35 + n * 0.1, 0.95, across);
	float fade = pow(1.0 - t, 1.1) * smoothstep(0.0, 0.05, t);
	float streak = pow(max(0.0, sin(UV.y * 40.0 + n * 2.0 + t * 8.0 - TIME * 4.0)), 16.0);
	vec3 color = mix(vec3(0.08, 0.4, 0.65), vec3(0.75, 0.95, 1.0), streak * 0.65 + (1.0 - across) * 0.25);
	ALBEDO = color; EMISSION = color * brightness;
	ALPHA = fade * edge * (0.35 + n * 0.45 + streak * 0.2) * effect_fade;
}
