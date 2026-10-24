// Animal parts: soft, rounded "toy figure" shading. A wrapped diffuse ramp keeps a
// gentle gradient across the form (so shapes read as 3D, not flat paper), the shade
// side takes a warm tint, a broad soft highlight and a rim light pick out the edges,
// and undersides darken slightly as if in contact shadow.
shader_type spatial;

uniform vec4 albedo : source_color = vec4(1.0);
uniform float gloss = 0.18;

varying float world_up;

void vertex() {
	world_up = (MODEL_MATRIX * vec4(NORMAL, 0.0)).y;
}

void fragment() {
	// Undersides a little darker (fake ambient occlusion).
	float occlusion = mix(0.78, 1.0, smoothstep(-0.9, 0.3, world_up));
	ALBEDO = albedo.rgb * occlusion;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float wrap = clamp((ndl + 0.5) / 1.5, 0.0, 1.0);
	// Smooth falloff with a slightly firmer terminator: soft but still anime-clean.
	float ramp = smoothstep(0.0, 1.0, wrap);
	vec3 shade = mix(vec3(0.55, 0.5, 0.62), vec3(1.0), ramp);
	DIFFUSE_LIGHT += shade * ATTENUATION * LIGHT_COLOR / PI;

	vec3 h = normalize(LIGHT + VIEW);
	float spec = pow(max(dot(NORMAL, h), 0.0), 28.0);
	SPECULAR_LIGHT += smoothstep(0.2, 0.7, spec) * gloss * 0.6 * ATTENUATION * LIGHT_COLOR;

	float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	DIFFUSE_LIGHT += vec3(smoothstep(0.6, 0.9, rim) * 0.18 * ramp) * LIGHT_COLOR / PI;
}
