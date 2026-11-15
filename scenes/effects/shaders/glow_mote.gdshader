shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, fog_disabled;

#include "res://scenes/levels/fog_of_war.gdshaderinc"
#include "res://scenes/effects/shaders/air_particle.gdshaderinc"

uniform float emission_energy : hint_range(0.0, 8.0) = 2.8;

void fragment() {
	float coverage = COLOR.a * air_particle_shape(UV);
	if (coverage == 0.0) discard;
	float pulse = 0.65 + 0.35 * sin(TIME * 0.7 + particle_phase);
	ALBEDO = vec3(0.0);
	EMISSION = COLOR.rgb * emission_energy * pulse;
	ALPHA = coverage * (1.0 - fog_alpha(world_position.xz));
	if (ALPHA == 0.0) discard;
}
