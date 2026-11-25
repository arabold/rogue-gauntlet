// The floor's top-down sample grid, published by FloorDiscovery as global shader uniforms (declared in project.godot):
// exploration (fog_of_war.gdshaderinc) and the wall mask (wall_clip.gdshaderinc) share it.
#ifndef FLOOR_MAP_GDSHADERINC
#define FLOOR_MAP_GDSHADERINC
global uniform vec2 fog_map_origin;
// Zero while no floor is loaded, which leaves everything uncovered and unclipped.
global uniform vec2 fog_map_extent;
// Solid walls and closed doors on the same grid (FloorDiscovery's WallMask): 1 inside one, 0 elsewhere.
global uniform sampler2D floor_wall_mask;

// Whether a world position lies inside a solid wall or closed door sample; false while no floor is loaded.
bool floor_wall_at(vec2 world_xz) {
	if (fog_map_extent.x <= 0.0) return false;
	vec2 uv = (world_xz - fog_map_origin) / fog_map_extent;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0)))) return false;
	ivec2 size = textureSize(floor_wall_mask, 0);
	return texelFetch(floor_wall_mask, min(ivec2(uv * vec2(size)), size - 1), 0).r > 0.5;
}
#endif
