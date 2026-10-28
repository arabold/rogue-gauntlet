shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform vec4 hot_color : source_color = vec4(1.0, 0.65, 0.08, 1.0);
uniform vec4 fire_color : source_color = vec4(1.0, 0.18, 0.015, 1.0);
uniform float brightness = 1.5;
uniform float effect_fade = 1.0;
void vertex() {
	float pulse = sin(TIME * 8.0 + VERTEX.y * 8.0) * 0.025;
	VERTEX += NORMAL * pulse;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 flow = UV * vec2(2.1, 3.0) + vec2(TIME * 0.3, -TIME * 0.8);
	float n = texture(noise_texture, flow).r;
	float detail = texture(noise_texture, flow * 2.1 + vec2(-TIME * 0.4, 0.3)).g;
	float heat = smoothstep(0.2, 0.8, n * 0.7 + detail * 0.3);
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.2);
	vec3 color = mix(fire_color.rgb, hot_color.rgb, heat * 0.8);
	ALBEDO = color;
	EMISSION = color * (brightness + rim * 0.35);
	ALPHA = 0.9 * effect_fade;
}
