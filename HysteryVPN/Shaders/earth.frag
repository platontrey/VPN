#version 330 core
out vec4 FragColor;
in vec3 FragPos;
in vec2 TexCoord;
in vec3 Normal;

//uniform sampler2D earthTexture;
uniform vec3 sunDirection;

void main()
{
    //vec4 texColor = texture(earthTexture, TexCoord);
    vec3 oceanColor = vec3(0.3, 0.3, 0.3);

    // Calculate lighting (terminator)
    float dotNL = dot(Normal, sunDirection);
    float light = clamp(dotNL, 0.0, 1.0);

    // Add some ambient light for the night side
    float ambient = 0.2; // 0.05 in past
    float intensity = max(light, ambient);
    
    //FragColor = vec4(texColor.rgb * intensity, texColor.a);
    //FragColor = vec4(1.0, 0.0, 0.0, 1.0); - for test
    FragColor = vec4(oceanColor * intensity, 1.0);
}

