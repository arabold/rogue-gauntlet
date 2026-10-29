shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/fireball/toon_fire.gdshaderinc"

// Faceted, broken shock ring: solid segments expand, cool through the banded ramp and erode
// shorter until they vanish.
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float brightness = 1.8;
uniform float segments = 7.0;
uniform float phase = 0.7;
varying vec3 local_position;

void vertex() {
	local_position = VERTEX;
	VERTEX *= mix(0.2, 1.0, 1.0 - pow(1.0 - progress, 3.0));
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float angle = atan(local_position.z, local_position.x);
	float keep = sin(angle * segments + phase) + 0.75 - progress * 1.9;
	vec3 color = toon_ramp(color_ramp, 0.9 - progress * 1.1);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, 0.06, keep);
}
