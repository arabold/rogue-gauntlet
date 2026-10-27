shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled;
uniform float amount : hint_range(0,1) = 0;
uniform vec4 base_color : source_color = vec4(1);
uniform sampler2D source_texture : source_color, hint_default_white;
uniform float source_roughness = 0.7;
uniform float source_metallic = 0;
uniform float source_specular = 0.5;
uniform sampler2D source_normal : hint_normal;
uniform bool source_normal_enabled = false;
uniform float source_normal_scale = 1;
uniform vec3 source_uv_scale = vec3(1);
uniform vec3 source_uv_offset = vec3(0);
uniform float source_alpha_scissor = 0;
uniform int source_cull = 0;
uniform vec4 light_color : source_color = vec4(0.25,0.5,1.0,1.0);
void fragment() {
 if ((source_cull == 0 && !FRONT_FACING) || (source_cull == 1 && FRONT_FACING)) discard;
 vec2 uv=UV*source_uv_scale.xy+source_uv_offset.xy;
 vec4 original=texture(source_texture,uv)*base_color;
 if(original.a<source_alpha_scissor) discard;
 float glow=smoothstep(0.0,0.45,amount);
 ALBEDO=mix(original.rgb,light_color.rgb,glow);
 EMISSION=mix(vec3(0),light_color.rgb*3.0,glow);
 // Match the original PBR response at zero glow so restoration has no lighting pop.
 ROUGHNESS=mix(source_roughness,0.8,glow);
 METALLIC=source_metallic*(1.0-glow);
 SPECULAR=source_specular;
 if(source_normal_enabled) { NORMAL_MAP=texture(source_normal,uv).rgb; NORMAL_MAP_DEPTH=source_normal_scale; }
 ALPHA=original.a*(1.0-smoothstep(0.25,1.0,amount));
}
