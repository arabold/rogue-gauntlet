shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(1,0.2,0.1,1);
uniform float envelope = 1;
uniform bool entry_only = false;
uniform float effect_time = 0;
uniform float phase = 0;
uniform int shape = 0;
uniform float speed = 1;
uniform float density = 3;
uniform float brightness = 1;
uniform float softness = 0.035;
uniform float twist = 1;
varying vec3 object_position;
void vertex() { object_position=VERTEX; }
void fragment() {
 float t=effect_time*speed;
 float mask=0.0;
 if(shape==0 || shape==4) {
  vec2 p=(UV-0.5)*2.0;
  float r=length(p), a=atan(p.y,p.x);
  float radius=shape==4 ? 0.73 : mix(0.25,0.88,clamp(effect_time*1.8,0.0,1.0));
  float ring=exp(-pow((r-radius)/softness,2.0));
  float ring2=exp(-pow((r-radius*0.84)/(softness*0.4),2.0));
  float breaks=smoothstep(-0.5,0.2,sin(a*density+t+phase));
  mask=ring*breaks+ring2*0.3;
  if(shape==4) {
   float sectors=abs(fract((a+t*0.1)/6.283185*density)-0.5);
   float glyphs=(1.0-smoothstep(0.025,0.065,sectors))*smoothstep(0.51,0.56,r)*(1.0-smoothstep(0.66,0.70,r));
   mask+=glyphs*0.65;
  }
 } else if(shape==1) {
  float v=UV.y;
  float wave=sin(UV.x*6.283185*density+v*twist*6.0-t*4.0+phase);
  float filament=pow(max(0.0,wave),28.0);
  float wake=pow(max(0.0,wave),5.0)*0.11;
  mask=(filament+wake)*pow(sin(clamp(v,0.0,1.0)*3.141593),1.4);
 } else if(shape==2) {
  float rim=pow(1.0-abs(dot(normalize(NORMAL),normalize(VIEW))),2.4);
  float waves=pow(max(0.0,sin(UV.y*35.0+sin(UV.x*18.0+t)*1.4-t*2.0)),18.0);
  mask=rim*0.24+waves*rim*0.5;
 } else if(shape==3) {
  float streaks=pow(max(0.0,sin(UV.x*6.283185*density+phase)),26.0);
  float rise=pow(max(0.0,sin(UV.y*9.0+t*6.0)),3.0);
  mask=streaks*rise*pow(sin(UV.y*3.141593),1.2);
 }
 if(entry_only) mask *= 1.0-smoothstep(0.15,0.7,effect_time);
 vec3 core=mix(tint.rgb,vec3(1.0,0.94,0.78),clamp(mask-0.5,0.0,0.5));
 ALBEDO=core*brightness;
 EMISSION=core*brightness;
 ALPHA=clamp(mask*envelope*tint.a,0.0,1.0);
}