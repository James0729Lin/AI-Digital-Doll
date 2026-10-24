// Base body: skin with fitted underwear, scalp, eyebrows, lips and lash line painted
// by rest-pose position. UV2 = (x, y) and UV.x = z of each vertex on the neutral base
// mesh, so every painted edge is exact and stays on the same skin whatever the shape.
// `toon` switches between soft realistic lighting and two-tone anime shading.
shader_type spatial;

uniform vec4 skin_color : source_color = vec4(0.88, 0.67, 0.54, 1.0);
uniform vec4 underwear_color : source_color = vec4(0.23, 0.25, 0.29, 1.0);
uniform vec4 hair_color : source_color = vec4(0.17, 0.12, 0.09, 1.0);
uniform bool toon = false;

uniform bool wears_top = true;
uniform float top_low;
uniform float top_high;
uniform float top_half_width;
uniform float shorts_low;
uniform float shorts_high;
uniform float shorts_half_width;

uniform bool scalp_on = true;
uniform vec3 head_centre;
uniform vec3 head_radii = vec3(0.09, 0.11, 0.1);
uniform float hairline_front = 0.47;
uniform float hairline_side = 0.22;
uniform float hairline_back = -0.72;

uniform vec3 eye_centre;     // left eye (x > 0), rest pose
uniform vec3 mouth_centre;   // middle of the lips, rest pose
uniform float face_front;    // rest z in front of which the face is painted

varying vec3 rest;

#include "res://assets/shaders/jiggle.gdshaderinc"

void vertex() {
	rest = vec3(UV2.x, UV2.y, UV.x);
	VERTEX += jiggle(rest);
}

float inside(float value, float low, float high, float soft) {
	return smoothstep(low - soft, low + soft, value) * (1.0 - smoothstep(high - soft, high + soft, value));
}

float eyebrow(vec3 p, float soft) {
	float x = abs(p.x);
	float inner = eye_centre.x - 0.019, outer = eye_centre.x + 0.024;
	float u = (x - inner) / (outer - inner);           // 0 inner end .. 1 outer end
	float arch = eye_centre.y + 0.0185 + 0.0055 * (1.0 - pow((u - 0.42) / 0.62, 2.0));
	float thickness = mix(toon ? 0.0024 : 0.0034, 0.0012, u);
	float along = inside(u, 0.0, 1.0, 0.05);
	return along * (1.0 - smoothstep(thickness - soft, thickness + soft, abs(p.y - arch)));
}

float lash_line(vec3 p, float soft) {
	vec2 d = vec2(abs(p.x) - eye_centre.x, p.y - eye_centre.y - 0.002);
	vec2 r = toon ? vec2(0.0165, 0.0085) : vec2(0.0155, 0.0075);
	float e = length(d / r);
	float width = toon ? 0.16 : 0.07;
	float upper = smoothstep(-0.003, 0.0015, d.y);
	// Anime liner flicks out at the outer corner.
	float wing = toon ? smoothstep(0.004, 0.012, d.x) * 0.12 : 0.0;
	return upper * (1.0 - smoothstep(width + wing - soft * 40.0, width + wing + soft * 40.0, abs(e - 1.0)));
}

float lips(vec3 p, float soft) {
	vec2 d = vec2(p.x - mouth_centre.x, p.y - mouth_centre.y);
	float half_width = 0.0235;
	float u = abs(d.x) / half_width;
	if (u > 1.0) return 0.0;
	float bow = 0.0012 * smoothstep(0.0, 0.35, u) * (1.0 - smoothstep(0.35, 0.9, u));
	float upper = (0.0085 + bow) * sqrt(max(1.0 - u * u, 0.0));
	float lower = 0.0105 * sqrt(max(1.0 - u * u, 0.0));
	float top = 1.0 - smoothstep(upper - soft, upper + soft, d.y);
	float bottom = smoothstep(-lower - soft, -lower + soft, d.y);
	return top * bottom;
}

void fragment() {
	float soft = max(0.5 * (fwidth(rest.y) + fwidth(rest.x)), 0.0003);
	float x = abs(rest.x);

	vec3 colour = skin_color.rgb;
	float face = smoothstep(face_front - 0.004, face_front + 0.004, rest.z);

	// Lips: a deeper, rosier version of the skin.
	vec3 lip_colour = mix(skin_color.rgb * vec3(0.92, 0.62, 0.62), vec3(0.78, 0.36, 0.38), 0.35);
	colour = mix(colour, lip_colour, lips(rest, soft) * face * (toon ? 0.55 : 0.85));

	// Scalp under the hair.
	if (scalp_on) {
		vec3 q = (rest - head_centre) / head_radii;
		float c = q.z / max(length(q.xz), 0.00001);
		float line = c > 0.0 ? mix(hairline_side, hairline_front, c) : mix(hairline_side, hairline_back, -c);
		float scalp = smoothstep(line - 0.04, line + 0.04, q.y) * (1.0 - smoothstep(1.0, 1.06, abs(q.x)));
		colour = mix(colour, hair_color.rgb * 0.85, scalp);
	}

	vec3 brow_colour = hair_color.rgb * 0.8;
	colour = mix(colour, brow_colour, eyebrow(rest, soft) * face * (toon ? 1.0 : 0.85));
	colour = mix(colour, vec3(0.08, 0.05, 0.05), lash_line(rest, soft) * face);

	float top = inside(rest.y, top_low, top_high, soft) * (1.0 - smoothstep(top_half_width - soft, top_half_width + soft, x));
	float shorts = inside(rest.y, shorts_low, shorts_high, soft) * (1.0 - smoothstep(shorts_half_width - soft, shorts_half_width + soft, x));
	float cover = max(wears_top ? top : 0.0, shorts);
	colour = mix(colour, underwear_color.rgb, cover);

	ALBEDO = colour;
	ROUGHNESS = mix(0.6, 0.85, cover);
	SPECULAR = toon ? 0.0 : 0.35;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	if (toon) {
		// Two tones with a narrow, soft terminator.
		float lit = smoothstep(0.0, 0.08, ndl);
		DIFFUSE_LIGHT += mix(vec3(0.62, 0.55, 0.6), vec3(1.0), lit) * ATTENUATION * LIGHT_COLOR / PI;
	} else {
		// Wrapped diffuse softens skin; a faint sheen keeps it from looking plastic.
		float wrap = clamp((ndl + 0.35) / 1.35, 0.0, 1.0);
		DIFFUSE_LIGHT += wrap * ATTENUATION * LIGHT_COLOR / PI;
		vec3 h = normalize(LIGHT + VIEW);
		SPECULAR_LIGHT += pow(max(dot(NORMAL, h), 0.0), 24.0) * 0.06 * ATTENUATION * LIGHT_COLOR;
	}
}
