shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
#include "res://scenes/magic/fireball/toon_fire.gdshaderinc"

// Stylized flame and smoke cards using alpha erosion: the particle colour sets the hue band and
// its alpha how far the noise-broken blob has eroded, so cards burn away with crisp edges
// instead of fading softly. A hotter inner tone gives flames a two-tone toon read; it is withheld
// from dark, smoke-coloured cards so cooled flames read as smoke. The blob always reaches zero
// inside the card border.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform vec4 inner_color : source_color = vec4(1.0, 0.86, 0.34, 1.0);
uniform float inner_strength = 1.0;
uniform float inner_threshold = 0.5;
uniform float brightness = 1.6;
uniform float edge_softness = 0.06;
uniform float noise_scale = 0.75;
uniform float scroll_speed = 1.4;
varying float seed;

void vertex() {
	seed = INSTANCE_CUSTOM.x;
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	billboard *= mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0),
		vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}

void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float radius = length((UV - 0.5) * 2.0);
	float n = texture(noise_texture, UV * noise_scale + vec2(seed * 0.37, seed * 0.11 - TIME * scroll_speed)).r;
	float shape = ((1.0 - radius) * 1.2 + (n - 0.5) * 0.9) * (1.0 - smoothstep(0.8, 1.0, radius));
	float eroded = shape - (1.0 - COLOR.a);
	float glow = smoothstep(0.3, 0.5, max(COLOR.r, COLOR.g));
	float inner = smoothstep(inner_threshold, inner_threshold + edge_softness, eroded) * inner_strength * glow;
	vec3 color = mix(toon_srgb_to_linear(COLOR.rgb), inner_color.rgb, inner);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = smoothstep(0.0, edge_softness, eroded);
}
