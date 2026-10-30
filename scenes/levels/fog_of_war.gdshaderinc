// The floor's exploration, published by FloorDiscovery as global shader uniforms (declared in project.godot), so the
// fog surface and the silhouettes that must stay readable above it agree on exactly what is still covered.
// R = explored coverage, G = discovery time multiplied by coverage.
global uniform sampler2D fog_exploration;
global uniform vec2 fog_map_origin;
// Zero while no floor is loaded, which leaves everything uncovered.
global uniform vec2 fog_map_extent;
global uniform float fog_elapsed;
global uniform float fog_fade_seconds;
global uniform float fog_feather_width;

float fog_coverage(vec2 uv) {
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 0.0;
	vec2 sample_value = texture(fog_exploration, uv).rg;
	float discovered_at = sample_value.g / max(sample_value.r, 0.00001);
	return sample_value.r * smoothstep(0.0, fog_fade_seconds, fog_elapsed - discovered_at);
}

// How opaque the fog is over a world position: 0 once explored and faded in, 1 while unexplored.
float fog_alpha(vec2 world_xz) {
	if (fog_map_extent.x <= 0.0) return 0.0;
	vec2 uv = (world_xz - fog_map_origin) / fog_map_extent;
	float center = fog_coverage(uv);
	vec2 step_uv = vec2(fog_feather_width) / fog_map_extent;
	float soft = 0.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			soft += (x == 0 && y == 0) ? center : fog_coverage(uv + vec2(float(x), float(y)) * step_uv);
		}
	}
	// Feather inward: neighboring discoveries never uncover a hidden center across a wall.
	return 1.0 - center * smoothstep(0.0, 0.8, soft / 9.0);
}
