#version 330 core
out vec4 FragColor;
in vec3 FragPos;
in vec2 TexCoord;
in vec3 Normal;

uniform sampler2D earthTexture;
uniform vec3 sunDirection;

void main()
{
    vec4 texColor = texture(earthTexture, TexCoord);

    // Calculate lighting (terminator)
    float dotNL = dot(Normal, sunDirection);
    float light = clamp(dotNL, 0.0, 1.0);

    // Add some ambient light for the night side
    float ambient = 0.05;
    float intensity = max(light, ambient);
    
    FragColor = vec4(texColor.rgb * intensity, texColor.a);
}

