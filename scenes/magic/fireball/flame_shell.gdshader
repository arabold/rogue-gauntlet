shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float effect_fade = 1.0;
uniform sampler2D noise_texture : repeat_enable, filter_linear;
void vertex() {
	float ripple = sin(TIME * 7.0 + VERTEX.y * 10.0 + VERTEX.x * 6.0) * 0.045;
	VERTEX += NORMAL * ripple;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 flow = UV * vec2(2.8, 3.4) + vec2(TIME * 0.24, -TIME * 0.9);
	float n = texture(noise_texture, flow).r;
	float detail = texture(noise_texture, flow * 2.0 + vec2(-TIME * 0.4, 0.31)).g;
	float turbulence = n * 0.7 + detail * 0.3;
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.4);
	float tongues = smoothstep(0.30, 0.76, turbulence);
	vec3 color = mix(vec3(1.0, 0.055, 0.004), vec3(1.0, 0.42, 0.025), tongues);
	ALBEDO = color;
	EMISSION = color * 1.5;
	ALPHA = tongues * (0.06 + rim * 0.16) * effect_fade;
}
