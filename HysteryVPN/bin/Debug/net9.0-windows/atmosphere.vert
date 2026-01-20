#version 330 core
layout (location = 0) in vec3 aPos;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform vec3 cameraPosition;

out vec3 WorldPos;
out vec3 Normal;
out vec3 ViewDir;

void main()
{
    WorldPos = vec3(model * vec4(aPos, 1.0));
    Normal = normalize(aPos);
    ViewDir = normalize(cameraPosition - WorldPos);
    gl_Position = projection * view * vec4(WorldPos, 1.0);
}
