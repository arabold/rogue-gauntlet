shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float progress = 0.0;
uniform float effect_fade = 1.0;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float n = texture(noise_texture, p * 2.5 + vec2(TIME * 0.05, -TIME * 0.08)).r;
	float front = mix(0.08, 0.92, 1.0 - pow(1.0 - progress, 2.5));
	float ring = exp(-pow((r - front + (n - 0.5) * 0.05) / 0.07, 2.0));
	float frost = smoothstep(0.3, 0.7, n) * (1.0 - smoothstep(front - 0.1, front + 0.04, r));
	vec3 color = mix(vec3(0.12, 0.5, 0.85), vec3(0.85, 1.0, 1.0), ring);
	ALBEDO = color; EMISSION = color * 1.1;
	ALPHA = (ring * 0.7 + frost * 0.16) * effect_fade;
}
