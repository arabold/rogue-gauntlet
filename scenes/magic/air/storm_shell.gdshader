shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Living storm shell for guards and lightning orbs: a soft electric rim, a flickering web of thin
// veins and a few thick crawling arcs, each a hot white core blending into blue glow. Arcs and veins
// re-strike several times a second; intensity adds arcs. The centre stays mostly clear.
uniform vec4 core_color : source_color = vec4(0.95, 0.99, 1.0, 1.0);
uniform vec4 glow_color : source_color = vec4(0.25, 0.6, 1.0, 1.0);
uniform float effect_fade = 1.0;
uniform float intensity = 1.0;
uniform float brightness = 2.0;
uniform float strike_rate = 12.0;
// Arc core half-width in UV latitude; the glow reaches twice as far.
uniform float arc_width = 0.035;
varying vec3 object_direction;

void vertex() {
	object_direction = normalize(VERTEX);
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float strike = floor(TIME * strike_rate);
	int arcs = int(2.0 + intensity * 2.0);
	float core = 0.0;
	float glow = 0.0;
	for (int k = 0; k < 6; k++) {
		if (k >= arcs) break;
		float seed = strike * 1.37 + float(k) * 7.9;
		float lane = (float(k) + 0.5) / float(arcs) + (toon_hash1(seed) - 0.5) * 0.2;
		float d = abs(UV.y - lane - (toon_zigzag(UV.x * 16.0, seed) - 0.5) * 0.14) / arc_width;
		float piece = step(0.4, toon_zigzag(UV.x * 3.0, seed + 5.0));
		core = max(core, (1.0 - smoothstep(0.25, 0.45, d)) * piece);
		glow = max(glow, pow(max(0.0, 1.0 - d * 0.5), 2.0) * piece);
	}
	float n = toon_noise3(object_direction * 4.0 + vec3(0.0, 0.0, strike * 0.71));
	float vein = pow(1.0 - abs(n * 2.0 - 1.0), 14.0) * step(0.35, toon_noise3(object_direction * 2.0 + strike * 0.37));
	float rim = pow(1.0 - abs(dot(NORMAL, VIEW)), 3.0);
	glow = max(glow, max(vein * 0.8, rim * 0.6)) * effect_fade;
	core *= effect_fade;
	if (max(core, glow) <= 0.003) discard;
	vec3 color = mix(glow_color.rgb * glow, core_color.rgb, core) * (0.75 + 0.5 * toon_hash1(strike * 3.1));
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = clamp(max(core, glow), 0.0, 1.0);
}
