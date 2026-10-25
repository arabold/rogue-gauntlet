shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back;

uniform float amount : hint_range(0.0, 1.0) = 1.0;
uniform float body_opacity : hint_range(0.0, 0.2) = 0.025;
uniform float edge_opacity : hint_range(0.0, 0.3) = 0.14;
uniform vec4 veil_color : source_color = vec4(0.58, 0.76, 0.84, 1.0);
uniform vec4 base_color : source_color = vec4(1.0);
uniform sampler2D source_texture : source_color, hint_default_white, filter_linear_mipmap;
uniform float source_roughness = 0.7;
uniform float source_metallic = 0.0;
varying vec3 world_position;

void vertex() {
 world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
 vec4 original = texture(source_texture, UV) * base_color;
 float edge = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 2.5);
 // A slow, shallow ripple keeps the outline alive without blinking or moving the mesh.
 float shimmer = 0.92 + 0.08 * sin(world_position.y * 7.0 + world_position.x * 1.5 - TIME * 1.6);
 float veil = clamp((body_opacity + edge_opacity * edge) * shimmer, 0.0, 1.0);
 ALBEDO = mix(original.rgb, veil_color.rgb, amount);
 ALPHA = original.a * mix(1.0, veil, amount);
 ROUGHNESS = mix(source_roughness, 0.75, amount);
 METALLIC = source_metallic * (1.0 - amount);
 SPECULAR = 0.5 * (1.0 - amount);
 EMISSION = veil_color.rgb * edge * 0.12 * amount;
}
