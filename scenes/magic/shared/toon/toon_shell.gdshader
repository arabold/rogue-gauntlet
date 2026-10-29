shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Toon energy shell for guards and orbs: a thin crisp rim plus thin flowing bands, with the centre
// left clear so whatever is inside stays readable. Band uniforms give each element its own motion;
// intensity widens the bands and fading thins everything away.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float effect_fade = 1.0;
uniform float intensity = 1.0;
uniform float brightness = 1.8;
uniform float rim_width = 0.16;
uniform float band_width = 0.05;
uniform vec2 flow_scale = vec2(3.0, 2.0);
uniform vec2 flow_speed = vec2(0.22, -0.3);
uniform float band_frequency = 24.0;
uniform float band_speed = 5.0;
uniform float band_warp = 5.0;
uniform float swirl_frequency = 15.0;
uniform float swirl_speed = -3.0;
uniform float swirl_amount = 1.0;

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float n = texture(noise_texture, UV * flow_scale + TIME * flow_speed).r;
	float bands = sin(UV.y * band_frequency + n * band_warp + TIME * band_speed + sin(UV.x * swirl_frequency + TIME * swirl_speed) * swirl_amount);
	float arc = toon_crest(bands, band_width * intensity) * smoothstep(0.3, 0.6, n);
	float edge = toon_crest(1.0 - abs(dot(NORMAL, VIEW)), rim_width);
	float body = max(arc, edge) - (1.0 - effect_fade);
	if (body <= 0.0) discard;
	vec3 color = toon_ramp(color_ramp, 0.3 + body * 0.7);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, 0.08, body);
}
