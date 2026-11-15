shader_type canvas_item;
// Draws an item icon (a model rendered on a transparent viewport) with a soft dark halo around its silhouette, so it
// stands off whatever lies behind it: a slot's niche, the lit gold of a worn slot, or the game itself.

/** The halo's color; its alpha is the halo's strength right at the silhouette. */
uniform vec4 halo_color : source_color = vec4(0.04, 0.025, 0.015, 0.9);
/** How far the halo reaches, in pixels of the icon's texture. */
uniform float halo_radius : hint_range(0.0, 32.0) = 11.0;

const int TAPS = 12;
const int RINGS = 3;

// The node's and its parents' modulate: writing COLOR below would otherwise drop it, and slots dim through it.
varying vec4 tint;

void vertex() {
	tint = COLOR;
}

void fragment() {
	vec4 item = texture(TEXTURE, UV);
	// The silhouette spread outward: its coverage sampled on rings around the pixel.
	float spread = 0.0;
	for (int ring = 1; ring <= RINGS; ring++) {
		vec2 reach = halo_radius * float(ring) / float(RINGS) * TEXTURE_PIXEL_SIZE;
		for (int tap = 0; tap < TAPS; tap++) {
			float angle = TAU * (float(tap) + 0.5 * float(ring)) / float(TAPS);
			spread += texture(TEXTURE, UV + vec2(cos(angle), sin(angle)) * reach).a;
		}
	}
	float halo = smoothstep(0.0, 0.6, spread / float(TAPS * RINGS)) * halo_color.a;
	float alpha = item.a + halo * (1.0 - item.a);
	COLOR = vec4(mix(halo_color.rgb, item.rgb, item.a / max(alpha, 0.0001)), alpha) * tint;
}
