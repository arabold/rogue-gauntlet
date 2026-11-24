shader_type spatial;
// The gelatinous cube's jelly: translucent slime green like the acid blob, with a brighter rim, drawn after the opaque bones and coins inside so
// they show through, and a slow wobble that ripples up its faces. Instance-local alpha; the Mobile renderer has no
// per-instance transparency.
render_mode blend_mix, depth_draw_never, cull_back, specular_schlick_ggx;
uniform vec4 body_color : source_color = vec4(0.45, 0.78, 0.22, 0.45);
uniform vec4 rim_color : source_color = vec4(0.8, 0.96, 0.36, 0.85);
uniform float rim_power : hint_range(0.5, 8.0) = 2.6;
uniform float wobble_amount : hint_range(0.0, 0.2) = 0.045;
uniform float wobble_speed : hint_range(0.0, 8.0) = 2.2;
uniform float wobble_scale : hint_range(0.0, 8.0) = 2.4;

void vertex() {
	// Rises with height so the oozing foot stays put on the floor.
	float rise = clamp(VERTEX.y / 2.3, 0.0, 1.0);
	float wave = sin(TIME * wobble_speed + VERTEX.y * wobble_scale + VERTEX.x * 1.3)
		+ 0.5 * sin(TIME * wobble_speed * 1.7 + VERTEX.z * wobble_scale);
	VERTEX += NORMAL * wave * wobble_amount * rise;
}

void fragment() {
	float rim = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), rim_power);
	ALBEDO = mix(body_color.rgb, rim_color.rgb, rim);
	ALPHA = mix(body_color.a, rim_color.a, rim);
	ROUGHNESS = 0.15;
	SPECULAR = 0.7;
	EMISSION = rim_color.rgb * rim * 0.12;
}
