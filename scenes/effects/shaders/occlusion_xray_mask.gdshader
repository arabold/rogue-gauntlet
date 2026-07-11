shader_type spatial;
// Stencil mask for the occlusion x-ray. Marks (stencil = 1) every screen pixel where this
// actor's front-most surface is actually visible — i.e. not hidden by a wall. Drawn on top with
// no visible color (additive black); a fragment is discarded where scene geometry is in front of
// it, so only the genuinely visible fragments write the stencil.
//
// The companion x-ray pass then draws only where stencil != 1, which is exactly where the actor
// is hidden. Because the test is per-pixel (via the shared stencil) rather than per-fragment,
// the actor's own body parts never x-ray through each other — no matter how deep the body is.
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, blend_add;
stencil_mode write, compare_always, 1;

// Nearest filtering: mipmapped/linear sampling of depth returns averaged values.
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform float occlusion_bias = 0.001;

void fragment() {
	float scene_depth = texture(depth_texture, SCREEN_UV).x;
	// Reversed-Z: nearer geometry has a LARGER value. If something is already drawn in front of
	// this fragment, it is not the visible surface, so don't mark this pixel as visible.
	if (scene_depth > FRAGCOORD.z + occlusion_bias) {
		discard;
	}
	ALBEDO = vec3(0.0);
	ALPHA = 1.0;
}
