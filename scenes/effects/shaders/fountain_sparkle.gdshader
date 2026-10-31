shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

// A twinkling star billboard for particles rising off a magical liquid. Its size follows
// only the vertical axis of the particle transform, so a pool emitter stretched in X/Z to
// its basin radius never distorts the stars. The particle color ramp drives the fade.
uniform sampler2D sparkle : source_color, filter_linear_mipmap, repeat_disable;
instance uniform vec4 tint : source_color = vec4(1.0);
uniform float brightness = 1.6;
uniform float core_whiteness = 0.35;
uniform float twinkle_speed = 11.0;

varying float twinkle;

void vertex() {
	float size = length(MODEL_MATRIX[1].xyz);
	float spin = INSTANCE_CUSTOM.x;
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(spin), -sin(spin), 0.0, 0.0), vec4(sin(spin), cos(spin), 0.0, 0.0),
		vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world * mat4(vec4(size, 0.0, 0.0, 0.0), vec4(0.0, size, 0.0, 0.0),
		vec4(0.0, 0.0, size, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	float seed = fract(sin(float(INSTANCE_ID) * 12.9898) * 43758.5453);
	twinkle = 0.55 + 0.45 * sin(TIME * twinkle_speed * (0.7 + 0.6 * seed) + seed * 6.2832);
}

void fragment() {
	float mask = texture(sparkle, UV).a;
	vec3 hue = mix(tint.rgb, vec3(1.0), core_whiteness * mask * mask);
	ALBEDO = hue * brightness;
	ALPHA = clamp(mask * COLOR.a * twinkle * tint.a, 0.0, 1.0);
}
