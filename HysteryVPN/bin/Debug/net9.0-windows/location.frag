#version 330 core
out vec4 FragColor;

void main()
{
    // Calculate distance from center of the point
    vec2 coord = gl_PointCoord - vec2(0.5);
    float dist = length(coord);

    // Create a glowing effect: brighter in the center, fading to transparent edges
    float alpha = 1.0 - smoothstep(0.0, 0.5, dist);
    vec3 color = vec3(1.0, 0.5, 0.0); // Orange-red glow
    FragColor = vec4(color, alpha);
}

