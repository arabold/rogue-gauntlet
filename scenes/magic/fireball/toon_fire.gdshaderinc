// Shared helpers for the toon fire family: cheap 3D value noise sampled in object or world
// space (no UV seams or pole pinching) and a hard-banded ramp lookup. Ramps use constant
// gradient interpolation, so heat steps through flat colour bands like KayKit's toon palette.
float toon_hash3(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float toon_noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(toon_hash3(i), toon_hash3(i + vec3(1.0, 0.0, 0.0)), f.x),
			mix(toon_hash3(i + vec3(0.0, 1.0, 0.0)), toon_hash3(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
		mix(mix(toon_hash3(i + vec3(0.0, 0.0, 1.0)), toon_hash3(i + vec3(1.0, 0.0, 1.0)), f.x),
			mix(toon_hash3(i + vec3(0.0, 1.0, 1.0)), toon_hash3(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}

float toon_fbm3(vec3 p) {
	return toon_noise3(p) * 0.65 + toon_noise3(p * 2.03 + 7.1) * 0.35;
}

// Particle and vertex colours arrive unconverted; ramps authored in sRGB need linearising.
vec3 toon_srgb_to_linear(vec3 c) {
	return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}

vec3 toon_ramp(sampler2D ramp, float heat) {
	return texture(ramp, vec2(clamp(heat, 0.0, 0.999), 0.5)).rgb;
}
