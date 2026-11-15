shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, ambient_light_disabled, specular_disabled, fog_disabled;

#include "res://scenes/levels/fog_of_war.gdshaderinc"
#include "res://scenes/effects/shaders/air_particle.gdshaderinc"

uniform float light_gain : hint_range(0.0, 8.0) = 2.5;
uniform float glint_strength : hint_range(0.0, 4.0) = 1.7;

void fragment() {
	float coverage = COLOR.a * air_particle_shape(UV);
	if (coverage == 0.0) discard;
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	NORMAL = normalize(particle_normal);
	ALPHA = coverage * (1.0 - fog_alpha(world_position.xz));
	if (ALPHA == 0.0) discard;
}

void light() {
	// Suspended grains scatter light through their volume instead of shading as flat camera-facing cards.
	float cosine = clamp(dot(-LIGHT, VIEW), -1.0, 1.0);
	float asymmetry = 0.35;
	float phase = (1.0 - asymmetry * asymmetry)
		/ pow(1.0 + asymmetry * asymmetry - 2.0 * asymmetry * cosine, 1.5);
	// Tiny tumbling flakes fade edge-on and briefly reflect toward the camera. No light means no visible dust.
	float facing_light = smoothstep(0.18, 0.8, abs(dot(NORMAL, LIGHT)));
	float facing_camera = smoothstep(0.1, 0.6, abs(dot(NORMAL, VIEW)));
	float glint = pow(abs(dot(NORMAL, normalize(LIGHT + VIEW))), 24.0);
	float response = light_gain * ((0.035 + 0.55 * facing_light * facing_camera) * phase + glint_strength * glint);
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * response / PI;
}
