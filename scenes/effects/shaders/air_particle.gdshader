shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_disabled;

#include "res://scenes/levels/fog_of_war.gdshaderinc"
#include "res://scenes/effects/shaders/air_particle.gdshaderinc"

uniform sampler2D noise_texture : filter_linear, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float smoke = 0.0;

void fragment() {
	float coverage = tint.a * COLOR.a * air_particle_shape(UV);
	if (coverage == 0.0) discard;
	float breakup = mix(1.0, smoothstep(0.15, 0.8,
		texture(noise_texture, UV * 1.7 + vec2(TIME * 0.017, -TIME * 0.025)).r), smoke);
	coverage *= breakup;
	if (coverage == 0.0) discard;
	ALBEDO = tint.rgb * COLOR.rgb;
	ROUGHNESS = 1.0;
	ALPHA = coverage * (1.0 - fog_alpha(world_position.xz));
	if (ALPHA == 0.0) discard;
}
