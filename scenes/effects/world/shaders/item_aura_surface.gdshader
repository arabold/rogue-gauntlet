shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform float effect_time = 0;
uniform float phase = 0;
uniform float envelope = 0;
uniform float outset = 0.01;
uniform vec4 tint : source_color = vec4(0.7,0.035,0.25,1);
uniform vec4 highlight : source_color = vec4(1.0,0.28,0.48,1);
uniform vec3 bounds_min = vec3(0);
uniform vec3 bounds_size = vec3(1);
uniform vec3 long_axis = vec3(0,1,0);
varying vec3 point;
void vertex() {
 point=VERTEX;
 VERTEX+=NORMAL*(outset*(0.65+0.35*sin(VERTEX.y*17.0+VERTEX.x*13.0-effect_time*2.0+phase)));
}
void fragment() {
 float flow=sin(dot(point,vec3(16.0,12.0,13.0))-effect_time*2.5+phase);
 float strand=pow(max(0.0,flow),18.0);
 float flecks=pow(max(0.0,sin(dot(point,vec3(39.0,21.0,29.0))+effect_time*1.4)),24.0);
 float height=dot((point-bounds_min)/max(bounds_size,vec3(0.001)),long_axis);
 float tip_fade=smoothstep(0.0,0.10,height)*(1.0-smoothstep(0.88,1.0,height));
 float rim=pow(1.0-abs(dot(normalize(NORMAL),normalize(VIEW))),1.4);
 ALBEDO=mix(tint.rgb,highlight.rgb,strand*0.45);
 EMISSION=ALBEDO*1.3;
 ALPHA=(strand*(0.28+rim*0.14)+flecks*0.08)*tip_fade*envelope;
}
