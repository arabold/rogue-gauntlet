shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled;
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"
uniform float amount : hint_range(0,1) = 0;
uniform vec4 light_color : source_color = vec4(0.25,0.5,1.0,1.0);
void fragment() {
 if (source_is_culled(FRONT_FACING)) discard;
 vec2 uv=source_uv(UV);
 vec4 original=source_albedo(uv);
 if(source_is_cutout(original.a)) discard;
 float glow=smoothstep(0.0,0.45,amount);
 ALBEDO=mix(original.rgb,light_color.rgb,glow);
 EMISSION=mix(vec3(0),light_color.rgb*3.0,glow);
 // Match the original PBR response at zero glow so restoration has no lighting pop.
 ROUGHNESS=mix(source_roughness,0.8,glow);
 METALLIC=source_metallic*(1.0-glow);
 SPECULAR=source_specular;
 if(source_normal_enabled) { NORMAL_MAP=source_normal_map(uv); NORMAL_MAP_DEPTH=source_normal_scale; }
 ALPHA=original.a*(1.0-smoothstep(0.25,1.0,amount));
}
