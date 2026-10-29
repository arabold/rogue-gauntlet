shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Opaque toon ice: flat facets banded through the ice ramp, brighter toward the top and the silhouette, and
// flashing to a white sheen where a facet catches a fixed key light, so the ice reads glossy rather than flat
// blue. Fading dissolves facets away so the crystals stay solid.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float effect_fade = 1.0;
uniform float brightness = 0.6;
varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	if (effect_fade < 1.0 && toon_noise3(world_position * 5.0) > effect_fade) discard;
	vec3 facet = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	if (dot(facet, VIEW) < 0.0) facet = -facet;
	vec3 up = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	vec3 key = normalize((VIEW_MATRIX * vec4(0.35, 0.85, 0.4, 0.0)).xyz);
	float rim = 1.0 - abs(dot(facet, VIEW));
	float sheen = step(0.8, dot(facet, normalize(key + VIEW)));
	vec3 color = toon_ramp(color_ramp, 0.38 + abs(dot(facet, up)) * 0.35 + rim * 0.25 + sheen);
	ALBEDO = color;
	EMISSION = color * brightness;
}
