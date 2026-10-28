shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/ground_fracture.gdshaderinc"

// A transparent surface overlay: fractured seams grow outward as the spike erupts.
uniform float progress : hint_range(0.0, 1.0) = 0.0;
varying float spread_delay;

void vertex() { spread_delay = INSTANCE_CUSTOM.r; }

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float radius = length(p);
	vec2 lines = fracture_lines(p, vec2(0.05, 0.14), 0.06);
	float local_progress = max(0.0, progress - spread_delay * 0.24);
	float opening = smoothstep(radius, radius + 0.18, min(local_progress * 8.0, 1.3));
	float edge = 1.0 - smoothstep(0.55, 0.95, radius);
	float fade = 1.0 - smoothstep(0.72, 1.0, progress);
	float mask = max(lines.x, lines.y * 0.75) * edge * opening * fade;
	ALBEDO = vec3(0.075, 0.045, 0.028);
	EMISSION = vec3(0.28, 0.11, 0.025) * mask * (1.0 - smoothstep(0.1, 0.6, progress));
	ALPHA = mask * 0.9;
}
