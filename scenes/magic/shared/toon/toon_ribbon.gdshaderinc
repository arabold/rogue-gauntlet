// Shared crisp ribbon body for toon_ribbon (blended) and toon_ribbon_glow (additive). UV.x runs from
// the travelling head to the fading tail: a noise-broken mask is cut into hard-edged tongues, so
// fading erodes the ribbon, and heat is banded through the element's colour ramp.
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float brightness = 1.3;
// Shifts the ribbon up the ramp; hot inner ribbons use a positive bias.
uniform float heat_bias = 0.0;
uniform float opacity = 1.0;
uniform float effect_fade = 1.0;
uniform float animation_speed = 1.0;
uniform bool closed_loop = false;
// Mask level the crisp cut keeps; lower it for faint, narrow ribbons.
uniform float crisp_threshold = 0.3;

void vertex() {
	float t = TIME * animation_speed;
	if (closed_loop) {
		float angle = UV.x * 6.2831853;
		float flicker = 0.5 + 0.5 * sin(angle * 13.0 - t * 8.0 + sin(angle * 7.0 + t * 3.0) * 1.4);
		VERTEX.y += max(VERTEX.y, 0.0) * flicker * 1.5;
	}
}

void fragment() {
	float t = TIME * animation_speed;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float along = UV.x;
	float across = abs(UV.y * 2.0 - 1.0);
	float fade = closed_loop ? 1.0 : pow(1.0 - along, 1.25) * smoothstep(0.0, 0.06, along);
	vec2 flow = vec2(along * 2.8 + t * 1.8, UV.y * 1.5);
	vec2 ring_flow = vec2(cos(along * 6.2831853), sin(along * 6.2831853)) * 2.4 + vec2(t * 0.25, -t * 0.4) + UV.y;
	float n = texture(noise_texture, closed_loop ? ring_flow : flow).r;
	float detail = texture(noise_texture, closed_loop ? ring_flow * 1.9 : flow * 1.9 + vec2(-t * 0.8, 0.37)).g;
	float tongues = smoothstep(closed_loop ? 0.28 : 0.18, 0.72, n * 0.7 + detail * 0.3 + (closed_loop ? 0.0 : (1.0 - across) * 0.25));
	float edge = 1.0 - smoothstep(0.28 + n * 0.12, 0.86, across);
	float mask = fade * edge * tongues * effect_fade;
	if (mask <= crisp_threshold) discard;
	float heat = pow(1.0 - across, 2.4) * smoothstep(0.2, 0.75, n + 0.25);
	float filament = pow(max(0.0, sin(UV.y * 30.0 + n * 2.0 - along * 9.0 + t * 7.0)), 12.0);
	vec3 color = toon_ramp(color_ramp, 0.2 + heat * 0.6 + filament * 0.15 + heat_bias);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(crisp_threshold, crisp_threshold + 0.06, mask) * opacity;
}
