// World markers (lock gems, the interaction "!") light themselves. A fixed key light in view space shades their
// faces in toon bands: shadow, lit and a narrow highlight that blooms slightly. They read as solid objects the
// same way in a torch-lit hall and in the dark, and torches, the hero's light and ambient light never wash them
// out. The light turns with the camera, so the lit side stays on screen when the camera rotates.
// world_marker.gdshader is the opaque, depth-tested variant; world_marker_overlay.gdshader defines
// WORLD_MARKER_OVERLAY to draw in the transparent pass, above the fog.

// Per marker, so markers of different colors share one material (a door's gem takes its key's color).
instance uniform vec3 base_color : source_color = vec3(0.95, 0.68, 0.14);
// View-space direction toward the key light: from the upper left and the viewer's side. Under the game camera an
// upright face toward the camera is half lit, tops and left sides catch the light, and right sides fall into shadow.
uniform vec3 light_direction = vec3(-0.62, 0.18, 0.76);
// Brightness of faces turned away from the key light; faces between shadow and full light grade per face, so each
// facet of a gem keeps its own tone.
uniform float shadow : hint_range(0.0, 1.0) = 0.42;
// How far faces facing the key light head-on brighten toward white.
uniform float highlight : hint_range(0.0, 1.0) = 0.25;
// Brightness of the highlight band; just above the environment's glow threshold, it blooms faintly.
uniform float highlight_energy : hint_range(1.0, 3.0) = 1.3;

void fragment() {
	float lambert = dot(normalize(NORMAL), normalize(light_direction));
	vec3 color = base_color * mix(shadow, 1.0, smoothstep(-0.15, 0.7, lambert));
	float shine = smoothstep(0.7, 0.74, lambert);
	ALBEDO = mix(color, mix(base_color, vec3(1.0), highlight) * highlight_energy, shine);
#ifdef WORLD_MARKER_OVERLAY
	ALPHA = 1.0;
#endif
}
