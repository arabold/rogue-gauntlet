shader_type spatial;
render_mode depth_draw_opaque, cull_disabled;
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"
#include "res://scenes/effects/world/shaders/skin_transition.gdshaderinc"
// The gelatinous cube's goo over an actor's own surfaces: wet slime green with a slow drifting sheen, rising from the
// feet as `amount` grows.
uniform float amount : hint_range(0.0, 1.0) = 0.0;
varying vec3 local_position;
void vertex() { local_position = VERTEX; }
void fragment() {
	if (source_is_hidden(FRONT_FACING, VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 uv = source_uv(UV);
	vec4 original = source_albedo(uv);
	if (source_is_cutout(original.a)) discard;
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.0);
	float sheen = 0.5 + 0.5 * sin(local_position.y * 9.0 - TIME * 1.6 + local_position.x * 4.0);
	vec3 goo = mix(vec3(0.26, 0.56, 0.16), vec3(0.74, 0.94, 0.34), 0.25 + rim * 0.45 + sheen * 0.15);
	float reveal = skin_transition(local_position, amount);
	ALBEDO = mix(original.rgb, mix(original.rgb * 0.45, goo, 0.8), reveal);
	ROUGHNESS = mix(source_roughness, 0.12, reveal);
	METALLIC = mix(source_metallic, 0.0, reveal);
	SPECULAR = mix(source_specular, 0.85, reveal);
	if (source_normal_enabled) { NORMAL_MAP = source_normal_map(uv); NORMAL_MAP_DEPTH = source_normal_scale * (1.0 - reveal); }
	EMISSION = vec3(0.08, 0.16, 0.03) * (rim + sheen * 0.3) * reveal;
}
