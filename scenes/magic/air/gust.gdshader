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
	float angle = atan(p.y, p.x);
	float front = mix(0.08, 0.9, 1.0 - pow(1.0 - progress, 2.0));
	vec2 flow = vec2(cos(angle), sin(angle)) * 2.2 + vec2(r * 1.3 + TIME * 0.5, -r * 0.9 + TIME * 0.2);
	float n = texture(noise_texture, flow).r;
	float detail = texture(noise_texture, flow * 1.7 + vec2(-TIME * 0.3, 0.4)).g;
	float wave = exp(-pow((r - front + (n - 0.5) * 0.075) / 0.075, 2.0));
	float wake = exp(-pow((r - front + 0.17 + (n - 0.5) * 0.12) / 0.12, 2.0));
	float streaks = smoothstep(0.32, 0.72, n * 0.65 + detail * 0.35);
	vec3 color = mix(vec3(0.12, 0.48, 0.68), vec3(0.82, 0.97, 1.0), wave * 0.8);
	ALBEDO = color; EMISSION = color * 1.2;
	ALPHA = clamp((wave * 0.55 + wake * streaks * 0.3) * effect_fade, 0.0, 1.0);
}
