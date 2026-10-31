// An animal's coat: its colour with a pattern worked out from where each point is on the
// body at rest (kept in UV and UV2), so the pattern stays put on the fur as the body moves.
shader_type spatial;

uniform vec3 base_color : source_color = vec3(0.5);
uniform vec3 pattern_color : source_color = vec3(0.1);
// 0 none, 1 spots, 2 stripes, 3 patches.
uniform int pattern = 0;
uniform float pattern_size = 0.5;
uniform float pattern_amount = 0.5;

varying vec3 rest;

vec3 hash(vec3 p) {
	p = vec3(dot(p, vec3(127.1, 311.7, 74.7)), dot(p, vec3(269.5, 183.3, 246.1)), dot(p, vec3(113.5, 271.9, 124.6)));
	return fract(sin(p) * 43758.5453);
}

float noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash(i).x, hash(i + vec3(1, 0, 0)).x, f.x), mix(hash(i + vec3(0, 1, 0)).x, hash(i + vec3(1, 1, 0)).x, f.x), f.y);
	float b = mix(mix(hash(i + vec3(0, 0, 1)).x, hash(i + vec3(1, 0, 1)).x, f.x), mix(hash(i + vec3(0, 1, 1)).x, hash(i + vec3(1, 1, 1)).x, f.x), f.y);
	return mix(a, b, f.z);
}

float spots(vec3 p) {
	float cells = mix(26.0, 8.0, pattern_size);
	vec3 q = p * cells;
	vec3 i = floor(q);
	float best = 0.0;
	for (int x = -1; x <= 1; x++) {
		for (int y = -1; y <= 1; y++) {
			for (int z = -1; z <= 1; z++) {
				vec3 cell = i + vec3(float(x), float(y), float(z));
				vec3 r = hash(cell);
				// Some cells have no spot: the amount decides how many do.
				if (fract(r.x * 7.13) > 0.25 + 0.75 * pattern_amount)
					continue;
				float radius = 0.28 + 0.25 * fract(r.y * 3.7);
				float d = length(q - cell - r);
				best = max(best, 1.0 - smoothstep(radius - 0.05, radius + 0.05, d));
			}
		}
	}
	return best;
}

float stripes(vec3 p) {
	float bands = mix(70.0, 20.0, pattern_size);
	float wave = sin((p.z + 0.1 * noise(p * 7.0) + 0.05 * noise(p * 17.0)) * bands);
	float edge = mix(0.75, -0.55, pattern_amount);
	return smoothstep(edge - 0.08, edge + 0.08, wave);
}

float patches(vec3 p) {
	float scale = mix(11.0, 3.5, pattern_size);
	float n = 0.65 * noise(p * scale) + 0.35 * noise(p * scale * 2.3 + 5.0);
	float edge = mix(0.75, 0.3, pattern_amount);
	return smoothstep(edge - 0.015, edge + 0.015, n);
}

void vertex() {
	rest = vec3(UV.x, UV.y, UV2.x);
}

void fragment() {
	float mark = 0.0;
	if (pattern == 1)
		mark = spots(rest);
	else if (pattern == 2)
		mark = stripes(rest);
	else if (pattern == 3)
		mark = patches(rest);
	ALBEDO = mix(base_color, pattern_color, mark);
	ROUGHNESS = 1.0;
	SPECULAR = 0.3;
}
