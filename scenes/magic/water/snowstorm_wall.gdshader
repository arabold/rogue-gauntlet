shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float effect_fade = 1.0;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float y = UV.y;
	vec2 flow = vec2(UV.x * 4.0 - TIME * 1.4, y * 1.3 + TIME * 0.15);
	float n = texture(noise_texture, flow).r;
	float fine = texture(noise_texture, flow * 2.2 + vec2(TIME * 0.4, -TIME * 0.3)).g;
	float streak = pow(max(0.0, sin(UV.x * 50.0 - y * 12.0 - TIME * 12.0 + n * 4.0)), 9.0);
	float height = smoothstep(0.0, 0.18, y) * (1.0 - smoothstep(0.72, 1.0, y));
	vec3 color = mix(vec3(0.18, 0.45, 0.65), vec3(0.8, 0.95, 1.0), streak);
	ALBEDO = color; EMISSION = color * 0.65;
	ALPHA = (streak * 0.11 + smoothstep(0.4, 0.8, n * 0.7 + fine * 0.3) * 0.045) * height * effect_fade;
}
