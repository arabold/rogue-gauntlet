shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"

uniform float progress : hint_range(0.0, 1.0) = 0.0;
// Fraction of the gameplay radius the wave has reached, fed by its timeline's WaveFront so hits follow the drawn front.
uniform float wave_front = 0.0;
// Plane distance (0 centre, 1 border) at which the gameplay radius lies.
uniform float wave_edge = 1.0;
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x), mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float turbulence = noise2(UV * 12.0 + vec2(progress * 1.4, -progress));
	float front = wave_front * wave_edge;
	float band = exp(-pow((r - front + (turbulence - 0.5) * 0.14) / 0.09, 2.0));
	float fracture = pow(abs(sin(atan(p.y, p.x) * 11.0 + turbulence * 3.0)), 16.0);
	float fissures = fracture * (1.0 - smoothstep(front - 0.12, front + 0.04, r)) * smoothstep(0.14, 0.5, r);
	float fade = 1.0 - smoothstep(0.35, 0.95, progress);
	vec3 color = mix(vec3(0.35, 0.12, 0.025), vec3(1.0, 0.68, 0.26), clamp(band + fissures * 0.7, 0.0, 1.0));
	ALBEDO = color;
	EMISSION = color * 1.6;
	ALPHA = clamp((band * (0.35 + turbulence * 0.35) + fissures * 0.3) * fade, 0.0, 0.8);
}
