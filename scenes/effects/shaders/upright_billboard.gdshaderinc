// Face the camera around world Y, preserving the model's nonuniform animation scale.
mat4 upright_billboard_transform(mat4 model, mat4 inverse_view) {
	vec3 right = vec3(inverse_view[0].x, 0.0, inverse_view[0].z);
	right = dot(right, right) > 0.000001 ? normalize(right) : vec3(1.0, 0.0, 0.0);
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 front = cross(right, up);
	return mat4(vec4(right * length(model[0].xyz), 0.0),
		vec4(up * length(model[1].xyz), 0.0), vec4(front * length(model[2].xyz), 0.0), model[3]);
}
