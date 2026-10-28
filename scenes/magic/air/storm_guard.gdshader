shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float effect_fade = 1.0;
uniform float progress = 0.0;
uniform float intensity = 1.0;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 flow = UV * vec2(3.0, 2.0) + vec2(TIME * 0.22, -TIME * 0.3);
	float n = texture(noise_texture, flow).r;
	float bands = sin(UV.y * 24.0 + n * 5.0 + TIME * 5.0 + sin(UV.x * 15.0 - TIME * 3.0));
	float arc = pow(max(0.0, bands), 28.0) * smoothstep(0.28, 0.65, n);
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.0);
	vec3 color = mix(vec3(0.04, 0.18, 0.6), vec3(0.45, 0.85, 1.0), arc);
	ALBEDO = color; EMISSION = color * (1.2 + arc * 1.5);
	ALPHA = clamp((0.025 + rim * 0.12 + arc * 0.52 * intensity) * effect_fade, 0.0, 1.0);
}
