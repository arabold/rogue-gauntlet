shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	ALBEDO = COLOR.rgb;
	EMISSION = COLOR.rgb * 2.0;
	ALPHA = COLOR.a;
}
