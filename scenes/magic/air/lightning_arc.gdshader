shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"

// Stylized lightning bolt: a hot white core that blends through the glow colour into nothing, so the
// bolt reads as light rather than an outlined shape. Fading narrows the core and dims the glow.
uniform vec4 core_color : source_color = vec4(0.95, 0.99, 1.0, 1.0);
uniform vec4 glow_color : source_color = vec4(0.25, 0.6, 1.0, 1.0);
uniform float effect_fade = 1.0;
uniform float progress = 0.0;
uniform float brightness = 2.2;
// Core half-width as a fraction of the ribbon half-width; the glow fills the rest.
uniform float core_width = 0.28;

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float d = abs(UV.y * 2.0 - 1.0);
	float core = (1.0 - smoothstep(core_width * 0.6, core_width, d)) * effect_fade;
	float glow = pow(1.0 - d, 2.2) * effect_fade;
	vec3 color = mix(glow_color.rgb * glow, core_color.rgb, core);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = clamp(max(core, glow), 0.0, 1.0);
}
