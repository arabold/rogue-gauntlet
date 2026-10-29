// Shared helpers for the toon effect family: cheap 3D value noise sampled in object or world
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

float toon_hash1(float x) {
	return fract(sin(x * 127.1) * 43758.5453);
}

// Piecewise-linear noise: straight runs between random corners, which reads as a lightning zigzag.
float toon_zigzag(float x, float seed) {
	float i = floor(x);
	return mix(toon_hash1(i + seed), toon_hash1(i + 1.0 + seed), fract(x));
}

// Rescales the top `width` of a signal that peaks at 1 (a sine crest, a rim) to 0..1; negative below.
float toon_crest(float value, float width) {
	return (value - (1.0 - width)) / max(width, 0.0001);
}

// Camera-facing card transform that keeps the instance scale; spin rotates it in screen space.
mat4 toon_billboard(mat4 model, mat4 inverse_view, float spin) {
	mat4 billboard = mat4(normalize(inverse_view[0]) * length(model[0]), normalize(inverse_view[1]) * length(model[1]),
		normalize(inverse_view[2]) * length(model[2]), model[3]);
	return billboard * mat4(vec4(cos(spin), -sin(spin), 0.0, 0.0), vec4(sin(spin), cos(spin), 0.0, 0.0),
		vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

// Gap between the nearest and second-nearest points of a jittered cell grid: ~0 along cell borders, so a
// threshold draws a web of straight lines, such as the plates of cracked ice.
float toon_cell_border(vec2 p) {
	vec2 cell = floor(p);
	vec2 f = fract(p);
	float d1 = 8.0;
	float d2 = 8.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 jitter = vec2(toon_hash3(vec3(cell + o, 3.7)), toon_hash3(vec3(cell + o, 9.1))) - 0.5;
			vec2 d = o + 0.5 + jitter * 0.8 - f;
			float dist = dot(d, d);
			if (dist < d1) { d2 = d1; d1 = dist; }
			else if (dist < d2) { d2 = dist; }
		}
	}
	return sqrt(d2) - sqrt(d1);
}
