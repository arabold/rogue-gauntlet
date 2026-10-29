// Shared lightning look for the additive air shaders: a white-hot core blending into a blue glow, with no
// dark toon outline. Callers compute their core and glow strengths and colour them here.
uniform vec4 core_color : source_color = vec4(0.95, 0.99, 1.0, 1.0);
uniform vec4 glow_color : source_color = vec4(0.25, 0.6, 1.0, 1.0);

vec3 lightning_color(float core, float glow) {
	return mix(glow_color.rgb * glow, core_color.rgb, core);
}

float lightning_alpha(float core, float glow) {
	return clamp(max(core, glow), 0.0, 1.0);
}
