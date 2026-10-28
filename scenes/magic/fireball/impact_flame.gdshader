shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"

// A brief hot flash blooms into broken, cooling tongues of flame.
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform bool ground_wave = false;
uniform vec4 inner_color : source_color = vec4(1.0, 0.72, 0.12, 1.0);
uniform vec4 outer_color : source_color = vec4(1.0, 0.08, 0.006, 1.0);
uniform float brightness = 1.6;

float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
		mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
void vertex() {
	float grow = mix(0.22, 1.0, 1.0 - pow(1.0 - progress, 3.0));
	VERTEX *= grow;
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float t = progress;
	vec2 p = (UV - 0.5) * 2.0;
	float turbulence = noise2(UV * 5.0 + vec2(t * 1.4, -t * 2.1));
	turbulence = turbulence * 0.7 + noise2(UV * 11.0 + vec2(-t * 1.8, t)) * 0.3;
	float fade = (1.0 - smoothstep(0.36, 1.0, t)) * smoothstep(0.0, 0.08, t + 0.03);
	float mask;
	float heat;
	if (ground_wave) {
		float radius = length(p);
		float broken_edge = radius + (turbulence - 0.5) * 0.24;
		float ring = exp(-pow((broken_edge - 0.73) / 0.14, 2.0));
		float center = (1.0 - smoothstep(0.0, 0.8, radius)) * (1.0 - smoothstep(0.05, 0.32, t));
		mask = (ring * smoothstep(0.22, 0.63, turbulence) + center * 0.55) * fade;
		heat = clamp(ring * 0.35 + center, 0.0, 1.0);
	} else {
		float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.5);
		float flames = smoothstep(0.48 + t * 0.1, 0.76, turbulence);
		mask = flames * (0.26 + rim * 0.25) * fade;
		heat = smoothstep(0.38, 0.72, turbulence) * (1.0 - t * 0.65);
	}
	vec3 color = mix(outer_color.rgb, inner_color.rgb, heat);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = clamp(mask * 0.75, 0.0, 1.0);
}
