shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float effect_fade = 1.0;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float a = atan(p.y, p.x);
	vec2 flow = vec2(a * 2.2 + r * 4.0 - TIME * 3.5, r * 2.0 - TIME * 0.8);
	float n = texture(noise_texture, flow).r;
	float n2 = texture(noise_texture, flow * 1.8 + vec2(TIME * 0.7, 0.3)).g;
	float bands = pow(max(0.0, sin(a * 5.0 + r * 20.0 - TIME * 7.0 + n * 3.0)), 4.0);
	float mist = smoothstep(0.32, 0.76, n * 0.7 + n2 * 0.3);
	float edge = 1.0 - smoothstep(0.75, 1.0, r);
	vec3 color = mix(vec3(0.13, 0.42, 0.58), vec3(0.7, 0.91, 1.0), bands);
	ALBEDO = color; EMISSION = color * 0.7;
	ALPHA = (mist * 0.14 + bands * 0.16) * edge * effect_fade;
}
