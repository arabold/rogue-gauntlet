shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"

// UV.x runs from the travelling head to the fading tail.
uniform sampler2D noise_texture : repeat_enable, filter_linear;
uniform vec4 ember_color : source_color = vec4(1.0, 0.08, 0.008, 1.0);
uniform vec4 flame_color : source_color = vec4(1.0, 0.32, 0.012, 1.0);
uniform vec4 core_color : source_color = vec4(1.0, 0.82, 0.22, 1.0);
uniform float brightness = 1.3;
uniform float core_strength = 0.45;
uniform float opacity = 0.8;
uniform float effect_fade = 1.0;
uniform float animation_speed = 1.0;
uniform bool closed_loop = false;

void vertex() {
	float t = TIME * animation_speed;
	if (closed_loop) {
		float angle = UV.x * 6.2831853;
		float flicker = 0.5 + 0.5 * sin(angle * 13.0 - t * 8.0 + sin(angle * 7.0 + t * 3.0) * 1.4);
		VERTEX.y += max(VERTEX.y, 0.0) * flicker * 1.5;
	}
}

void fragment() {
	float t = TIME * animation_speed;
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
	float along = UV.x;
	float across = abs(UV.y * 2.0 - 1.0);
	float fade = closed_loop ? 1.0 : pow(1.0 - along, 1.25) * smoothstep(0.0, 0.06, along);
	vec2 flow = vec2(along * 2.8 + t * 1.8, UV.y * 1.5);
	vec2 ring_flow = vec2(cos(along * 6.2831853), sin(along * 6.2831853)) * 2.4 + vec2(t * 0.25, -t * 0.4) + UV.y;
	float n = texture(noise_texture, closed_loop ? ring_flow : flow).r;
	float detail = texture(noise_texture, closed_loop ? ring_flow * 1.9 : flow * 1.9 + vec2(-t * 0.8, 0.37)).g;
	float tongues = smoothstep(0.18, 0.72, n * 0.7 + detail * 0.3 + (1.0 - across) * 0.25);
	float edge = 1.0 - smoothstep(0.28 + n * 0.12, 0.86, across);
	float heat = pow(1.0 - across, 2.4) * smoothstep(0.2, 0.75, n + 0.25);
	float filament = pow(max(0.0, sin(UV.y * 30.0 + n * 2.0 - along * 9.0 + t * 7.0)), 12.0);
	vec3 color = mix(ember_color.rgb, flame_color.rgb, heat);
	color = mix(color, core_color.rgb, clamp(heat * core_strength + filament * 0.18, 0.0, 0.75));
	float mask = fade * edge * (closed_loop ? smoothstep(0.28, 0.72, n * 0.7 + detail * 0.3) : tongues);
	ALBEDO = color;
	EMISSION = color * brightness;
	ALPHA = clamp(mask * opacity * effect_fade, 0.0, 1.0);
}
