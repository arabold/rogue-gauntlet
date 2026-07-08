shader_type spatial;
// Additive radial floor glow ("pool of light") for loot, rendered on a flat QuadMesh.
// Bright hot center fading to nothing at the quad edge.
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 beam_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float intensity = 2.0;
uniform float falloff : hint_range(1.0, 5.0) = 2.0;

void fragment() {
	// Distance from quad center in UV space: 0 at center, 0.5 at edge midpoints.
	float r = length(UV - vec2(0.5)) * 2.0;
	float a = pow(clamp(1.0 - r, 0.0, 1.0), falloff);
	ALBEDO = beam_color * intensity;
	ALPHA = a;
}
