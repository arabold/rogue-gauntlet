shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float effect_fade = 1.0;
uniform vec4 ice_color : source_color = vec4(0.38, 0.78, 0.95, 1.0);
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.0);
	float facet = 0.5 + 0.5 * abs(normalize(NORMAL).y);
	ALBEDO = mix(ice_color.rgb * 0.5, vec3(0.85, 0.98, 1.0), facet * 0.45 + rim * 0.2);
	ROUGHNESS = 0.18; SPECULAR = 0.8; METALLIC = 0.05;
	EMISSION = vec3(0.08, 0.24, 0.3) * (0.3 + rim * 0.7);
	ALPHA = (0.68 + rim * 0.22) * effect_fade;
}
