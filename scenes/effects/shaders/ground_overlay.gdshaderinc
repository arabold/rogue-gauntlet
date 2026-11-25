// Flat markers drawn on the floor (attack warnings, lob landing rings) must stay readable on every floor. A dungeon
// floor's visible surface is not its plane: tiles top out a few centimetres above it, rocky and decorated ones higher,
// and rugs, inlays and stairs rise further. A marker at any fixed small height sinks into some of them. Instead, keep
// the marker on the floor plane and pull only its depth toward the camera by GROUND_OVERLAY_RELIEF: it then draws over
// any floor relief while walls, props and bodies standing in front of it still hide it. Screen coverage is unchanged.
#ifndef GROUND_OVERLAY_GDSHADERINC
#define GROUND_OVERLAY_GDSHADERINC

// More than the tallest floor relief a marker lies on; low enough that a body standing on the marker still covers it.
const float GROUND_OVERLAY_RELIEF = 0.25;

// The clip position of a floor marker's vertex, with its depth pulled toward the camera. Call it from vertex():
// POSITION = ground_overlay_position(MODELVIEW_MATRIX, PROJECTION_MATRIX, VERTEX);
vec4 ground_overlay_position(mat4 modelview, mat4 projection, vec3 vertex) {
	vec4 view = modelview * vec4(vertex, 1.0);
	vec4 position = projection * view;
	view.z += GROUND_OVERLAY_RELIEF;
	vec4 nearer = projection * view;
	position.z = nearer.z / nearer.w * position.w;
	return position;
}
#endif
