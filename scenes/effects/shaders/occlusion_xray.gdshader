shader_type spatial;
// See-through-wall silhouette for an actor (monster/loot). Drawn on top of everything, but the
// stencil written by the companion mask (occlusion_xray_mask.gdshader) makes it appear ONLY where
// the actor is hidden (stencil != 1). So an actor in plain view shows nothing, and — because the
// gate is per-pixel — its own body parts never x-ray through each other. Alpha fades with distance
// so far actors stay quiet.
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled;
stencil_mode read, compare_not_equal, 1;

uniform vec4 xray_color : source_color = vec4(1.0, 0.35, 0.35, 0.85);
uniform float fade_near = 16.0;
uniform float fade_far = 34.0;

void fragment() {
	float fade = 1.0 - smoothstep(fade_near, fade_far, length(VERTEX));
	if (fade <= 0.0) {
		discard;
	}
	ALBEDO = xray_color.rgb;
	ALPHA = xray_color.a * fade;
}
