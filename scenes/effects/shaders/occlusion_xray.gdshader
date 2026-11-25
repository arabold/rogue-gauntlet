shader_type spatial;
// See-through-wall silhouette for an actor (monster/loot). Drawn on top of everything, but the
// stencil written by the companion mask (occlusion_xray_mask.gdshader) makes it appear ONLY where
// the actor is hidden (stencil != 1). So an actor in plain view shows nothing, and — because the
// gate is per-pixel — its own body parts never x-ray through each other. A hidden pixel shows only
// when what hides it is a wall, a closed door or fogged ground (xray_occlusion.gdshaderinc): a
// projectile, puddle, prop or the floor in front of the actor is no reason to see through.
// Alpha fades with distance so far actors stay quiet.
// This is a visibility indicator with its own distance fade, independent of atmospheric fog.
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, fog_disabled;
stencil_mode read, compare_not_equal, 1;

#include "res://scenes/levels/xray_occlusion.gdshaderinc"

// Nearest filtering: mipmapped/linear sampling of depth returns averaged values and breaks the comparison.
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec4 xray_color : source_color = vec4(1.0, 0.35, 0.35, 0.85);
uniform float fade_near = 16.0;
uniform float fade_far = 34.0;
// Absorbs the depth buffer holding this actor's own surface where it is in view.
uniform float occlusion_bias = 0.0005;
// Per-helper state; actor ground clipping never changes another actor's shared shader/material.
instance uniform float xray_ground_height = -1e30;

void fragment() {
	if ((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).y < xray_ground_height) discard;
	// Godot uses a reversed-Z depth buffer, so nearer geometry has a LARGER value. Only a surface nearer than this
	// fragment can hide it, and that surface decides whether the hero should see through.
	float scene_depth = texture(depth_texture, SCREEN_UV).x;
	if (scene_depth <= FRAGCOORD.z + occlusion_bias) discard;
	vec3 occluder = fog_surface_position(SCREEN_UV, scene_depth, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
	float hidden = xray_occluder_hides(occluder, CAMERA_POSITION_WORLD);
	float fade = 1.0 - smoothstep(fade_near, fade_far, length(VERTEX));
	if (hidden * fade <= 0.0) {
		discard;
	}
	ALBEDO = xray_color.rgb;
	ALPHA = xray_color.a * fade * hidden;
}
