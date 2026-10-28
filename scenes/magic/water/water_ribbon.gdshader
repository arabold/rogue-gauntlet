shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float effect_fade = 1.0;
uniform float brightness = 1.1;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float along = UV.x, across = abs(UV.y * 2.0 - 1.0);
	vec2 flow = vec2(along * 3.0 - TIME * 1.7, UV.y * 1.7 + TIME * 0.35);
	float n = texture(noise_texture, flow).r;
	float detail = texture(noise_texture, flow * 2.1 + vec2(TIME * 0.6, 0.0)).g;
	float edge = 1.0 - smoothstep(0.35 + n * 0.12, 0.95, across);
	float tail = pow(1.0 - along, 0.65) * smoothstep(0.0, 0.04, along);
	float current = smoothstep(0.62, 0.9, n * 0.65 + detail * 0.35);
	vec3 color = mix(vec3(0.025, 0.27, 0.38), vec3(0.48, 0.92, 0.94), clamp(current * 0.65 + detail * 0.3, 0.0, 1.0));
	ALBEDO = color; EMISSION = color * brightness;
	ALPHA = tail * edge * (0.55 + n * 0.35 + current * 0.2) * effect_fade;
}
