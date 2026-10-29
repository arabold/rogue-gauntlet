shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
// Blended toon ribbon for a spell's main body; it keeps its hue on bright floors, where the additive
// toon_ribbon_glow washes out.
#include "res://scenes/magic/shared/toon/toon_ribbon.gdshaderinc"
