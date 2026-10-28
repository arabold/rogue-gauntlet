shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float intensity = 1.0;
uniform float effect_fade = 1.0;
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float radius = length(p);
	float angle = atan(p.y, p.x);
	float front = mix(0.12, 0.89, 1.0 - pow(1.0 - progress, 2.0));
	vec2 flow = vec2(angle / TAU * 4.0 + TIME * 1.8, radius * 2.0 - TIME * 0.8);
	float noise = texture(noise_texture, flow).r;
	float detail = texture(noise_texture, flow * 1.9 + vec2(-TIME * 0.8, 0.37)).g;
	float turbulence = noise * 0.7 + detail * 0.3;
	float ring = exp(-pow((radius - front + (turbulence - 0.5) * 0.075) / 0.085, 2.0));
	float filaments = pow(max(0.0, sin(angle * 8.0 + turbulence * 4.0 - TIME * 3.0)), 4.0);
	float trails = filaments * smoothstep(0.25, 0.7, turbulence) * smoothstep(0.08, 0.24, radius) * (1.0 - smoothstep(front - 0.1, front + 0.04, radius));
	float fade = smoothstep(0.0, 0.05, progress + 0.01) * (1.0 - smoothstep(0.5, 1.0, progress));
	float hot = clamp(ring * (0.4 + turbulence * 0.4) + trails * 0.25, 0.0, 0.85);
	vec3 color = mix(vec3(0.06, 0.22, 0.95), vec3(0.7, 0.94, 1.0), hot);
	ALBEDO = color; EMISSION = color * (1.1 + intensity * 0.4);
	ALPHA = clamp((ring * (0.55 + turbulence * 0.4) + trails * 0.7) * fade * intensity * effect_fade, 0.0, 1.0);
}
