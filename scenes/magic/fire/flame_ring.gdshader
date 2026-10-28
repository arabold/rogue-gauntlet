shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float progress : hint_range(0.0, 1.0) = 0.0;
void vertex() {
	float expansion = mix(0.12, 1.0, 1.0 - pow(1.0 - progress, 2.2));
	VERTEX.xz *= expansion;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float n = sin(UV.x * 71.0 + TIME * 7.0 + sin(UV.x * 23.0 - TIME * 3.0) * 2.0);
	float tongues = smoothstep(0.15, 0.85, 0.55 + 0.45 * n - UV.y * 0.45);
	float vertical = smoothstep(0.0, 0.08, UV.y) * (1.0 - smoothstep(0.38, 0.95, UV.y + n * 0.13));
	float fade = smoothstep(0.0, 0.08, progress + 0.02) * (1.0 - smoothstep(0.5, 1.0, progress));
	vec3 color = mix(vec3(1.0, 0.08, 0.006), vec3(1.0, 0.7, 0.12), (1.0 - UV.y) * 0.7);
	ALBEDO = color; EMISSION = color * 2.0; ALPHA = tongues * vertical * fade * 0.8;
}
