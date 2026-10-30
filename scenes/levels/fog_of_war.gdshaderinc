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

bool fog_outside(vec2 uv) {
	return any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)));
}

// Explored coverage of an exploration texel, faded in from its discovery time.
float fog_faded(vec2 sample_value) {
	float discovered_at = sample_value.g / max(sample_value.r, 0.00001);
	return sample_value.r * smoothstep(0.0, fog_fade_seconds, fog_elapsed - discovered_at);
}

float fog_coverage(vec2 uv) {
	return fog_outside(uv) ? 0.0 : fog_faded(texture(fog_exploration, uv).rg);
}

// World position of the depth-buffer surface under a screen pixel, i.e. what the fog is drawn over there.
vec3 fog_surface_position(vec2 screen_uv, float depth, mat4 inv_projection, mat4 inv_view) {
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	vec3 ndc = vec3(screen_uv, depth) * 2.0 - 1.0;
#else
	vec3 ndc = vec3(screen_uv * 2.0 - 1.0, depth);
#endif
	vec4 world = inv_view * inv_projection * vec4(ndc, 1.0);
	return world.xyz / world.w;
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

// How unexplored a world position's own exploration sample is: 0 once explored and faded in, 1 while unexplored.
// Unlike fog_alpha it neither filters nor feathers, so explored surfaces under the fog's soft inward fringe (such as
// the near face of a closed door on the explored edge) do not count as hidden.
float fog_unexplored(vec2 world_xz) {
	if (fog_map_extent.x <= 0.0) return 0.0;
	vec2 uv = (world_xz - fog_map_origin) / fog_map_extent;
	if (fog_outside(uv)) return 1.0;
	ivec2 size = textureSize(fog_exploration, 0);
	return 1.0 - fog_faded(texelFetch(fog_exploration, min(ivec2(uv * vec2(size)), size - 1), 0).rg);
}
