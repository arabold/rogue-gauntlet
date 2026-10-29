shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/ground_fracture.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Molten floor fractures for meteor landings: crisp lines open outward from the impact, glow hot,
// then cool through the banded ramp into dark scars before eroding away.
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float effect_fade = 1.0;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform vec4 scar_color : source_color = vec4(0.09, 0.06, 0.05, 1.0);
uniform float brightness = 1.8;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	if (r > 1.0) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 lines = fracture_lines(p, vec2(0.06, 0.16), 0.065);
	float opening = smoothstep(r, r + 0.15, min(progress * 7.0, 1.3));
	float line = max(lines.x, lines.y * 0.7) * opening * (1.0 - smoothstep(0.55, 0.98, r));
	float heat = (1.0 - r) * 0.9 + 0.35 - progress * 1.3;
	vec3 color = heat > 0.05 ? toon_ramp(color_ramp, heat) : scar_color.rgb;
	ALBEDO = color;
	EMISSION = heat > 0.05 ? color * brightness : vec3(0.0);
	float erode = (1.0 - smoothstep(0.65, 1.0, progress)) * effect_fade;
	ALPHA = smoothstep(0.42, 0.5, line - (1.0 - erode) * 0.6);
}
