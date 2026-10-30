shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Toon ice sheet: a frosted rim sweeps outward and leaves glossy pale ice behind it, split into plates by
// straight white cracks and caught by a few short double glints, the cartoon read for ice. Fading melts the
// sheet into holes. Pixels beyond the rim's widest wobble are discarded before any noise is sampled.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
// Fraction of the gameplay radius the wave has reached, fed by its timeline's WaveFront so hits follow the drawn front.
uniform float wave_front = 0.0;
// Plane distance (0 centre, 1 border) at which the gameplay radius lies.
uniform float wave_edge = 0.875;
uniform float effect_fade = 1.0;
uniform float brightness = 0.3;
uniform float crack_scale = 4.0;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float front = wave_front * wave_edge;
	if (r > front + 0.05) discard;
	float n = toon_fbm3(vec3(p * 3.0, 0.0));
	float edge = front + (n - 0.5) * 0.1;
	if (r > edge) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	// fbm stays below 1, so the melt can only discard once the fade has begun.
	if (effect_fade < 0.96 && toon_fbm3(vec3(p * 6.0, 4.0)) > effect_fade * 1.15 - 0.1) discard;
	float crack = toon_cell_border(p * crack_scale + (n - 0.5) * 0.3);
	// World-X strokes read as diagonals from the game camera; a thick and a thin one make the glint, shown
	// only in sparse patches whose noise is sampled just inside the strokes.
	float stroke = fract(dot(p, vec2(0.25, 1.0)) * 2.5 + n * 0.5);
	bool glint = false;
	if (step(stroke, 0.05) + step(abs(stroke - 0.1), 0.012) > 0.0) glint = toon_fbm3(vec3(p * 2.0, 8.0)) < 0.36;
	vec3 color = toon_ramp(color_ramp, n > 0.5 ? 0.72 : 0.55);
	float alpha = 0.5;
	if (crack < 0.04) { color = toon_ramp(color_ramp, 0.95); alpha = 0.85; }
	if (glint) { color = vec3(1.0); alpha = 0.9; }
	if (edge - r < 0.035) { color = toon_ramp(color_ramp, 0.95); alpha = 0.95; }
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = alpha;
}
