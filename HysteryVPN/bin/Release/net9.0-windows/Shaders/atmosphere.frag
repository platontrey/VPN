#version 330 core
out vec4 FragColor;
in vec3 WorldPos;
in vec3 ViewDir;

uniform vec3 sunDirection;
uniform vec3 cameraPosition;

uniform float earthRadius;
uniform float atmosphereRadius;
uniform vec3 rayleighCoeff;
uniform float mieCoeff;
uniform float mieG;
uniform float rayleighScaleHeight;
uniform float mieScaleHeight;

#define PI 3.14159265359
#define SAMPLES 16
#define LIGHT_SAMPLES 8

float densityAtHeight(float height, float scaleHeight) {
    return exp(-height / scaleHeight);
}

bool raySphereIntersect(vec3 orig, vec3 dir, float radius, out float t0, out float t1) {
    float b = 2.0 * dot(dir, orig);
    float c = dot(orig, orig) - radius * radius;
    float d = b * b - 4.0 * c;
    if (d < 0.0) return false;
    d = sqrt(d);
    t0 = (-b - d) / 2.0;
    t1 = (-b + d) / 2.0;
    return true;
}

vec3 ACESFilm(vec3 x) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x*(a*x+b))/(x*(c*x+d)+e), 0.0, 1.0);
}

void main()
{
    vec3 rayOrigin = cameraPosition;
    vec3 rayDir = normalize(WorldPos - cameraPosition);

    float t0, t1;
    if (!raySphereIntersect(rayOrigin, rayDir, atmosphereRadius, t0, t1)) {
        discard;
    }
    t0 = max(t0, 0.0);

    float segmentLength = (t1 - t0) / float(SAMPLES);
    float tCurrent = t0;

    vec3 rayleighSum = vec3(0.0);
    vec3 mieSum = vec3(0.0);
    float optDepthR = 0.0;
    float optDepthM = 0.0;

    for (int i = 0; i < SAMPLES; i++) {
        vec3 samplePos = rayOrigin + rayDir * (tCurrent + segmentLength * 0.5);
        float height = length(samplePos) - earthRadius;
        
        float dR = densityAtHeight(height, rayleighScaleHeight) * segmentLength;
        float dM = densityAtHeight(height, mieScaleHeight) * segmentLength;
        
        optDepthR += dR;
        optDepthM += dM;

        // Light sampling
        float lt0, lt1;
        raySphereIntersect(samplePos, sunDirection, atmosphereRadius, lt0, lt1);
        float lSegmentLength = lt1 / float(LIGHT_SAMPLES);
        float lOptDepthR = 0.0;
        float lOptDepthM = 0.0;
        bool blocked = false;

        for (int j = 0; j < LIGHT_SAMPLES; j++) {
            vec3 lSamplePos = samplePos + sunDirection * (float(j) + 0.5) * lSegmentLength;
            float lHeight = length(lSamplePos) - earthRadius;
            if (lHeight < 0.0) {
                blocked = true;
                break;
            }
            lOptDepthR += densityAtHeight(lHeight, rayleighScaleHeight) * lSegmentLength;
            lOptDepthM += densityAtHeight(lHeight, mieScaleHeight) * lSegmentLength;
        }

        if (!blocked) {
            vec3 transmittance = exp(-(rayleighCoeff * (optDepthR + lOptDepthR) + mieCoeff * 1.1 * (optDepthM + lOptDepthM)));
            rayleighSum += dR * transmittance;
            mieSum += dM * transmittance;
        }

        tCurrent += segmentLength;
    }

    float cosTheta = dot(rayDir, sunDirection);
    float phaseR = 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
    float phaseM = 3.0 / (8.0 * PI) * ((1.0 - mieG * mieG) * (1.0 + cosTheta * cosTheta)) / ((2.0 + mieG * mieG) * pow(1.0 + mieG * mieG - 2.0 * mieG * cosTheta, 1.5));

    vec3 color = (rayleighSum * rayleighCoeff * phaseR + mieSum * mieCoeff * phaseM) * 40.0; // 40.0 is sun intensity
    
    // Tone mapping
    color = ACESFilm(color);

    FragColor = vec4(color, 1.0);
}
