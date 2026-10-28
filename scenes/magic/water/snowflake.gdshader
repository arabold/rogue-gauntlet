shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float effect_fade = 1.0;
uniform float storm_radius = 4.375;
varying float particle_age;
void vertex() {
	particle_age = INSTANCE_CUSTOM.y;
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float edge = 1.0 - smoothstep(storm_radius * 0.82, storm_radius, distance(world.xz, area_origin));
	vec2 p = abs(UV * 2.0 - 1.0);
	float shard = 1.0 - smoothstep(0.28, 0.9, p.x * 0.8 + p.y);
	float life = smoothstep(0.0, 0.1, particle_age) * (1.0 - smoothstep(0.7, 1.0, particle_age));
	ALBEDO = vec3(0.78, 0.94, 1.0);
	EMISSION = vec3(0.3, 0.62, 0.95);
	ALPHA = shard * edge * life * COLOR.a * effect_fade;
}
