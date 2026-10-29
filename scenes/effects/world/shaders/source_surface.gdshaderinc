// Source material contract shared by cloak, skins and both teleport passes.
// Render modes and effect-specific PBR blending stay in the consuming shader.
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
bool source_is_culled(bool front_facing) {
 return (source_cull == 0 && !front_facing) || (source_cull == 1 && front_facing);
}
bool source_is_cutout(float alpha) {
 return alpha < source_alpha_scissor;
}
vec3 source_normal_map(vec2 uv) {
 return texture(source_normal, uv).rgb;
}
