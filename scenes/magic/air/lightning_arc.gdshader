shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float effect_fade = 1.0;
uniform float progress = 0.0;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float across = abs(UV.y * 2.0 - 1.0);
	float core = 1.0 - smoothstep(0.035, 0.15, across);
	float glow = pow(1.0 - across, 2.3);
	vec3 color = mix(vec3(0.05, 0.34, 1.0), vec3(0.85, 0.98, 1.0), core);
	ALBEDO = color;
	EMISSION = color * (1.8 + core * 1.5);
	ALPHA = clamp((core * 0.9 + glow * 0.55) * effect_fade, 0.0, 1.0);
}
