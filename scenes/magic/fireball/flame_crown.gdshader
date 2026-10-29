shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/fireball/toon_fire.gdshaderinc"

// Low-poly flame tongues licking back from the core. Local +Z points away from the flight
// direction: back-hemisphere vertices stretch into a flickering, tapering tail and the alpha
// erodes into crisp tongues, while the front stays open so the core shows through.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float brightness = 1.7;
uniform float radius = 0.36;
uniform float stretch = 1.5;
uniform float taper = 0.6;
uniform float flicker_speed = 7.0;
uniform float erosion = 0.85;
uniform float edge_softness = 0.05;
uniform float effect_fade = 1.0;
varying vec3 object_position;
varying float tail;

void vertex() {
	object_position = VERTEX;
	tail = clamp(VERTEX.z / radius, 0.0, 1.0);
	float flicker = toon_noise3(vec3(VERTEX.xy / radius * 1.8, TIME * flicker_speed - tail * 2.0));
	VERTEX.z += tail * tail * radius * stretch * (0.5 + flicker);
	VERTEX.xy *= 1.0 - tail * tail * taper;
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec3 p = object_position / radius;
	float n = toon_fbm3(p * 2.2 + vec3(0.0, 0.0, -TIME * 3.5));
	float back = smoothstep(0.0, 0.35, p.z + (n - 0.5) * 0.5);
	float body = n * back + 0.3 - tail * erosion - (1.0 - effect_fade);
	ALPHA = smoothstep(0.0, edge_softness, body - 0.25);
	vec3 color = toon_ramp(color_ramp, 0.78 - tail * 0.7 + (n - 0.5) * 0.6);
	ALBEDO = color;
	EMISSION = color * brightness;
}
