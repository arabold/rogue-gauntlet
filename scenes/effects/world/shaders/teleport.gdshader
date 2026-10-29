shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.3,0.55,1.0,1.0);
uniform float effect_time = 0;
uniform float envelope = 0;
uniform float phase = 0;
uniform bool arriving = true;
void vertex() {
 float progress=smoothstep(0.0,0.35,effect_time);
 float radius=arriving ? mix(0.08,1.35,progress) : mix(1.35,0.08,progress);
 VERTEX.xz *= radius;
}
void fragment() {
 float rim=pow(1.0-abs(dot(normalize(NORMAL),normalize(VIEW))),2.0);
 float sweep=pow(max(0.0,sin(UV.x*18.84956+UV.y*8.0-effect_time*19.0+phase)),12.0);
 float vertical=pow(sin(clamp(UV.y,0.0,1.0)*3.141593),0.7);
 float mask=(rim*0.35+sweep*0.65)*vertical;
 vec3 color=mix(tint.rgb,vec3(0.85,0.94,1.0),sweep*0.45);
 ALBEDO=color*1.6;
 EMISSION=color*1.6;
 ALPHA=mask*envelope*tint.a;
}
