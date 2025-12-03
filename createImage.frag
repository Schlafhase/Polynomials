#define n 13
vec2 getComplex(int i) {
  return texelFetch(roots, i).rg;
}
vec4 fragmentShader() {
  vec2 uv = TexCoord * 2. - 1.;
  uv.x *= uResolution.x / uResolution.y;
  uv *= 2;

  float d = 9999;
  for (int i = 0; i < rootCount; i++) {
    vec2 c = getComplex(i);
    if (distance(c, vec2(0, 0)) < 0.2) {
      continue;
    }
    d = min(d, distance(uv, c));
  }

  d = 1. / (50.*d);

  return vec4((currentColour.rgb * d)/n, 1.);
}
