shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Floor mark under a falling hail shard: a faint ring closes in on the landing spot while the shard falls,
// so the hit can be read and dodged, then a broken white ring bursts outward and thins away.
uniform sampler2D color_ramp : source_color, filter_nearest, repeat_disable;
// Lifetime fraction at which the shard lands; HailstormVisual sets it from the emitters' lifetimes.
uniform float impact_at = 0.4;
uniform float effect_fade = 1.0;
varying float age;
varying float spin;

void vertex() {
	age = INSTANCE_CUSTOM.y;
	spin = toon_hash3(MODEL_MATRIX[3].xyz) * 6.2832;
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p);
	float heat;
	float alpha;
	if (age < impact_at) {
		float t = age / impact_at;
		if (abs(r - mix(0.95, 0.2, t)) > 0.06) discard;
		heat = 0.55;
		alpha = mix(0.25, 0.7, t);
	} else {
		float t = (age - impact_at) / max(1.0 - impact_at, 0.001);
		float ring = abs(r - mix(0.2, 0.95, sqrt(t)));
		float broken = toon_hash3(vec3(floor((atan(p.y, p.x) + spin) * 2.5), spin, 0.0));
		if (ring > 0.09 * (1.0 - t) || broken < 0.3 + t * 0.4) discard;
		heat = 0.9;
		alpha = 0.9;
	}
	vec3 color = toon_ramp(color_ramp, heat);
	ALBEDO = color;
	EMISSION = color * 0.5;
	ALPHA = alpha * effect_fade;
}
