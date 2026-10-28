shader_type spatial;
render_mode depth_draw_opaque, cull_disabled;
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"
uniform float amount : hint_range(0.0, 1.0) = 0.0;
varying vec3 local_position;
void vertex() { local_position = VERTEX; }
void fragment() {
	if (source_is_culled(FRONT_FACING)) discard;
	vec2 uv = source_uv(UV);
	vec4 original = source_albedo(uv);
	if (source_is_cutout(original.a)) discard;
	vec3 face = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	if (dot(face, NORMAL) < 0.0) face = -face;
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.0);
	float shards = pow(abs(sin(local_position.y * 24.0 + local_position.x * 13.0 + local_position.z * 9.0)), 20.0);
	vec3 ice = mix(vec3(0.22, 0.55, 0.7), vec3(0.8, 0.97, 1.0), 0.35 + rim * 0.35 + shards * 0.15);
	float reveal = smoothstep(0.0, 0.2, amount);
	ALBEDO = mix(original.rgb, ice, reveal);
	ROUGHNESS = mix(source_roughness, 0.18, reveal);
	METALLIC = mix(source_metallic, 0.08, reveal);
	SPECULAR = mix(source_specular, 0.8, reveal);
	NORMAL = normalize(mix(NORMAL, face, reveal * 0.55));
	EMISSION = vec3(0.05, 0.18, 0.24) * (rim + shards * 0.4) * reveal;
}
