shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Rising wind wall cut into thin, crisp swoosh streaks so characters behind it stay visible.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
// Fraction of the gameplay radius the wave has reached, fed by its timeline's WaveFront so hits follow the drawn front.
uniform float wave_front = 0.0;
uniform float effect_fade = 1.0;
uniform float brightness = 1.1;

void vertex() {
	// The wall mesh stands at the gameplay radius, so it rides the ground wave's front.
	VERTEX.xz *= wave_front;
	VERTEX.y += 0.18 * sin(UV.x * TAU * 5.0 - TIME * 3.0) * sin(UV.y * PI);
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 flow = vec2(UV.x * 4.0 - TIME * 0.7, UV.y * 1.3 + TIME * 0.4);
	float n = texture(noise_texture, flow).r * 0.65 + texture(noise_texture, flow * 1.8 + vec2(TIME * 0.2, 0.3)).g * 0.35;
	float height = smoothstep(0.0, 0.18, UV.y) * (1.0 - smoothstep(0.55, 1.0, UV.y));
	float streak = toon_crest(sin(UV.y * 22.0 + n * 6.0 - UV.x * 9.0), 0.2) * smoothstep(0.4, 0.62, n);
	float body = streak * height - (1.0 - effect_fade);
	if (body <= 0.0) discard;
	vec3 color = toon_ramp(color_ramp, 0.4 + body * 0.6);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, 0.1, body);
}
