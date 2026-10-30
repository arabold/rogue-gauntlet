shader_type spatial;
// Draw on top of everything (depth test off), but only keep fragments that are
// hidden: occluded by scene geometry (a wall or the black fog cap), or in plain
// view on a surface that is still unexplored. The silhouette therefore appears
// only while the door cannot be seen, and stays invisible when it is in plain
// explored view, even under the fog's soft fringe at the explored edge.
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
	// Occluded when something already drawn is in front of this fragment. Otherwise
	// the door is the frontmost surface here, hidden while that surface is unexplored.
	// Test the surface the depth buffer holds rather than this fragment: with culling
	// off, the leaf's far face of a closed door lies past its threshold in unexplored
	// space, and the bias absorbs the leaf's thickness.
	float hidden = scene_depth > FRAGCOORD.z + occlusion_bias
		? 1.0
		: fog_unexplored(fog_surface_position(SCREEN_UV, scene_depth, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX).xz);

	float fade = 1.0 - smoothstep(fade_near, fade_far, length(VERTEX));
	if (hidden * fade <= 0.0) {
		discard;
	}

	ALBEDO = xray_color.rgb;
	ALPHA = xray_color.a * fade * hidden;
}
