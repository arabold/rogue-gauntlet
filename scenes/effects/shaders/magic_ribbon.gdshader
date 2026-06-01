shader_type spatial;
render_mode blend_add, cull_disabled, unshaded;

uniform vec4 head_color : source_color = vec4(1.0, 0.72, 0.18, 1.0);
uniform vec4 tail_color : source_color = vec4(1.0, 0.08, 0.01, 0.0);
uniform float emission_intensity = 4.0;
uniform float edge_softness = 0.35;

void fragment() {
	float along = UV.x;
	float across = abs(UV.y - 0.5) * 2.0;
	float edge = 1.0 - smoothstep(edge_softness, 1.0, across);
	float fade = 1.0 - along;
	vec4 color = mix(head_color, tail_color, along);
	ALBEDO = color.rgb;
	EMISSION = color.rgb * emission_intensity * edge * fade;
	ALPHA = color.a * edge * fade;
}
