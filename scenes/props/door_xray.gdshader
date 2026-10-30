shader_type spatial;
// Draw on top of everything (depth test off), but only keep fragments that are
// hidden: occluded by scene geometry (a wall or the black fog cap), or in plain
// view yet still under the fog. The silhouette therefore appears only while the
// door cannot be seen, and stays invisible when it is in plain explored view.
// Alpha fades with distance so far doors stay quiet.
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled;

#include "res://scenes/levels/fog_of_war.gdshaderinc"

// Nearest filtering: mipmapped/linear sampling of depth returns averaged values
// and breaks the comparison.
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec4 xray_color : source_color = vec4(1.0, 0.72, 0.3, 0.85);
uniform float fade_near = 16.0;
uniform float fade_far = 34.0;
// Absorbs the self-equality with the door's own opaque mesh when it is visible.
uniform float occlusion_bias = 0.0005;

void fragment() {
	float scene_depth = texture(depth_texture, SCREEN_UV).x;

	// Godot uses a reversed-Z depth buffer, so nearer geometry has a LARGER value.
	// Occluded when something already drawn is in front of this fragment; otherwise
	// this is the visible surface, hidden exactly as much as the fog covers it.
	float hidden = scene_depth > FRAGCOORD.z + occlusion_bias
		? 1.0
		: fog_alpha((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz);

	float fade = 1.0 - smoothstep(fade_near, fade_far, length(VERTEX));
	if (hidden * fade <= 0.0) {
		discard;
	}

	ALBEDO = xray_color.rgb;
	ALPHA = xray_color.a * fade * hidden;
}
