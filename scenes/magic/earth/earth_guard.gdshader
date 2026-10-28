shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded;

uniform vec3 glow_color : source_color = vec3(0.8, 0.45, 0.16);
uniform float flash = 0.0;

void fragment() {
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.0);
	float fissures = pow(abs(sin(VERTEX.y * 9.0 + VERTEX.x * 5.0) * sin(VERTEX.z * 8.0 - VERTEX.y * 4.0)), 18.0);
	float pulse = 0.8 + 0.2 * sin(TIME * 2.5);
	ALBEDO = glow_color * (1.2 + flash * 2.0);
	EMISSION = glow_color * (rim * 1.5 + fissures * 0.5 + flash);
	ALPHA = clamp((rim * 0.4 + fissures * 0.15 + flash * 0.3) * pulse, 0.0, 0.7);
}
