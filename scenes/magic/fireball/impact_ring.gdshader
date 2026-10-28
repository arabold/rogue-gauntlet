shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float hit_flash = 0.0;
uniform vec4 tint : source_color = vec4(1.0, 0.48, 0.06, 1.0);
varying vec3 local_position;
void vertex() {
	local_position = VERTEX;
	float grow = mix(0.2, 1.0, 1.0 - pow(1.0 - progress, 3.0));
	VERTEX *= grow;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float angle = atan(local_position.z, local_position.x);
	float breaks = smoothstep(-0.55, -0.15, sin(angle * 7.0 + 0.7));
	float fade = (1.0 - smoothstep(0.28, 0.75, progress)) * smoothstep(0.0, 0.06, progress + 0.02);
	ALBEDO = tint.rgb;
	EMISSION = tint.rgb * (1.7 + hit_flash);
	ALPHA = tint.a * breaks * fade * 0.9;
}
