shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://scenes/magic/area_visibility.gdshaderinc"
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float hit_flash = 0.0;
uniform int shape = 0; // 0: flame column, 1: protective shell
uniform vec4 flame_color : source_color = vec4(1.0, 0.16, 0.01, 1.0);
uniform vec4 hot_color : source_color = vec4(1.0, 0.72, 0.14, 1.0);
uniform float brightness = 1.5;
uniform float opacity = 0.7;
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise2(vec2 p) {
 vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
 return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
  mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
void vertex() {
 if (shape == 0) VERTEX.xz *= mix(0.65, 1.0, smoothstep(0.0, 0.18, progress));
}
void fragment() {
	if (area_is_blocked(VERTEX, INV_VIEW_MATRIX)) discard;
 float fade = smoothstep(0.0, 0.09, progress + 0.025) * (1.0 - smoothstep(0.7, 1.0, progress));
 vec2 uv = UV;
 float n = noise2(uv * vec2(7.0, 3.0) + vec2(TIME * 0.35, -TIME * 1.5));
 float fine = noise2(uv * vec2(15.0, 7.0) + vec2(-TIME * 0.5, -TIME * 2.0));
 float heat = n * 0.7 + fine * 0.3;
 float mask;
 if (shape == 0) {
  float tip = 1.0 - smoothstep(0.55, 1.0, uv.y + (n - 0.5) * 0.45);
  float base = smoothstep(0.0, 0.08, uv.y);
  float streaks = pow(max(0.0, sin(uv.x * 45.0 + n * 5.0 - TIME * 5.0)), 8.0);
  mask = (smoothstep(0.3, 0.75, heat) * 0.6 + streaks * 0.25) * tip * base;
 } else {
  float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.6);
  float bands = pow(max(0.0, sin(uv.y * 34.0 + n * 5.0 - TIME * 3.0)), 12.0);
  mask = rim * (0.22 + smoothstep(0.3, 0.7, heat) * 0.42) + bands * rim * 0.2 + hit_flash * rim * 0.9;
 }
 vec3 color = mix(flame_color.rgb, hot_color.rgb, smoothstep(0.35, 0.78, heat));
 ALBEDO = color; EMISSION = color * (brightness + hit_flash * 1.2); ALPHA = clamp(mask * opacity * fade, 0.0, 1.0);
}
