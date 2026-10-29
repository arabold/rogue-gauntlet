shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/ground_fracture.gdshaderinc"
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec3 outer_color = vec3(0.65, 0.015, 0.0);
uniform vec3 hot_color = vec3(1.0, 0.45, 0.025);
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	vec2 lines = fracture_lines(p, vec2(0.06, 0.16), 0.065);
	float opening = smoothstep(r, r + 0.15, min(progress * 7.0, 1.3));
	float fade = (1.0 - smoothstep(0.55, 1.0, progress)) * (1.0 - smoothstep(0.55, 0.98, r));
	float mask = max(lines.x, lines.y * 0.7) * opening * fade;
	vec3 color = mix(outer_color, hot_color, (1.0 - r) * (1.0 - progress));
	ALBEDO = color; EMISSION = color * 2.5; ALPHA = mask * 0.9;
}
