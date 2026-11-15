shader_type spatial;
render_mode blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled;
#include "res://scenes/levels/fog_of_war.gdshaderinc"
uniform vec4 tint : source_color = vec4(0.67, 0.84, 0.92, 0.8);
uniform float drop_height : hint_range(0.5, 8.0) = 3.2;
uniform float splash_radius : hint_range(0.02, 0.6) = 0.28;
uniform float drop_width : hint_range(0.005, 0.1) = 0.045;
uniform float drop_length : hint_range(0.02, 0.5) = 0.22;
uniform float idle_fraction : hint_range(0.0, 0.6) = 0.2;
uniform float splash_fraction : hint_range(0.05, 0.4) = 0.25;
// Splash rings project onto opaque scene surfaces instead of assuming a flat Y=0 floor.
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
uniform float surface_range : hint_range(0.05, 1.5) = 0.8;
varying vec3 impact_center;
varying float age;
varying vec3 world;
void vertex() {
	age = INSTANCE_CUSTOM.y;
	vec3 center = MODEL_MATRIX[3].xyz;
	impact_center = center;
	float impact = 1.0 - splash_fraction;
	float start = min(idle_fraction, impact - 0.05);
	float falling = clamp((age - start) / (impact - start), 0.0, 1.0);
	if (age < impact) {
		world = center + vec3(0.0, drop_height * (1.0 - falling * falling), 0.0)
			+ INV_VIEW_MATRIX[0].xyz * VERTEX.x * drop_width
			+ INV_VIEW_MATRIX[1].xyz * VERTEX.y * drop_length;
	} else {
		world = center + vec3(VERTEX.x, 0.0, VERTEX.y) * (splash_radius + surface_range) * 2.0;
	}
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
}
void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float shape;
	float depth = texture(depth_texture, SCREEN_UV).r;
	vec3 surface = fog_surface_position(SCREEN_UV, depth, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
	float impact = 1.0 - splash_fraction;
	float start = min(idle_fraction, impact - 0.05);
	if (age < impact) {
		// Falling drops retain ordinary scene occlusion even though the projected ring bypasses hardware depth.
		float surface_z = (VIEW_MATRIX * vec4(surface, 1.0)).z;
		if (VERTEX.z < surface_z - 0.005) discard;
		shape = (1.0 - smoothstep(0.3, 1.0, r)) * step(start, age);
	} else {
		// Restrict projection to nearby, upward-facing surfaces; do not paint rings on walls or furniture above.
		if (abs(surface.y - impact_center.y) > surface_range) discard;
		vec3 surface_normal = normalize(cross(dFdx(surface), dFdy(surface)));
		if (abs(surface_normal.y) < 0.65) discard;
		r = length(surface.xz - impact_center.xz) / splash_radius;
		float t = (age - impact) / splash_fraction;
		shape = (1.0 - smoothstep(0.04, 0.13, abs(r - mix(0.12, 0.9, t)))) * (1.0 - t);
	}
	ALBEDO = tint.rgb;
	ROUGHNESS = 0.08;
	SPECULAR = 0.8;
	ALPHA = tint.a * shape * (1.0 - fog_alpha(age < impact ? world.xz : surface.xz));
	if (ALPHA <= 0.0) discard;
}
