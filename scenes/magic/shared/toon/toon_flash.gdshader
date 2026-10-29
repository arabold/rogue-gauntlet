shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Toon impact flash card: on the first frames a white-hot burst pops larger than the effect, then
// settles, cools through the ramp and erodes away. Star points shape lightning flashes; zero gives a
// jagged round burst for explosions. Author the quad at its popped size.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float progress = 0.0;
uniform float effect_fade = 1.0;
uniform float brightness = 2.5;
uniform float star_points = 0.0;
// First-frame size relative to the settled flash, and the progress over which it settles.
uniform float pop_scale = 2.0;
uniform float pop_time = 0.08;
// Progress by which the flash has eroded away; 1 leaves the fade to effect_fade.
uniform float lifetime = 1.0;
// Crisp toon edge by default; lightning uses a soft edge so the flash blends like light.
uniform float edge_softness = 0.08;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * toon_billboard(MODEL_MATRIX, INV_VIEW_MATRIX, 0.0);
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0 * pop_scale / mix(pop_scale, 1.0, smoothstep(0.0, pop_time, progress));
	float r = length(p);
	if (r >= 1.0) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float angle = atan(p.y, p.x);
	float reach = star_points > 0.0
		? mix(0.3, 1.0, pow(abs(cos(angle * star_points * 0.5)), 8.0))
		: 0.75 + 0.25 * toon_noise3(vec3(cos(angle), sin(angle), progress * 2.0) * 3.0);
	float body = 1.0 - r / reach - smoothstep(pop_time, lifetime, progress) - (1.0 - effect_fade);
	if (body <= 0.0) discard;
	vec3 color = toon_ramp(color_ramp, 1.0 - smoothstep(0.0, lifetime, progress) * 0.7 + body * 0.3);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, edge_softness, body);
}
