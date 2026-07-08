shader_type spatial;
// Flat additive emissive used for the loot beam's core orb and rising sparks. Bright enough
// (intensity > 1) that additive blending pushes pixels into the glow threshold to bloom.
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 color : source_color = vec3(1.0, 1.0, 1.0);
uniform float intensity = 2.0;

void fragment() {
	ALBEDO = color * intensity;
	ALPHA = 1.0;
}
