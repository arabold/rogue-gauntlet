shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float effect_fade = 1.0;
uniform float progress = 0.0;
uniform vec4 water_color : source_color = vec4(0.1, 0.55, 0.8, 1.0);
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 flow = UV * vec2(2.8, 2.0) + vec2(TIME * 0.15, -TIME * 0.22);
	float n = texture(noise_texture, flow).r;
	float bands = sin(UV.y * 13.0 + n * 1.5 + sin(UV.x * TAU * 2.0 + TIME * 0.9) * 1.1 + TIME * 1.4);
	float current = smoothstep(0.55, 0.96, bands) * (0.55 + n * 0.45);
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.0);
	vec3 color = mix(water_color.rgb, vec3(0.75, 0.95, 1.0), current * 0.7 + rim * 0.15);
	ALBEDO = color; EMISSION = color * 1.2;
	ALPHA = clamp((0.025 + rim * 0.12 + current * 0.22) * effect_fade, 0.0, 1.0);
}
