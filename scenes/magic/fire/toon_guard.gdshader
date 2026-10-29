shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/fireball/toon_fire.gdshaderinc"

// Sustained guard shell: crisp flame tongues lick up its silhouette while the centre stays clear, so
// the caster remains readable. A hit briefly thickens and heats the rim.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float hit_flash = 0.0;
uniform float effect_fade = 1.0;
uniform float brightness = 1.6;
uniform float rim_width = 0.42;
varying vec3 object_position;

void vertex() {
	object_position = VERTEX;
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float rim = 1.0 - abs(dot(NORMAL, VIEW));
	vec3 d = normalize(object_position);
	float angle = atan(d.z, d.x) / TAU;
	float n = texture(noise_texture, vec2(angle * 3.0, d.y * 0.8 - TIME * 0.9)).r * 0.7
		+ texture(noise_texture, vec2(angle * 6.0 + 0.3, d.y * 1.6 - TIME * 1.6)).g * 0.3;
	float lift = d.y * 0.5 + 0.5;
	float body = rim - (1.0 - rim_width) + (n - 0.5) * (0.5 + lift * 0.4) + hit_flash * 0.12 - (1.0 - effect_fade);
	vec3 color = toon_ramp(color_ramp, rim * 0.5 + n * 0.5 + hit_flash * 0.2 - 0.05);
	ALBEDO = color;
	EMISSION = color * (brightness + hit_flash);
	ALPHA = smoothstep(0.0, 0.05, body);
}
