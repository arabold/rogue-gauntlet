shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled;

#include "res://scenes/levels/fog_of_war.gdshaderinc"

uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
	ALBEDO = vec3(0.0);
	ALPHA = depth <= 0.000001 ? 1.0
		: fog_alpha(fog_surface_position(SCREEN_UV, depth, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX).xz);
}
