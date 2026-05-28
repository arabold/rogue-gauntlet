shader_type spatial;
// Draw on top of everything (depth test off), but only keep fragments that are
// actually occluded by scene geometry (a wall or the black fog cap). This makes
// the door's silhouette appear only when it is hidden, and stay invisible when
// the door is in plain view. Alpha fades with distance so far doors stay quiet.
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled;

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
	// This fragment is occluded only when something already drawn is in front of it.
	if (scene_depth <= FRAGCOORD.z + occlusion_bias) {
		discard;
	}

	float fade = 1.0 - smoothstep(fade_near, fade_far, length(VERTEX));
	if (fade <= 0.0) {
		discard;
	}

	ALBEDO = xray_color.rgb;
	ALPHA = xray_color.a * fade;
}
