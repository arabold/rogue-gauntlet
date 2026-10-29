uniform bool area_clip_enabled = false;
uniform sampler2D area_visibility : filter_nearest, repeat_disable;
uniform vec2 area_origin = vec2(0.0);
uniform float area_extent = 1.0;

bool area_is_blocked(vec3 view_position, mat4 inverse_view) {
	if (!area_clip_enabled) return false;
	vec3 world = (inverse_view * vec4(view_position, 1.0)).xyz;
	vec2 relative = world.xz - area_origin;
	float angle = mod(atan(relative.y, relative.x) + TAU, TAU) / TAU;
	float step_size = 1.0 / float(textureSize(area_visibility, 0).x);
	float a = texture(area_visibility, vec2(fract(angle), 0.5)).r;
	float b = texture(area_visibility, vec2(fract(angle - step_size), 0.5)).r;
	float c = texture(area_visibility, vec2(fract(angle + step_size), 0.5)).r;
	return length(relative) > min(a, min(b, c)) * area_extent;
}
