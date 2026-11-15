shader_type spatial;
// Stencil mask for the occlusion x-ray. Marks (stencil = 1) every screen pixel where this
// actor's front-most surface is actually visible — i.e. not hidden by a wall. Drawn on top with
// no visible color (additive black); hardware depth testing keeps hidden fragments out of the stencil.
//
// The companion x-ray pass then draws only where stencil != 1, which is exactly where the actor
// is hidden. Because the test is per-pixel (via the shared stencil) rather than per-fragment,
// the actor's own body parts never x-ray through each other — no matter how deep the body is.
// Fog would turn additive black into visible color on every overlapping body-part copy.
render_mode unshaded, cull_disabled, depth_draw_never, blend_add, fog_disabled;
stencil_mode write, compare_always, 1;

// View-space distance, so changing the camera's clip range cannot hide an entire body part from the x-ray.
uniform float occlusion_bias : hint_range(0.0, 0.1, 0.001) = 0.02;

void vertex() {
	vec4 view = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	POSITION = PROJECTION_MATRIX * view;
	// Bias depth only: preserve screen coverage, including perspective and per-sample MSAA edges.
	view.z += occlusion_bias;
	vec4 nearer = PROJECTION_MATRIX * view;
	POSITION.z = nearer.z / nearer.w * POSITION.w;
}

void fragment() {
	ALBEDO = vec3(0.0);
	ALPHA = 1.0;
}
