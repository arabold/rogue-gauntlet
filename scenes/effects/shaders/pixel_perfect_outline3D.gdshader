shader_type spatial;
render_mode cull_front, unshaded;
#include "res://scenes/effects/shaders/upright_billboard.gdshaderinc"

uniform vec4 outline_color : source_color;
uniform float outline_width = 1.0;
uniform bool upright_billboard = false;

void vertex() {
	mat4 model_view = upright_billboard
		? VIEW_MATRIX * upright_billboard_transform(MODEL_MATRIX, INV_VIEW_MATRIX)
		: MODELVIEW_MATRIX;
	vec4 clip_position = PROJECTION_MATRIX * (model_view * vec4(VERTEX, 1.0));
	vec3 clip_normal = mat3(PROJECTION_MATRIX) * (mat3(model_view) * NORMAL);
	
	vec2 offset = normalize(clip_normal.xy) / VIEWPORT_SIZE * clip_position.w * outline_width * 2.0;
	
	clip_position.xy += offset;
	
	POSITION = clip_position;
}

void fragment() {
	ALBEDO = outline_color.rgb;
	if (outline_color.a < 1.0) {
		ALPHA = outline_color.a;
	}
}
