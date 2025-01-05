shader_type canvas_item;

uniform vec2 CircleCentre = vec2(0,0);
uniform float CircleRadius = 100.0;
uniform float CircleBlur = 0.01;

void fragment()
{
    // make sure blur is in the expected range (could be done with a hint)
    float circle_blur = clamp( 0.0, 1.0, CircleBlur );
    // compute distance between fragment and circle centre
    float circle_dist = distance( CircleCentre, FRAGCOORD.xy );
    // compute signal based on circle radius
    float circle_signal = 1.0 - smoothstep( CircleRadius, CircleRadius*(1.0-circle_blur), circle_dist );
    // read the base texture color
    vec4 tex_color = texture( TEXTURE, UV );
    // set output color to the read texture value
    COLOR = tex_color;
    // now modify the alpha by the circle
    COLOR.a *= circle_signal;
}