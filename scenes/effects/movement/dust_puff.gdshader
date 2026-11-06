shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

void fragment() {
	vec2 p = (UV - vec2(0.5)) * 2.0;
	float angle = atan(p.y, p.x);
	float irregularity = 0.82 + 0.08 * sin(angle * 5.0) + 0.06 * sin(angle * 3.0 + 1.7);
	float radius = length(p) / irregularity;
	if (radius >= 1.0) discard;
	float edge = 1.0 - smoothstep(0.72, 1.0, radius);
	ALBEDO = COLOR.rgb;
	ALPHA = COLOR.a * edge;
}
