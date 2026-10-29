shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Helical ember wisps orbiting the guard, cut into crisp, tapering toon strands that run hot along
// their centre. Higher levels add strands; a hit thickens them briefly.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
uniform float hit_flash = 0.0;
uniform int wisp_count = 4;
uniform float effect_fade = 1.0;
uniform float brightness = 1.7;
uniform float strand_width = 0.04;

void fragment() {
	float helix = UV.x * TAU * 2.0 + UV.y * 13.0 - TIME * 3.5;
	float strands = cos(helix + sin(UV.y * 13.0 - TIME * 2.0) * 0.8);
	strands = max(strands, cos(helix + 1.6) * 0.98);
	if (wisp_count >= 3) strands = max(strands, cos(helix + 3.2) * 0.99);
	if (wisp_count >= 4) strands = max(strands, cos(helix + 4.8) * 0.98);
	float taper = smoothstep(0.0, 0.12, UV.y) * (1.0 - smoothstep(0.8, 1.0, UV.y));
	float width = strand_width * taper * effect_fade * (1.0 + hit_flash);
	float core = toon_crest(strands, width);
	if (core <= 0.0) discard;
	vec3 color = toon_ramp(color_ramp, 0.25 + core * 0.6 + hit_flash * 0.25);
	ALBEDO = color;
	EMISSION = color * (brightness + hit_flash);
	ALPHA = smoothstep(0.0, 0.08, core);
}
