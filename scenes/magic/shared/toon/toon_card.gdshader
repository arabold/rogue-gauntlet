shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/shared/toon/toon.gdshaderinc"

// Stylized particle cards (flame, smoke, mist, dust) using alpha erosion: the particle colour sets the hue band and
// its alpha how far the noise-broken blob has eroded, so cards burn away with crisp edges
// instead of fading softly. A hotter inner tone gives flames a two-tone toon read; it is withheld
// from dark, smoke-coloured cards so cooled flames read as smoke.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform vec4 inner_color : source_color = vec4(1.0, 0.86, 0.34, 1.0);
uniform float inner_threshold = 0.5;
uniform float brightness = 1.6;
uniform float edge_softness = 0.06;
uniform float noise_scale = 0.75;
uniform float scroll_speed = 1.4;
uniform float effect_fade = 1.0;
varying float seed;
varying vec3 base_color;
varying float glow;

void vertex() {
	seed = INSTANCE_CUSTOM.x;
	base_color = toon_srgb_to_linear(COLOR.rgb);
	glow = smoothstep(0.3, 0.5, max(COLOR.r, COLOR.g));
	MODELVIEW_MATRIX = VIEW_MATRIX * toon_billboard(MODEL_MATRIX, INV_VIEW_MATRIX, seed);
}

void fragment() {
	float radius = length((UV - 0.5) * 2.0);
	if (radius >= 1.0) discard;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float n = texture(noise_texture, UV * noise_scale + vec2(seed * 0.37, seed * 0.11 - TIME * scroll_speed)).r;
	float shape = ((1.0 - radius) * 1.2 + (n - 0.5) * 0.9) * (1.0 - smoothstep(0.8, 1.0, radius));
	float eroded = shape - (1.0 - COLOR.a * effect_fade);
	if (eroded <= 0.0) discard;
	float inner = smoothstep(inner_threshold, inner_threshold + edge_softness, eroded) * glow;
	vec3 color = mix(base_color, inner_color.rgb, inner);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, edge_softness, eroded);
}
