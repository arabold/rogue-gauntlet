shader_type spatial;
// World marker drawn in the transparent pass without a depth test; see world_marker.gdshaderinc. It stays out of the
// depth buffer, so the fog of war never shades it, and a render priority above the fog draws it on top. Convex
// meshes only: without a depth test, back-face culling alone keeps the far faces hidden.
render_mode unshaded, depth_test_disabled, depth_draw_never;
#define WORLD_MARKER_OVERLAY
#include "res://scenes/effects/shaders/world_marker.gdshaderinc"
