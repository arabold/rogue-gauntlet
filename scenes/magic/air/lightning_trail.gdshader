shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Crackling lightning wake for a travelling orb: jagged bolt strands pinned to the orb that re-strike
// several times a second and thin out toward the tail. Each strand has a hot white core blending into
// a soft glow. UV.x runs from the head to the tail.
uniform vec4 core_color : source_color = vec4(0.95, 0.99, 1.0, 1.0);
uniform vec4 glow_color : source_color = vec4(0.25, 0.6, 1.0, 1.0);
uniform float effect_fade = 1.0;
uniform float brightness = 2.2;
uniform float strike_rate = 14.0;
uniform float corners = 9.0;
// Glow half-width of the main strand, as a fraction of the ribbon width.
uniform float strand_width = 0.22;
uniform int strands = 2;

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float along = UV.x;
	float strike = floor(TIME * strike_rate);
	float taper = (1.0 - along) * smoothstep(0.0, 0.05, along) * effect_fade;
	float core = 0.0;
	float glow = 0.0;
	for (int s = 0; s < strands; s++) {
		float seed = strike * 3.17 + float(s) * 11.3;
		float center = 0.5 + (toon_zigzag(along * corners, seed) - 0.5) * 0.7 * smoothstep(0.0, 0.25, along);
		float d = abs(UV.y - center) / max(strand_width * taper * (s == 0 ? 1.0 : 0.6), 0.001);
		core = max(core, 1.0 - smoothstep(0.18, 0.32, d));
		glow = max(glow, pow(max(0.0, 1.0 - d), 2.0));
	}
	if (max(core, glow) <= 0.003) discard;
	vec3 color = mix(glow_color.rgb * glow, core_color.rgb, core);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = clamp(max(core, glow), 0.0, 1.0);
}
