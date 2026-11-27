shader_type spatial;
// The holes a spike trap's spikes rise through, drawn on whatever floor the trap lies on instead of on a tile of its
// own: a grid of octagonal holes with a soft dark lip (see ground_overlay.gdshaderinc for how it stays on any floor).
// reveal and reveal_glow let DiscoveryPulse fade the holes in with its glow when the trap is found.
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

#include "res://scenes/effects/shaders/ground_overlay.gdshaderinc"

uniform vec4 hole_color : source_color = vec4(0.02, 0.018, 0.016, 0.95);
uniform vec4 lip_color : source_color = vec4(0.0, 0.0, 0.0, 0.45);
/// Metres between neighbouring holes, and holes along each side of the square grid, centred on the trap.
uniform float spacing = 1.0;
uniform int holes_per_side = 3;
uniform float radius = 0.17;
/// Width of the darkened lip around each hole.
uniform float lip = 0.07;
uniform float reveal : hint_range(0.0, 1.0) = 1.0;
uniform vec3 reveal_glow : source_color = vec3(0.0);

varying vec2 ground;

void vertex() {
	ground = VERTEX.xz;
	POSITION = ground_overlay_position(MODELVIEW_MATRIX, PROJECTION_MATRIX, VERTEX);
}

/// Distance to an octagon's edge, the floor tiles' shape, rather than a circle's.
float octagon(vec2 p) {
	p = abs(p);
	return max(max(p.x, p.y), (p.x + p.y) * 0.7071);
}

void fragment() {
	float half_count = float(holes_per_side - 1) * 0.5;
	vec2 nearest = clamp(round(ground / spacing + half_count) - half_count, vec2(-half_count), vec2(half_count));
	float d = octagon(ground - nearest * spacing);
	float hole = 1.0 - smoothstep(radius - 0.012, radius, d);
	float rim = (1.0 - smoothstep(radius, radius + lip, d)) * (1.0 - hole);
	float alpha = max(hole * hole_color.a, rim * lip_color.a);
	ALBEDO = mix(lip_color.rgb, hole_color.rgb, hole) + reveal_glow;
	ALPHA = alpha * reveal;
}
