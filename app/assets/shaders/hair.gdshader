// Procedural hair locks: darker roots, fine strands across each lock and a sheen
// band. `toon` gives flat two-tone shading with an anime "angel ring" highlight.
shader_type spatial;
render_mode cull_disabled;

uniform vec4 hair_color : source_color = vec4(0.17, 0.12, 0.09, 1.0);
uniform bool toon = false;

varying float along;
varying float across;
varying float band;

void vertex() {
	across = UV.x;
	along = UV.y;
	// The ring sits on the upper part of each lock.
	band = 1.0 - smoothstep(0.15, 0.45, UV.y);
}

float hash(float n) {
	return fract(sin(n) * 43758.5453);
}

void fragment() {
	vec3 base = hair_color.rgb;
	float strands = toon ? 0.0 : (hash(floor(across * 40.0)) - 0.5) * 0.18;
	vec3 colour = base * (1.0 + strands) * mix(0.82, 1.08, smoothstep(0.0, 0.5, along));
	ALBEDO = colour;
	ROUGHNESS = toon ? 1.0 : 0.45;
	SPECULAR = toon ? 0.0 : 0.4;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	if (toon) {
		float lit = smoothstep(0.0, 0.1, ndl);
		DIFFUSE_LIGHT += mix(vec3(0.55, 0.5, 0.6), vec3(1.0), lit) * ATTENUATION * LIGHT_COLOR / PI;
		// Angel ring: a bright band across the upper hair, facing the viewer.
		float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
		float ring = smoothstep(0.8, 0.92, facing) * (1.0 - smoothstep(0.94, 0.99, facing));
		SPECULAR_LIGHT += ring * band * 0.12 * ATTENUATION * LIGHT_COLOR / PI;
	} else {
		float wrap = clamp((ndl + 0.3) / 1.3, 0.0, 1.0);
		DIFFUSE_LIGHT += wrap * ATTENUATION * LIGHT_COLOR / PI;
		vec3 h = normalize(LIGHT + VIEW);
		SPECULAR_LIGHT += pow(max(dot(NORMAL, h), 0.0), 40.0) * 0.18 * ATTENUATION * LIGHT_COLOR;
	}
}
