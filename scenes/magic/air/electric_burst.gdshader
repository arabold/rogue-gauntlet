shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Expanding storm front: a jagged electric ring with radial filaments crackling behind it, each a hot
// white core blending into a soft glow. Pixels beyond the front are discarded before any other work.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform vec4 core_color : source_color = vec4(0.95, 0.99, 1.0, 1.0);
uniform vec4 glow_color : source_color = vec4(0.25, 0.6, 1.0, 1.0);
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float brightness = 1.6;
uniform float effect_fade = 1.0;

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float radius = length(p);
	float front = mix(0.12, 0.89, 1.0 - pow(1.0 - progress, 2.0));
	if (radius > front + 0.15) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float angle = atan(p.y, p.x);
	vec2 flow = vec2(angle / TAU * 4.0 + TIME * 1.8, radius * 2.0 - TIME * 0.8);
	float n = texture(noise_texture, flow).r * 0.7 + texture(noise_texture, flow * 1.9 + vec2(-TIME * 0.8, 0.37)).g * 0.3;
	float d = abs(radius - front + (n - 0.5) * 0.08) / 0.05;
	float filaments = toon_crest(sin(angle * 8.0 + n * 4.0 - TIME * 3.0), 0.08)
		* step(0.12, radius) * step(radius, front - 0.04) * smoothstep(0.35, 0.6, n);
	float fade = (1.0 - smoothstep(0.5, 1.0, progress)) * effect_fade;
	float core = max(1.0 - smoothstep(0.3, 0.6, d), step(0.5, filaments)) * fade;
	float glow = max(pow(max(0.0, 1.0 - d * 0.35), 2.0), max(filaments, 0.0) * 0.8) * fade;
	if (max(core, glow) <= 0.003) discard;
	vec3 color = mix(glow_color.rgb * glow, core_color.rgb, core);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = clamp(max(core, glow), 0.0, 1.0);
}
