shader_type spatial;
// A pendulum's rod fading out toward its pivot, so it hangs from above the walls without a ceiling.
// Fades on the mesh's own height: centre the rod mesh on its origin along Y, pivot end up.
render_mode blend_mix, depth_draw_never, cull_back;

uniform vec4 albedo : source_color = vec4(0.36, 0.34, 0.32, 1.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.3;
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
// Local heights on the rod: opaque below fade_start, gone above fade_end.
uniform float fade_start = -1.3;
uniform float fade_end = 0.3;

varying float local_height;

void vertex() {
	local_height = VERTEX.y;
}

void fragment() {
	ALBEDO = albedo.rgb;
	METALLIC = metallic;
	ROUGHNESS = roughness;
	ALPHA = albedo.a * (1.0 - smoothstep(fade_start, fade_end, local_height));
}
