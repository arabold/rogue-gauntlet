shader_type spatial;
// Invisible additive pass writes the nearest actor depth before any fade colors are drawn.
render_mode unshaded, blend_add, depth_draw_always, cull_disabled, shadows_disabled;
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"
uniform float amount = 0;
void fragment() {
 if(amount>=0.999) discard;
 if (source_is_culled(FRONT_FACING)) discard;
 vec4 original=source_albedo(source_uv(UV));
 if(source_is_cutout(original.a)) discard;
 ALBEDO=vec3(0);
 ALPHA=1.0;
}
