shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

#include "res://scenes/magic/shared/toon/toon.gdshaderinc"
#include "res://scenes/magic/air/lightning.gdshaderinc"

varying flat float phase;

void vertex() {
	phase = toon_hash3(MODEL_MATRIX[3].xyz);
}

void fragment() {
	// Offset the recharge time and the strike pattern for each crystal deposit.
	float interval = mix(2.3, 4.1, phase);
	float clock = TIME + phase * 13.0;
	float age = mod(clock, interval);
	if (age >= 0.30) discard;

	float along = UV.x;
	float seed = phase * 63.0 + floor(clock / interval) * 5.7 + floor(age * 11.0) * 2.3;
	float ends = smoothstep(0.02, 0.22, along) * smoothstep(0.02, 0.22, 1.0 - along);
	float lane = 0.5 + (toon_zigzag(along * 7.0, seed) - 0.5) * ends * 0.7;
	float width = 0.14 * (0.4 + 0.6 * ends);
	float distance = abs(UV.y - lane) / width;
	float core = 1.0 - smoothstep(0.18, 0.40, distance);
	float glow = pow(max(0.0, 1.0 - distance), 2.0);
	vec3 color = lightning_color(core, glow);
	ALBEDO = color;
	EMISSION = color * 1.4;
	ALPHA = lightning_alpha(core, glow) * ends * (1.0 - smoothstep(0.12, 0.30, age));
}
