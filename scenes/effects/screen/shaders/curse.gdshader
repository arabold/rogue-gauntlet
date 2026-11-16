// Curse equip flash: purple aura along the edges of the screen with crackling electric arcs.
// Blends via alpha without reading screen_texture so it overlays both 3D gameplay and paused menus.
shader_type canvas_item;
render_mode unshaded;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float motion : hint_range(0.0, 1.0) = 1.0;
uniform vec3 aura_color : source_color = vec3(0.48, 0.06, 0.68);
uniform vec3 arc_color : source_color = vec3(0.86, 0.34, 1.0);
uniform vec3 core_color : source_color = vec3(1.0, 0.86, 1.0);
uniform float border_width : hint_range(0.05, 0.5) = 0.22;
uniform float crackle_speed : hint_range(1.0, 30.0) = 15.0;

float hash21(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Signed noise in [-1, 1] whose zero crossings form sharp branching lightning ridges.
float signed_fbm(vec2 p) {
	float sum = 0.0;
	float amp = 0.55;
	for (int i = 0; i < 3; i++) {
		sum += (value_noise(p) * 2.0 - 1.0) * amp;
		p = mat2(vec2(1.6, -1.2), vec2(1.2, 1.6)) * p;
		amp *= 0.5;
	}
	return sum;
}

float lightning_ridge(vec2 p) {
	float n = abs(signed_fbm(p));
	return clamp(1.0 - n * 3.8, 0.0, 1.0);
}

void fragment() {
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 edge = min(SCREEN_UV, 1.0 - SCREEN_UV) * vec2(aspect, 1.0);
	float border_dist = min(edge.x, edge.y);

	float reach = border_width * mix(0.65, 1.15, intensity);
	float edge_factor = 1.0 - smoothstep(0.0, max(reach, 0.001), border_dist);

	float tick = floor(TIME * crackle_speed * motion);
	vec2 centered = (SCREEN_UV - 0.5) * vec2(aspect, 1.0);
	vec2 seed_a = vec2(hash21(vec2(tick, 3.1)), hash21(vec2(tick, 7.7))) * 29.0;
	vec2 seed_b = vec2(hash21(vec2(tick + 11.0, 5.3)), hash21(vec2(tick + 11.0, 9.9))) * 31.0;

	float ridge_a = pow(lightning_ridge(centered * 7.5 + seed_a), 3.2);
	float ridge_b = pow(lightning_ridge(centered * 11.0 + seed_b), 3.6);
	float arcs = max(ridge_a, ridge_b * 0.85) * pow(edge_factor, 1.35);

	float pulse = mix(0.8, 1.15, hash21(vec2(tick, 13.7)));
	float vignette = pow(edge_factor, 1.8) * 0.52;
	float core = pow(arcs, 2.6);

	vec3 color = aura_color * vignette + arc_color * arcs * 1.15 * pulse + core_color * core * 0.9;
	float alpha = clamp(vignette + arcs * 0.95 * pulse, 0.0, 1.0) * intensity;
	COLOR = vec4(color, alpha);
}
