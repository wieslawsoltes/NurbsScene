#version 300 es
precision highp float;layout(location=0)in vec3 position;layout(location=1)in vec3 normal;uniform mat4 matrix;out vec3 world;out vec3 vertexNormal;
void main(){gl_Position=matrix*vec4(position,1.0);world=position;vertexNormal=normal;}