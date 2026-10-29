shader_type spatial;
// Solid skins must write opaque depth; alpha blending exposes back faces on double-sided meshes.
render_mode depth_draw_opaque, cull_disabled;
#include "res://scenes/effects/world/shaders/source_surface.gdshaderinc"
#include "res://scenes/effects/world/shaders/skin_transition.gdshaderinc"
uniform float amount : hint_range(0, 1) = 1;
uniform int skin_kind = 0;
uniform bool faceted = false;
uniform float polish : hint_range(0,1) = 0;
varying vec3 local_position;
vec2 hash2(vec2 p) { return fract(sin(vec2(dot(p,vec2(127.1,311.7)),dot(p,vec2(269.5,183.3))))*43758.5453); }
void vertex() { local_position = VERTEX; }
void fragment() {
 if (source_is_culled(FRONT_FACING)) discard;
 vec2 uv = source_uv(UV);
 vec4 original = source_albedo(uv);
 if (source_is_cutout(original.a)) discard;
 // Derivative normals shade existing triangles flat without rebuilding or modifying the mesh.
 vec3 face_normal=normalize(cross(dFdx(VERTEX),dFdy(VERTEX)));
 if(dot(face_normal,NORMAL)<0.0) face_normal=-face_normal;
 vec3 p=local_position;
 float brushed=0.5+0.5*sin(p.y*420.0+sin(p.x*31.0)*2.0);
 vec3 iron=mix(vec3(0.30,0.35,0.42),vec3(0.68,0.73,0.78),brushed*mix(0.22,0.04,polish)+0.35);
 float etch=pow(0.5+0.5*sin((p.x+p.y)*58.0),48.0);
 vec2 cellp=(p.xy+vec2(p.z*0.37,p.z*0.61))*7.5;
 vec2 cell=floor(cellp), f=fract(cellp);
 float first=8.0, second=8.0;
 for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++) {
  vec2 g=vec2(float(x),float(y));
  float d=length(g+hash2(cell+g)-f);
  if(d<first) {second=first;first=d;} else {second=min(second,d);}
 }
 float crack=1.0-smoothstep(0.015,0.065,second-first);
 float grain=fract(sin(dot(p,vec3(217.1,113.7,61.2)))*43758.5453);
 vec3 stone=mix(vec3(0.31,0.34,0.36),vec3(0.48,0.46,0.40),first)*mix(0.82,1.08,grain);
 stone=mix(stone,vec3(0.065,0.077,0.085),crack*0.88);
 vec3 skin=skin_kind==0 ? iron : stone;
 float reveal=skin_transition(p,amount);
 if(faceted) NORMAL=normalize(mix(NORMAL,face_normal,reveal));
 ALBEDO=mix(original.rgb,skin+original.rgb*0.08,reveal);
 METALLIC=mix(source_metallic,skin_kind==0 ? mix(0.68,0.95,polish) : 0.04,reveal);
 ROUGHNESS=mix(source_roughness,skin_kind==0 ? mix(0.3,0.055,polish) : 0.94,reveal);
 SPECULAR=mix(source_specular,skin_kind==0 ? mix(0.65,0.9,polish) : 0.28,reveal);
 EMISSION=skin_kind==0 ? vec3(0.18,0.29,0.38)*etch*0.12*reveal : vec3(0.16,0.11,0.055)*crack*0.08*reveal;
 if(source_normal_enabled) { NORMAL_MAP=source_normal_map(uv); NORMAL_MAP_DEPTH=source_normal_scale*(faceted ? 1.0-reveal : 1.0); }
}
