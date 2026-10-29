shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform vec4 tint : source_color = vec4(0.95, 0.55, 0.2, 0.8);
uniform float brightness = 1.2;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	ALBEDO = tint.rgb;
	EMISSION = vec3(1.0, 0.4, 0.08) * brightness;
	ALPHA = tint.a;
}
