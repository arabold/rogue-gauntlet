shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// A nearly solid hot flash blooms into a ball of fire that erodes into crisp tongues while it
// cools through the banded ramp. The ground-wave variant is a broken ring of flame on the floor.
// Both cover a lot of screen, so they sample a noise texture instead of per-pixel hash noise.
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform bool ground_wave = false;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float brightness = 1.8;
uniform float edge_softness = 0.05;
// Off when a motion component already scales the mesh with the effect.
uniform bool self_grow = true;
varying vec3 object_position;

void vertex() {
	object_position = VERTEX;
	if (self_grow) VERTEX *= mix(0.22, 1.0, 1.0 - pow(1.0 - progress, 3.0));
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	if (ground_wave && (r < 0.38 || r > 1.0)) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float body;
	float heat;
	if (ground_wave) {
		float n = texture(noise_texture, p * 1.3 + vec2(progress * 0.4, -progress * 0.3)).r * 0.7
			+ texture(noise_texture, p * 2.7 - vec2(progress * 0.6, 0.0)).g * 0.3;
		float ring = 1.0 - abs(r + (n - 0.5) * 0.3 - 0.7) / 0.14;
		body = ring + (n - 0.5) * 0.6 - progress * 0.9;
		heat = 0.85 - progress * 0.9 + (n - 0.5) * 0.4;
	} else {
		vec3 d = normalize(object_position);
		float n = texture(noise_texture, d.xz * 0.9 + vec2(0.0, -progress * 0.8)).r * 0.7
			+ texture(noise_texture, d.xy * 1.3 + vec2(progress * 0.4, 0.0)).g * 0.3;
		float rim = 1.0 - abs(dot(NORMAL, VIEW));
		body = n + 0.7 - progress * 1.4 - rim * 0.5;
		heat = 0.95 - progress * 1.5 + (n - 0.5) * 0.5 - rim * 0.3;
	}
	vec3 color = toon_ramp(color_ramp, heat);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.5, 0.5 + edge_softness, body);
}
