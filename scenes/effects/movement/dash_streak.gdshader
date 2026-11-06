shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 streak_color : source_color = vec4(0.72, 0.69, 0.59, 0.58);

void fragment() {
	float across = abs(UV.y * 2.0 - 1.0);
	float along = UV.x;
	float stripe = step(0.56, fract(along * 7.0));
	float fade = pow(1.0 - along, 0.8) * smoothstep(0.0, 0.08, along);
	float center = 1.0 - smoothstep(0.18, 1.0, across);
	float alpha = COLOR.a * stripe * fade * center;
	if (alpha < 0.025) discard;
	ALBEDO = streak_color.rgb;
	ALPHA = alpha * streak_color.a;
}
