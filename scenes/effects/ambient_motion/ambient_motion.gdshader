shader_type spatial;
render_mode cull_disabled;

#define SOURCE_TEXTURE_HINTS source_color, filter_nearest, repeat_enable
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"

uniform mat4 mesh_to_target;
uniform mat4 target_to_mesh;
uniform vec3 motion_anchor;
uniform vec3 free_axis = vec3(0.0, -1.0, 0.0);
uniform vec3 support_normal = vec3(0.0);
uniform float free_span = 1.0;
uniform float pinned_fraction = 0.18;
uniform float bend_amplitude = 0.25;
uniform float flutter_amplitude = 0.035;
uniform float flutter_frequency = 1.15;
uniform vec3 motion_wind;
uniform float motion_time = 0.0;
uniform float motion_phase = 0.0;
uniform bool source_vertex_color = false;

vec3 deform(vec3 point) {
	vec3 p = (mesh_to_target * vec4(point, 1.0)).xyz;
	float distance = max(dot(p - motion_anchor, free_axis), 0.0);
	float u = clamp(distance / max(free_span, 0.001), 0.0, 1.0);
	float weight = smoothstep(pinned_fraction, 1.0, u);
	vec3 wind = motion_wind - free_axis * dot(motion_wind, free_axis);
	vec3 across = cross(free_axis, wind);
	float t = motion_time * TAU * flutter_frequency;
	float ripple = sin(t - u * 5.0 + motion_phase + p.x * 1.7 + p.z * 1.1)
		+ 0.35 * sin(t * 1.67 - u * 8.0 + motion_phase);
	vec3 offset = wind * bend_amplitude * weight * weight
		+ across * flutter_amplitude * weight * weight * weight * ripple;
	// A wall-mounted surface can move away from its support, never through it. Zero disables the constraint.
	offset -= support_normal * min(0.0, dot(offset, support_normal));
	// Small arc shortening keeps bending from simply stretching the free end away from its attachment.
	float shorten = max(0.0, distance - sqrt(max(distance * distance - dot(offset, offset), 0.0)));
	p += offset - free_axis * shorten;
	return (target_to_mesh * vec4(p, 1.0)).xyz;
}

void vertex() {
	vec3 n = normalize(NORMAL);
	vec3 tangent = normalize(cross(abs(n.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0), n));
	vec3 bitangent = cross(n, tangent);
	vec3 p = deform(VERTEX);
	vec3 a = deform(VERTEX + tangent * 0.01) - p;
	vec3 b = deform(VERTEX + bitangent * 0.01) - p;
	NORMAL = normalize(cross(a, b));
	// Godot detects animated shadow casters through TIME reads directly in vertex().
	// This cache marker preserves our shared, pausable clock while refreshing point-light shadows.
	VERTEX = p + vec3(TIME * 0.0);
}

void fragment() {
	if (source_is_hidden(FRONT_FACING, VERTEX, INV_VIEW_MATRIX)) discard;
	vec2 uv = source_uv(UV);
	vec4 color = source_albedo(uv);
	if (source_is_cutout(color.a)) discard;
	ALBEDO = color.rgb * (source_vertex_color ? COLOR.rgb : vec3(1.0));
	ROUGHNESS = source_roughness;
	METALLIC = source_metallic;
	SPECULAR = source_specular;
	if (source_normal_enabled) {
		NORMAL_MAP = source_normal_map(uv);
		NORMAL_MAP_DEPTH = source_normal_scale;
	}
}
