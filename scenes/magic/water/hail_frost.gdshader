shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Frosted floor under a hailstorm: sparse hard-edged frost patches creep in from the storm's edge, then hold
// still, so the area reads without anything moving under the action. A broken frost line traces the boundary.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float progress = 0.0;
uniform float effect_fade = 1.0;
uniform float brightness = 0.8;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	if (r > 1.0 || r < 1.0 - smoothstep(0.0, 0.2, progress)) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	// Static value noise, so the frost forms irregular patches that never crawl.
	float n = toon_fbm3(vec3(p * 3.2, 0.0));
	float frost = (n - 0.68) / 0.06 * 0.6;
	float edge = (1.0 - abs(r - 0.96 + (n - 0.5) * 0.06) / 0.022) * step(0.45, toon_noise3(vec3(p * 9.0, 1.0)));
	float body = max(frost, edge) - (1.0 - effect_fade);
	if (body <= 0.0) discard;
	vec3 color = toon_ramp(color_ramp, 0.72 + body * 0.25);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, 0.08, body) * 0.6;
}
