// A spatially varying screen blur: a clear ellipse for play, with a smooth falloff on every side.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform float intensity : hint_range(0.0, 1.0) = 1.0;
uniform vec2 focus_uv = vec2(0.5, 0.48);
// Semi-axes of the completely sharp ellipse, as fractions of viewport width and height.
uniform vec2 clear_radius_uv = vec2(0.26, 0.3);
// Transition width relative to that ellipse; 0.65 reaches full blur at 1.65 times its radii.
uniform float falloff : hint_range(0.05, 2.0, 0.05) = 0.65;
// Approximate maximum filter width in render pixels. Godot supplies the Gaussian mip chain.
uniform float blur_pixels : hint_range(0.0, 32.0, 0.5) = 2.0;

void fragment() {
	vec2 radius = max(abs(clear_radius_uv), vec2(0.001));
	float distance_from_focus = length((SCREEN_UV - focus_uv) / radius);
	float softness = smoothstep(1.0, 1.0 + max(falloff, 0.001), distance_from_focus);
	float lod = softness * intensity * log2(max(blur_pixels, 1.0));
	COLOR = vec4(textureLod(screen_texture, SCREEN_UV, lod).rgb, 1.0);
}
