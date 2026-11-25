// What may hide an x-rayed object (a monster, loot, a chest or a door) from the hero: a wall or closed door, or ground
// the fog of war still covers. Anything else in front of it, such as a projectile, a puddle, a prop, the floor under a
// sunken foot or another actor, is not a reason to see through: the object is simply partly in view.
// The x-ray shaders call this only where something nearer than the object fills the depth buffer; that nearer surface
// is the occluder.
#ifndef XRAY_OCCLUSION_GDSHADERINC
#define XRAY_OCCLUSION_GDSHADERINC
#include "res://scenes/levels/fog_of_war.gdshaderinc"

// How far past the occluding surface, away from the camera, it is sampled: a wall's face lies on a sample boundary, so
// the probe steps inside the wall it belongs to.
const float XRAY_OCCLUDER_PROBE = 0.2;

// 1 when the occluder at world position `occluder` (seen from `camera`) is a wall, closed door or fogged surface, 0 when
// it is open, explored floor and what stands on it. Without a loaded floor (harnesses, previews) every occluder hides.
float xray_occluder_hides(vec3 occluder, vec3 camera) {
	if (fog_map_extent.x <= 0.0) return 1.0;
	vec3 probe = occluder + normalize(occluder - camera) * XRAY_OCCLUDER_PROBE;
	return floor_wall_at(probe.xz) ? 1.0 : fog_unexplored(probe.xz);
}
#endif
