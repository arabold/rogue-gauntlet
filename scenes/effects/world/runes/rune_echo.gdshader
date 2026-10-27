shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.35,0.75,1.0,1.0);
uniform float effect_time = 0;
uniform float envelope = 1;
uniform float dissolution = 0;
void fragment() {
 float stroke=smoothstep(UV.y*0.30,UV.y*0.30+0.16,effect_time);
 float etch=0.93+0.07*sin(UV.y*62.0-effect_time*3.0);
 vec3 color=mix(tint.rgb,vec3(0.86,0.96,1.0),0.18);
 // Fade the whole glyph continuously as the ring expands, without a noise mask.
 float remaining=1.0-smoothstep(0.0,1.0,dissolution);
 ALBEDO=color*1.25;
 EMISSION=color*1.25;
 ALPHA=stroke*envelope*etch*tint.a*remaining;
}
