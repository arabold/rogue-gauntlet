// Poison: the view slowly tilts and sways, ripples like a heat haze, splits into a drifting double image
// and takes on a sickly green cast. Everything scales with `intensity`, so a fading poison eases out.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear, repeat_disable;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float max_tilt_degrees : hint_range(0.0, 30.0) = 7.0;
uniform float tilt_speed : hint_range(0.0, 5.0) = 0.6;
// Slight zoom for the swaying look. The warp zooms in further whenever the tilt needs it to keep the
// screen corners covered, so `max_tilt_degrees` can be tuned without exposing the clamped screen edge.
uniform float zoom : hint_range(0.0, 0.5) = 0.12;
uniform float wave_amplitude : hint_range(0.0, 0.05) = 0.01;
uniform float wave_frequency : hint_range(0.0, 40.0) = 8.0;
uniform float wave_speed : hint_range(0.0, 10.0) = 1.8;
uniform float ghost_offset : hint_range(0.0, 0.05) = 0.014;
uniform float ghost_strength : hint_range(0.0, 1.0) = 0.4;
uniform vec3 tint : source_color = vec3(0.6, 1.0, 0.45);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.45;

vec2 warp(vec2 uv, float aspect, float time) {
	vec2 p = (uv - 0.5) * vec2(aspect, 1.0);

	float angle = radians(max_tilt_degrees) * intensity
		* (0.7 * sin(time * tilt_speed) + 0.3 * sin(time * tilt_speed * 2.3 + 1.7));
	float c = cos(angle);
	float s = sin(angle);
	p = mat2(vec2(c, s), vec2(-s, c)) * p;

	float breathe = 1.0 + intensity * (zoom + 0.03 * sin(time * 1.1));
	// A view rotated by `angle` only covers the screen when scaled by at least cos + aspect * sin.
	breathe = max(breathe, abs(c) + max(aspect, 1.0 / aspect) * abs(s));
	p /= breathe;

	float amp = wave_amplitude * intensity;
	p.x += sin(p.y * wave_frequency + time * wave_speed) * amp;
	p.y += cos(p.x * wave_frequency * 0.8 + time * wave_speed * 1.3) * amp;

	return p / vec2(aspect, 1.0) + 0.5;
}

void fragment() {
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 uv = warp(SCREEN_UV, aspect, TIME);

	vec2 drift = vec2(cos(TIME * 0.9), sin(TIME * 1.3)) * ghost_offset * intensity;
	vec3 scene = texture(screen_texture, uv).rgb;
	vec3 ghost = texture(screen_texture, uv + drift).rgb;
	vec3 color = mix(scene, ghost, ghost_strength * intensity);

	color = mix(color, color * tint, tint_strength * intensity);
	COLOR = vec4(color, 1.0);
}
