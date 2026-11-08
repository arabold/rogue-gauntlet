// Source material contract shared by cloak, skins, both teleport passes and wall-clipped surfaces.
// Render modes and effect-specific PBR blending stay in the consuming shader.
#include "res://scenes/levels/wall_clip.gdshaderinc"
#ifndef SOURCE_TEXTURE_HINTS
#define SOURCE_TEXTURE_HINTS source_color, hint_default_white
#endif
uniform vec4 base_color : source_color = vec4(1);
uniform sampler2D source_texture : SOURCE_TEXTURE_HINTS;
uniform vec3 source_uv_scale = vec3(1);
uniform vec3 source_uv_offset = vec3(0);
uniform float source_alpha_scissor = 0;
uniform int source_cull = 0;
uniform float source_roughness = 0.7;
uniform float source_metallic = 0;
uniform float source_specular = 0.5;
uniform sampler2D source_normal : hint_normal;
uniform bool source_normal_enabled = false;
uniform float source_normal_scale = 1;

vec2 source_uv(vec2 mesh_uv) {
 return mesh_uv * source_uv_scale.xy + source_uv_offset.xy;
}
vec4 source_albedo(vec2 uv) {
 return texture(source_texture, uv) * base_color;
}
// Whether to discard a fragment: a face the source culls, or, on a wall-clipped mesh, a part beyond a wall
// (wall_clip.gdshaderinc). Pass FRONT_FACING, VERTEX and INV_VIEW_MATRIX.
bool source_is_hidden(bool front_facing, vec3 vertex, mat4 inv_view) {
 return (source_cull == 0 && !front_facing) || (source_cull == 1 && front_facing)
  || wall_clip_beyond_wall((inv_view * vec4(vertex, 1.0)).xz);
}
bool source_is_cutout(float alpha) {
 return alpha < source_alpha_scissor;
}
vec3 source_normal_map(vec2 uv) {
 return texture(source_normal, uv).rgb;
}
