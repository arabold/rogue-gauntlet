// An actor's visuals stop at walls. FloorDiscovery publishes the floor's WallMask as a global, and the actor's
// WallClipComponent sets its center as each mesh's wall_clip_anchor. A fragment is beyond a wall once the straight
// top-down path from the anchor to it enters a wall sample, so it may sink into a wall but never show on its far side.
#ifndef WALL_CLIP_GDSHADERINC
#define WALL_CLIP_GDSHADERINC
#include "res://scenes/levels/floor_map.gdshaderinc"

global uniform sampler2D floor_wall_mask;
// Meshes without an anchor (actors without a WallClipComponent) never clip.
instance uniform vec2 wall_clip_anchor = vec2(1e30);

// Samples are half a unit, so this covers a held weapon's reach of about four units even diagonally.
const int WALL_CLIP_MAX_STEPS = 16;

// Walks every mask sample the path crosses (a grid traversal, so even a one-sample wall cannot be skipped). The
// anchor's own sample is never tested: the actor's center always stands clear of walls.
bool wall_clip_beyond_wall(vec2 world_xz) {
	if (fog_map_extent.x <= 0.0 || wall_clip_anchor.x > 1e29) return false;
	ivec2 size = textureSize(floor_wall_mask, 0);
	vec2 scale = vec2(size) / fog_map_extent;
	vec2 from = (wall_clip_anchor - fog_map_origin) * scale;
	vec2 delta = (world_xz - fog_map_origin) * scale - from;
	ivec2 cell = ivec2(floor(from));
	ivec2 dir = ivec2(sign(delta));
	// Path parameter per sample step along each axis, and to the first sample boundary.
	vec2 step_t = 1.0 / max(abs(delta), vec2(1e-6));
	vec2 next_t = vec2(
		dir.x == 0 ? 2.0 : (dir.x > 0 ? float(cell.x + 1) - from.x : from.x - float(cell.x)) * step_t.x,
		dir.y == 0 ? 2.0 : (dir.y > 0 ? float(cell.y + 1) - from.y : from.y - float(cell.y)) * step_t.y);
	for (int i = 0; i < WALL_CLIP_MAX_STEPS; i++) {
		if (min(next_t.x, next_t.y) > 1.0) return false;
		if (next_t.x < next_t.y) {
			cell.x += dir.x;
			next_t.x += step_t.x;
		} else {
			cell.y += dir.y;
			next_t.y += step_t.y;
		}
		if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, size))) return false;
		if (texelFetch(floor_wall_mask, cell, 0).r > 0.5) return true;
	}
	return false;
}
#endif
