shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform sampler2D spark_texture : source_color, filter_linear;
void vertex() {
	mat4 billboard = mat4(normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	vec4 spark = texture(spark_texture, UV) * COLOR;
	ALBEDO = spark.rgb;
	EMISSION = vec3(1.0, 0.35, 0.03) * 1.5;
	ALPHA = spark.a;
}
