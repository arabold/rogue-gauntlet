// Shared radial fissure pattern; each surface supplies its own edge widths and timing.
float fracture_hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float fracture_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(fracture_hash(i), fracture_hash(i + vec2(1.0, 0.0)), f.x), mix(fracture_hash(i + vec2(0.0, 1.0)), fracture_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
vec2 fracture_lines(vec2 p, vec2 seam_edges, float ring_outer) {
	float radius = length(p), noise = fracture_noise(p * 5.0 + 4.7);
	float arms = abs(sin(atan(p.y, p.x) * 4.0 + radius * 1.6 + (noise - 0.5) * 1.3));
	return vec2(1.0 - smoothstep(seam_edges.x, seam_edges.y, arms),
		1.0 - smoothstep(0.025, ring_outer, abs(radius - 0.38 - (noise - 0.5) * 0.1)));
}
