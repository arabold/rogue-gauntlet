shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
// Blended variant of flame_wake for ribbons that form a spell's main body: with crisp edges and heat
// bands it keeps its hue on bright floors, where the additive ribbon washes out.
#include "res://scenes/magic/fireball/flame_wake.gdshaderinc"
