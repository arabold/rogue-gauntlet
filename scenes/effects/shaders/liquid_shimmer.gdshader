shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

// Additive light play on a glowing pool: drifting caustic veins, slow rings spreading from
// the center and a soft edge that keeps it inside the basin. Drawn on a unit-radius disc
// laid just above the liquid surface; the pool's own emissive color shows through.
instance uniform vec4 tint : source_color = vec4(1.0);
uniform float brightness = 0.45;
uniform float speed = 0.3;
uniform float vein_scale = 2.4;
uniform float rings = 2.5;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0)), u.x), u.y);
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float t = TIME * speed;
	// Two noise layers drifting against each other brighten where they cross.
	float a = noise(p * vein_scale + vec2(t, -0.7 * t));
	float b = noise(p * vein_scale * 1.15 - vec2(0.8 * t, 0.5 * t) + 7.3);
	float veins = pow(1.0 - abs(a - b), 6.0);
	float ripple = pow(max(0.0, sin((r * rings - t * 1.6) * 6.2832)), 10.0) * (1.0 - r) * 0.5;
	float edge = 1.0 - smoothstep(0.78, 1.0, r);
	float mask = (veins * 0.75 + ripple) * edge;
	ALBEDO = mix(tint.rgb, vec3(1.0), 0.2 * veins) * brightness;
	ALPHA = clamp(mask * tint.a, 0.0, 1.0);
}
