#version 330 core
layout (location = 0) in vec3 aPos;

uniform mat4 view;
uniform mat4 projection;
uniform float cameraDistance;

void main()
{
    gl_Position = projection * view * vec4(aPos, 1.0);
    gl_PointSize = 20.0 * (5.0 / cameraDistance); // Size increases as camera gets closer
}