// The floor's top-down sample grid, published by FloorDiscovery as global shader uniforms (declared in project.godot):
// exploration (fog_of_war.gdshaderinc) and the wall mask (wall_clip.gdshaderinc) share it.
#ifndef FLOOR_MAP_GDSHADERINC
#define FLOOR_MAP_GDSHADERINC
global uniform vec2 fog_map_origin;
// Zero while no floor is loaded, which leaves everything uncovered and unclipped.
global uniform vec2 fog_map_extent;
#endif
