// Soft billboards shared by smoke and dust; particle rotation supplies a stable, per-grain phase for dust glints.
varying vec3 world_position;
varying float particle_phase;
varying vec3 particle_normal;

void vertex() {
	mat4 particle_scale = mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]) * particle_scale;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	world_position = (INV_VIEW_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	particle_phase = INSTANCE_CUSTOM.x;
	// Independent slow tumbles give each grain a changing orientation to the actual lights and camera.
	float spin = TIME * (0.55 + 0.6 * fract(particle_phase * 1.73)) + particle_phase;
	float tilt = TIME * (0.41 + 0.27 * fract(particle_phase * 2.39)) + particle_phase * 2.1;
	particle_normal = mat3(VIEW_MATRIX) * vec3(cos(spin) * sin(tilt), cos(tilt), sin(spin) * sin(tilt));
}

float air_particle_shape(vec2 uv) {
	vec2 p = uv * 2.0 - 1.0;
	return exp(-dot(p, p) * 3.5) * (1.0 - smoothstep(0.65, 1.0, length(p)));
}
