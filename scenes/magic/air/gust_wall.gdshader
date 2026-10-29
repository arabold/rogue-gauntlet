shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float progress = 0.0;
uniform float effect_fade = 1.0;
void vertex() {
	float expansion = 1.0 - pow(1.0 - min(progress * 1.7, 1.0), 2.0);
	VERTEX.xz *= mix(0.1, 1.0, expansion);
	VERTEX.y += 0.18 * sin(UV.x * TAU * 5.0 - TIME * 3.0) * sin(UV.y * PI);
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 flow = vec2(UV.x * 4.0 - TIME * 0.7, UV.y * 1.3 + TIME * 0.4);
	float n = texture(noise_texture, flow).r;
	float detail = texture(noise_texture, flow * 1.8 + vec2(TIME * 0.2, 0.3)).g;
	float wisps = smoothstep(0.35, 0.75, n * 0.65 + detail * 0.35);
	float height = smoothstep(0.0, 0.18, UV.y) * (1.0 - smoothstep(0.55, 1.0, UV.y));
	vec3 color = mix(vec3(0.2, 0.48, 0.65), vec3(0.8, 0.95, 1.0), wisps);
	ALBEDO = color; EMISSION = color * 0.9;
	ALPHA = wisps * height * 0.28 * effect_fade;
}
