vec2 getComplex(int i) {
  return texelFetch(roots, i).rg;
}
vec4 fragmentShader() {
  vec2 uv = TexCoord * 2. - 1.;

  float d = distance(uv, getComplex(0));
  for (int i = 0; i < rootCount; i++) {
    vec2 c = getComplex(i);
    d = min(d, distance(uv, c));
  }

  if (d < 0.01) {
    d = 0.01;
  } else {
    d = .1 / d;
  }

  return vec4(currentColour.rgb * d, 1.);
}
