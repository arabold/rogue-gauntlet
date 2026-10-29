shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform float hit_flash = 0.0;
uniform int wisp_count = 4;
void fragment() {
	float helix = UV.x * TAU * 2.0 + UV.y * 13.0 - TIME * 3.5;
	float strand = pow(max(0.0, cos(helix + sin(UV.y * 13.0 - TIME * 2.0) * 0.8)), 18.0);
	float second = pow(max(0.0, cos(helix + 1.6)), 24.0);
	float third = pow(max(0.0, cos(helix + 3.2)), 22.0);
	float fourth = pow(max(0.0, cos(helix + 4.8)), 24.0);
	float fade = smoothstep(0.0, 0.1, UV.y) * (1.0 - smoothstep(0.85, 1.0, UV.y));
	vec3 color = mix(vec3(1.0, 0.12, 0.005), vec3(1.0, 0.7, 0.12), strand);
	float wisps = strand + second * 0.7 + (wisp_count >= 3 ? third * 0.8 : 0.0) + (wisp_count >= 4 ? fourth * 0.7 : 0.0);
	ALBEDO = color; EMISSION = color * (2.0 + hit_flash); ALPHA = clamp(wisps * fade * 0.85, 0.0, 1.0);
}
