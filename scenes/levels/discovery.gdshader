shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled, depth_test_disabled, depth_draw_never, shadows_disabled;

uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;
// R = explored coverage, G = discovery time multiplied by coverage.
uniform sampler2D exploration : repeat_disable, filter_linear;
uniform vec2 map_origin;
uniform vec2 map_extent;
uniform float elapsed = 1.0;
uniform float feather_width : hint_range(0.0, 3.0) = 1.0;
uniform float fade_seconds : hint_range(0.01, 2.0) = 0.35;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float coverage(vec2 uv) {
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 0.0;
	vec2 sample_value = texture(exploration, uv).rg;
	float discovered_at = sample_value.g / max(sample_value.r, 0.00001);
	return sample_value.r * smoothstep(0.0, fade_seconds, elapsed - discovered_at);
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	vec3 ndc = vec3(SCREEN_UV, depth) * 2.0 - 1.0;
#else
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
#endif
	vec4 world = INV_VIEW_MATRIX * INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	vec2 uv = (world.xz / world.w - map_origin) / map_extent;
	float center = coverage(uv);
	vec2 step_uv = vec2(feather_width) / map_extent;
	float soft = 0.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			soft += (x == 0 && y == 0) ? center : coverage(uv + vec2(float(x), float(y)) * step_uv);
		}
	}
	// Feather inward: neighboring discoveries never uncover a hidden center across a wall.
	float revealed = center * smoothstep(0.0, 0.8, soft / 9.0);
	ALBEDO = vec3(0.0);
	ALPHA = depth <= 0.000001 ? 1.0 : 1.0 - revealed;
}
