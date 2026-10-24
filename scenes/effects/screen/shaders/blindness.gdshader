// Blindness: everything outside a small circle around the focus point (usually the player) goes black,
// and what remains visible is drained to a dark, cold gray. Ramping `intensity` up closes the circle
// from beyond the screen edges down to `sight_radius`, so fading the effect in reads as vision failing.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
// Driven by BlindnessScreenEffect: focus position in screen UV and radius as a fraction of screen height.
uniform vec2 focus_uv = vec2(0.5);
uniform float sight_radius : hint_range(0.0, 2.0) = 0.15;
// Fraction of the radius used for the soft falloff into darkness.
uniform float edge_softness : hint_range(0.0, 1.0) = 0.6;
uniform vec3 tint : source_color = vec3(0.62, 0.64, 0.7);
uniform float brightness : hint_range(0.0, 2.0) = 1.1;
// Slow wobble of the sight edge so the darkness feels alive rather than a flat stencil.
uniform float edge_wobble : hint_range(0.0, 0.2) = 0.04;

void fragment() {
	vec3 scene = texture(screen_texture, SCREEN_UV).rgb;
	float luma = dot(scene, vec3(0.299, 0.587, 0.114));
	vec3 gray = vec3(luma) * tint * brightness;

	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 offset = (SCREEN_UV - focus_uv) * vec2(aspect, 1.0);
	float angle = atan(offset.y, offset.x);
	float wobble = 1.0 + edge_wobble * sin(angle * 5.0 + TIME * 1.3) * sin(angle * 3.0 - TIME * 0.7);

	float radius = mix(2.0, sight_radius, intensity) * wobble;
	float sight = 1.0 - smoothstep(radius * (1.0 - edge_softness), radius, length(offset));

	vec3 blind = mix(scene, gray, intensity) * sight;
	COLOR = vec4(blind, 1.0);
}
