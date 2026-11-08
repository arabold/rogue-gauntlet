// An authored StandardMaterial3D rebuilt on the source contract, so it renders the same but stops at walls
// (wall_clip.gdshaderinc). WallClippedMaterial copies the parameters and lists the supported features.
// wall_clipped.gdshader is the opaque variant; wall_clipped_alpha.gdshader defines WALL_CLIPPED_ALPHA for blending.
#define SOURCE_TEXTURE_HINTS source_color, hint_default_white, filter_linear_mipmap, repeat_enable
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"

uniform bool source_vertex_color = false;
// False samples only the base level, as a texture filter without mipmaps does.
uniform bool source_mipmaps = true;
// False clamps to the edge texels, as a texture without repeat does.
uniform bool source_repeat = true;
uniform vec4 source_emission : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// Zero while the source has emission disabled.
uniform float source_emission_energy = 0.0;
uniform sampler2D source_emission_texture : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool source_emission_multiply = false;

vec4 wall_clipped_sample(sampler2D tex, vec2 uv) {
	if (!source_repeat) {
		vec2 half_texel = 0.5 / vec2(textureSize(tex, 0));
		uv = clamp(uv, half_texel, 1.0 - half_texel);
	}
	return source_mipmaps ? texture(tex, uv) : textureLod(tex, uv, 0.0);
}

void vertex() {
	UV = source_uv(UV);
}

void fragment() {
	if (source_is_hidden(FRONT_FACING, VERTEX, INV_VIEW_MATRIX)) discard;
	vec4 albedo = wall_clipped_sample(source_texture, UV) * base_color;
	if (source_vertex_color) albedo *= COLOR;
	if (source_is_cutout(albedo.a)) discard;
	ALBEDO = albedo.rgb;
	ROUGHNESS = source_roughness;
	METALLIC = source_metallic;
	SPECULAR = source_specular;
	if (source_emission_energy > 0.0) {
		vec3 emission_texel = wall_clipped_sample(source_emission_texture, UV).rgb;
		vec3 emission = source_emission_multiply ? source_emission.rgb * emission_texel : source_emission.rgb + emission_texel;
		EMISSION = emission * source_emission_energy;
	}
#ifdef WALL_CLIPPED_ALPHA
	ALPHA = albedo.a;
#endif
}
