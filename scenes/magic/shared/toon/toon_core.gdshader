shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;
#include "res://scenes/effects/shaders/upright_billboard.gdshaderinc"
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Opaque faceted fire core. Flat facet normals and object-space noise are quantised through
// the banded ramp: facets facing the camera run hot, the silhouette cools to red.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float brightness = 2.3;
uniform float noise_scale = 2.6;
uniform float flow_speed = 2.2;
uniform float pulse = 0.035;
// Shifts the whole core up or down the ramp.
uniform float heat_bias = 0.0;
// Fades by dissolving facets away, so the core stays opaque.
uniform float effect_fade = 1.0;
uniform bool upright_billboard = false;
varying vec3 object_position;

void vertex() {
	if (upright_billboard) {
		MODELVIEW_MATRIX = VIEW_MATRIX * upright_billboard_transform(MODEL_MATRIX, INV_VIEW_MATRIX);
		MODELVIEW_NORMAL_MATRIX = transpose(inverse(mat3(MODELVIEW_MATRIX)));
	}
	object_position = VERTEX;
	VERTEX += NORMAL * sin(TIME * 9.0 + VERTEX.y * 7.0) * pulse;
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	if (effect_fade < 1.0 && toon_noise3(object_position * 6.0) > effect_fade) discard;
	vec3 facet = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	float facing = abs(dot(facet, VIEW));
	float n = toon_fbm3(object_position * noise_scale + vec3(0.0, -TIME * flow_speed, TIME * 0.7));
	vec3 color = toon_ramp(color_ramp, 0.2 + heat_bias + facing * 0.55 + (n - 0.5) * 0.8);
	ALBEDO = color;
	EMISSION = color * brightness;
}
