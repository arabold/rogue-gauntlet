shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
// Additive toon ribbon for hot inner trails that should glow over the blended body.
#include "res://scenes/magic/shared/toon/toon_ribbon.gdshaderinc"
