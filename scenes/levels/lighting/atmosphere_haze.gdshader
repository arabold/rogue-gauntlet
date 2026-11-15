shader_type fog;

#include "res://scenes/levels/fog_of_war.gdshaderinc"

uniform sampler2D floor_mask : filter_linear, repeat_disable;
uniform sampler3D noise_texture : filter_linear, repeat_enable;
uniform vec2 map_origin;
uniform vec2 map_extent = vec2(1.0);
uniform vec3 haze_color : source_color = vec3(0.8, 0.84, 0.9);
uniform float density : hint_range(0.0,0.5,0.001) = 0.035;
uniform float ambient_scatter : hint_range(0.0,0.2,0.001) = 0.035;
uniform float noise_scale : hint_range(0.02,1.0,0.01) = 0.16;
uniform vec3 drift = vec3(0.14, 0.0, 0.06);
uniform float phase = 0.0;
uniform float elapsed = 0.0;
uniform float wobble_amplitude : hint_range(0.0,3.0,0.05) = 0.95;
uniform float wobble_period : hint_range(4.0,60.0,1.0) = 20.0;

void fog() {
	vec2 uv = (WORLD_POSITION.xz - map_origin) / map_extent;
	float floor_coverage = texture(floor_mask, uv).r;
	float height = clamp((WORLD_POSITION.y - OBJECT_POSITION.y + SIZE.y * 0.5) / SIZE.y, 0.0, 1.0);
	float height_fade = 1.0 - smoothstep(0.0, 1.0, height);
	float edge = 1.0 - smoothstep(-0.3, 0.0, SDF);
	DENSITY = 0.0;
	// Only contributing cells need exploration sampling, deformation and 3D noise.
	if (density != 0.0 && floor_coverage > 0.0 && height_fade > 0.0 && edge > 0.0) {
		float explored = 1.0 - fog_alpha(WORLD_POSITION.xz);
		if (explored > 0.0) {
			float t = elapsed * TAU / max(wobble_period, 4.0);
			// Spatially varying deformation makes neighboring wisps curl and wobble rather than sliding as one flat pattern.
			vec3 wobble = vec3(
				sin(WORLD_POSITION.z * 0.62 + t + phase) + 0.35 * sin(WORLD_POSITION.x * 0.9 - t * 0.7),
				0.18 * sin(WORLD_POSITION.x * 0.48 + WORLD_POSITION.z * 0.31 + t * 0.63 + phase),
				cos(WORLD_POSITION.x * 0.56 + t * 0.83 + phase) + 0.35 * sin(WORLD_POSITION.z * 0.8 + t * 0.9)
			) * wobble_amplitude;
			// Keep the pattern coherent through the height of the mist so the view ray does not average its motion away.
			vec3 p = (WORLD_POSITION + wobble) * vec3(noise_scale, noise_scale * 0.3, noise_scale) + vec3(phase);
			vec3 flow = elapsed * drift * noise_scale;
			float body = texture(noise_texture, p - flow).r;
			float detail = texture(noise_texture, p * 1.65 + flow * 0.65 + vec3(0.37, 0.61, 0.19)).r;
			float clouds = mix(body, detail, 0.2);
			// Distinct soft gaps let the wisps visibly travel and deform, instead of just changing a uniform veil's brightness.
			float patches = smoothstep(0.4, 0.6, clouds);
			DENSITY = density * patches * height_fade * floor_coverage * explored * edge;
		}
	}
	ALBEDO = haze_color;
	// A small ambient contribution keeps local mist readable away from fixtures without lighting surfaces.
	EMISSION = haze_color * ambient_scatter;
}
