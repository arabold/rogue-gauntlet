shader_type spatial;
// Wall-clipped surfaces whose source uses depth-prepass alpha; see wall_clipped.gdshaderinc.
render_mode depth_prepass_alpha, cull_disabled;
#define WALL_CLIPPED_ALPHA
#include "res://scenes/effects/shaders/wall_clipped.gdshaderinc"
