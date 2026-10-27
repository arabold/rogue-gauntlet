shader_type spatial;
// Invisible additive pass writes the nearest actor depth before any fade colors are drawn.
render_mode unshaded, blend_add, depth_draw_always, cull_disabled, shadows_disabled;
uniform sampler2D source_texture : source_color, hint_default_white;
uniform vec4 base_color : source_color = vec4(1);
uniform vec3 source_uv_scale = vec3(1);
uniform vec3 source_uv_offset = vec3(0);
uniform float source_alpha_scissor = 0;
uniform int source_cull = 0;
uniform float amount = 0;
void fragment() {
 if(amount>=0.999) discard;
 if ((source_cull == 0 && !FRONT_FACING) || (source_cull == 1 && FRONT_FACING)) discard;
 vec4 original=texture(source_texture,UV*source_uv_scale.xy+source_uv_offset.xy)*base_color;
 if(original.a<source_alpha_scissor) discard;
 ALBEDO=vec3(0);
 ALPHA=1.0;
}
