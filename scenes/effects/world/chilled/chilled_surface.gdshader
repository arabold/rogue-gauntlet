shader_type spatial;
render_mode depth_draw_opaque, cull_disabled;
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"

// Chilled actor: the original material shifted slightly toward a cool blue, with a faint pale rim. It stays
// subtle, unlike Frozen, so a slowed actor still reads as itself while it keeps moving.
uniform float amount : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	if (source_is_culled(FRONT_FACING)) discard;
	vec2 uv = source_uv(UV);
	vec4 original = source_albedo(uv);
	if (source_is_cutout(original.a)) discard;
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 3.0);
	vec3 cool = mix(original.rgb * vec3(0.78, 0.9, 1.08), vec3(0.75, 0.9, 1.0), rim * 0.35);
	ALBEDO = mix(original.rgb, cool, amount);
	ROUGHNESS = source_roughness;
	METALLIC = source_metallic;
	SPECULAR = source_specular;
	if (source_normal_enabled) { NORMAL_MAP = source_normal_map(uv); NORMAL_MAP_DEPTH = source_normal_scale; }
}
