shader_type spatial;

// Per-run disguise tint for unidentified items. Instead of replacing the model's
// material with a flat colour (which erases all surface detail), this keeps the
// original albedo texture's light/dark variation and recolours it: the texture's
// luminance drives brightness while the tint uniform supplies the hue. The result
// reads as the same bottle filled with a differently coloured liquid, and still
// receives scene lighting normally. See docs/item-identification-system.md.

uniform sampler2D albedo_tex : source_color;
uniform vec4 tint : source_color = vec4(1.0);
// Brightness floor so dark texels (cork, shadows) keep a hint of the tint.
uniform float floor_value : hint_range(0.0, 1.0) = 0.35;
// How strongly the texture's own luminance shapes the result.
uniform float detail : hint_range(0.0, 1.0) = 0.65;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.6;

void fragment() {
	vec4 base = texture(albedo_tex, UV);
	float lum = dot(base.rgb, vec3(0.299, 0.587, 0.114));
	ALBEDO = tint.rgb * (floor_value + detail * lum);
	ROUGHNESS = roughness_value;
}
