// Near death: the whole view is washed red and drained of color, with a blood-red vignette that
// throbs to a lub-dub heartbeat. Higher `intensity` pulls the vignette further toward the center.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform vec3 blood_color : source_color = vec3(0.45, 0.0, 0.02);
uniform vec3 wash_color : source_color = vec3(1.0, 0.3, 0.3);
uniform float wash_strength : hint_range(0.0, 1.0) = 0.55;
uniform float desaturation : hint_range(0.0, 1.0) = 0.5;
uniform float heartbeat_rate : hint_range(0.1, 4.0) = 1.1;
uniform float pulse_strength : hint_range(0.0, 1.0) = 0.5;
// Vignette start (distance from center, 1 = corner) at full intensity.
uniform float vignette_inner : hint_range(0.0, 1.0) = 0.25;
uniform float vignette_width : hint_range(0.05, 1.0) = 0.6;

float heartbeat(float time) {
	float phase = fract(time * heartbeat_rate);
	float lub = exp(-pow((phase - 0.08) * 16.0, 2.0));
	float dub = 0.6 * exp(-pow((phase - 0.3) * 16.0, 2.0));
	return lub + dub;
}

void fragment() {
	vec3 scene = texture(screen_texture, SCREEN_UV).rgb;
	float luma = dot(scene, vec3(0.299, 0.587, 0.114));
	vec3 color = mix(scene, vec3(luma), desaturation * intensity);
	color = mix(color, color * wash_color, wash_strength * intensity);

	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 offset = (SCREEN_UV - 0.5) * vec2(aspect, 1.0);
	float dist = length(offset) / length(vec2(aspect, 1.0) * 0.5);

	float beat = heartbeat(TIME) * pulse_strength;
	float inner = mix(1.0, vignette_inner, intensity) - beat * 0.15 * intensity;
	float vignette = smoothstep(inner, inner + vignette_width, dist);

	color = mix(color, blood_color, clamp(vignette * intensity * (0.8 + beat), 0.0, 1.0));
	COLOR = vec4(color, 1.0);
}
