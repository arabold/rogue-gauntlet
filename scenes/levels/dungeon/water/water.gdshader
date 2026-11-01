shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, shadows_disabled;

// Still dungeon water on a transparent surface over an opaque bed: a clear film right at the surface that turns
// murky within a short depth, a pale band wherever it meets stone or something floating, and slow drifting light.
// Depth comes from the depth buffer, so beds, quay walls and sunken props darken with how far below they lie.
uniform vec3 clear_color : source_color = vec3(0.38, 0.70, 0.64);
uniform vec3 murk_color : source_color = vec3(0.11, 0.29, 0.27);
// Opacity where the water is shallowest.
uniform float clarity : hint_range(0.0, 1.0) = 0.3;
// Water depth along the view at which it is all but opaque.
uniform float murk_depth : hint_range(0.05, 4.0) = 1.1;
uniform vec3 foam_color : source_color = vec3(0.66, 0.84, 0.76);
uniform float foam_width : hint_range(0.0, 0.5) = 0.08;
uniform float ripple_scale = 0.55;
uniform float ripple_speed = 0.05;
uniform float ripple_light : hint_range(0.0, 1.0) = 0.07;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

varying vec3 world;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0)), u.x), u.y);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// View-space position of whatever opaque surface lies behind this point of the water.
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, texture(depth_texture, SCREEN_UV).r, 1.0);
	behind.xyz /= behind.w;
	float thickness = max(VERTEX.z - behind.z, 0.0);
	float murk = 1.0 - exp(-3.0 * thickness / murk_depth);
	float foam = 1.0 - smoothstep(0.0, foam_width, thickness);
	vec2 p = world.xz * ripple_scale;
	float t = TIME * ripple_speed;
	// Two noise layers drifting against each other lift soft, slowly changing patches of light.
	float a = noise(p + vec2(t, -0.6 * t));
	float b = noise(p * 0.7 - vec2(0.5 * t, 0.3 * t) + 3.7);
	float patches = smoothstep(0.55, 0.85, a * 0.6 + b * 0.4);
	vec3 color = mix(clear_color, murk_color, murk) + patches * ripple_light;
	ALBEDO = mix(color, foam_color, foam * 0.85);
	ALPHA = clamp(mix(clarity, 1.0, murk) + foam * 0.5, 0.0, 1.0);
	ROUGHNESS = 0.12;
	SPECULAR = 0.6;
}
