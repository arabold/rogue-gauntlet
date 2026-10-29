shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/ground_fracture.gdshaderinc"

// A transparent surface overlay: crisp fractured seams grow outward as the spike erupts, glowing
// briefly before they settle to dark cracks.
uniform float progress : hint_range(0.0, 1.0) = 0.0;
varying float spread_delay;

void vertex() { spread_delay = INSTANCE_CUSTOM.r; }

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float radius = length(p);
	if (radius > 0.95) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 lines = fracture_lines(p, vec2(0.05, 0.14), 0.06);
	float local_progress = max(0.0, progress - spread_delay * 0.24);
	float opening = smoothstep(radius, radius + 0.18, min(local_progress * 8.0, 1.3));
	float edge = 1.0 - smoothstep(0.55, 0.95, radius);
	float mask = max(lines.x, lines.y * 0.75) * edge * opening - smoothstep(0.72, 1.0, progress) * 0.6;
	float hot = step(0.7, mask) * (1.0 - smoothstep(0.1, 0.45, progress));
	ALBEDO = mix(vec3(0.075, 0.045, 0.028), vec3(1.0, 0.55, 0.16), hot);
	EMISSION = vec3(1.0, 0.45, 0.1) * hot;
	ALPHA = smoothstep(0.4, 0.48, mask);
}
