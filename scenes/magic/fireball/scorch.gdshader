shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"

// Burnt floor mark laid flat by FireballImpact: a textured quad rather than a Decal, because every
// decal pairs with each overlapping instance, which gets expensive when many scorches overlap.
// Glowing cracks cool off first; the dark mark fades last.
uniform sampler2D scorch_texture : source_color, filter_linear_mipmap;
uniform sampler2D glow_texture : source_color, filter_linear_mipmap;
uniform float opacity = 1.0;
uniform float glow_energy = 3.0;

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec4 scorch = texture(scorch_texture, UV);
	vec3 glow = glow_energy > 0.0 ? texture(glow_texture, UV).rgb * glow_energy : vec3(0.0);
	float glow_mask = clamp(max(glow.r, max(glow.g, glow.b)), 0.0, 1.0);
	ALBEDO = mix(scorch.rgb, glow, glow_mask);
	EMISSION = glow;
	ALPHA = clamp(max(scorch.a, glow_mask), 0.0, 1.0) * opacity;
}
