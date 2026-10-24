// Animal parts: flat colour with two-tone anime shading and a soft rim light.
shader_type spatial;

uniform vec4 albedo : source_color = vec4(1.0);
uniform float rim = 0.25;

void fragment() {
	ALBEDO = albedo.rgb;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float lit = smoothstep(0.0, 0.08, ndl);
	DIFFUSE_LIGHT += mix(vec3(0.66, 0.6, 0.66), vec3(1.0), lit) * ATTENUATION * LIGHT_COLOR / PI;
	float edge = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	DIFFUSE_LIGHT += vec3(smoothstep(0.55, 0.8, edge) * rim * lit) * LIGHT_COLOR / PI;
}
