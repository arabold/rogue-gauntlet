shader_type spatial;
render_mode blend_add, cull_back, unshaded;

uniform vec4 core_color : source_color = vec4(1.0, 0.82, 0.22, 1.0);
uniform vec4 flame_color : source_color = vec4(1.0, 0.18, 0.02, 1.0);
uniform sampler2D noise_texture;
uniform float emission_intensity = 5.0;
uniform float pulse_speed = 8.0;
uniform float noise_scale = 2.4;

void vertex() {
	float pulse = sin(TIME * pulse_speed + VERTEX.y * 8.0) * 0.06;
	VERTEX += NORMAL * pulse;
}

void fragment() {
	vec2 uv = UV * noise_scale + vec2(TIME * 0.35, -TIME * 0.65);
	float noise = texture(noise_texture, uv).r;
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
	vec3 color = mix(flame_color.rgb, core_color.rgb, noise);
	ALBEDO = color;
	EMISSION = color * (emission_intensity + fresnel * emission_intensity * 1.4);
	ALPHA = clamp(0.55 + noise * 0.45 + fresnel * 0.35, 0.0, 1.0);
}
