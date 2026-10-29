shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Expanding toon gust: a crisp wind front trailed by curling swoosh lines, banded through the wind
// ramp. Pixels beyond the front are discarded before any other work.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float progress = 0.0;
uniform float effect_fade = 1.0;
uniform float brightness = 1.2;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float front = mix(0.08, 0.9, 1.0 - pow(1.0 - progress, 2.0));
	if (r > front + 0.08 || r < front - 0.4) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float angle = atan(p.y, p.x);
	vec2 flow = vec2(cos(angle), sin(angle)) * 2.2 + vec2(r * 1.3 + TIME * 0.5, -r * 0.9 + TIME * 0.2);
	float n = texture(noise_texture, flow).r * 0.65 + texture(noise_texture, flow * 1.7 + vec2(-TIME * 0.3, 0.4)).g * 0.35;
	float wave = 1.0 - abs(r - front + (n - 0.5) * 0.06) / 0.04;
	float swoosh = toon_crest(sin(angle * 9.0 + r * 14.0 - TIME * 4.0 + n * 3.0), 0.16)
		* step(r, front - 0.03) * smoothstep(0.35, 0.6, n);
	float fade = (1.0 - smoothstep(0.55, 1.0, progress)) * effect_fade;
	float body = max(wave, swoosh * 0.9) - (1.0 - fade);
	if (body <= 0.0) discard;
	vec3 color = toon_ramp(color_ramp, 0.35 + body * 0.65);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, 0.08, body);
}
