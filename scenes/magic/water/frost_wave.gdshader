shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Toon ice sheet: a frosted rim sweeps outward and leaves glossy pale ice behind it, split into plates by
// straight white cracks and caught by a few short double glints, the cartoon read for ice. Fading melts the
// sheet into holes. Pixels beyond the rim are discarded before any other work.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float progress = 0.0;
uniform float effect_fade = 1.0;
uniform float brightness = 0.3;
uniform float crack_scale = 4.0;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float n = toon_fbm3(vec3(p * 3.0, 0.0));
	float edge = mix(0.08, 0.92, 1.0 - pow(1.0 - progress, 2.5)) + (n - 0.5) * 0.1;
	if (r > edge) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	if (toon_fbm3(vec3(p * 6.0, 4.0)) > effect_fade * 1.15 - 0.1) discard;
	float crack = toon_cell_border(p * crack_scale + (n - 0.5) * 0.3, 0.0);
	// World-X strokes read as diagonals from the game camera; a thick and a thin one make the glint.
	float stroke = fract(dot(p, vec2(0.25, 1.0)) * 2.5 + n * 0.5);
	float glint = step(toon_fbm3(vec3(p * 2.0, 8.0)), 0.36) * (step(stroke, 0.05) + step(abs(stroke - 0.1), 0.012));
	vec3 color = toon_ramp(color_ramp, n > 0.5 ? 0.72 : 0.55);
	float alpha = 0.5;
	if (crack < 0.04) { color = toon_ramp(color_ramp, 0.95); alpha = 0.85; }
	if (glint > 0.0) { color = vec3(1.0); alpha = 0.9; }
	if (edge - r < 0.035) { color = toon_ramp(color_ramp, 0.95); alpha = 0.95; }
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = alpha;
}
