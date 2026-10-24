// Base body: skin with the fitted underwear painted on by rest-pose position.
// UV2 carries each vertex's (x, y) on the neutral base mesh, so the cut lines are
// exact planes and stay attached to the same skin whatever the body shape.
shader_type spatial;

uniform vec4 skin_color : source_color = vec4(0.88, 0.67, 0.54, 1.0);
uniform vec4 underwear_color : source_color = vec4(0.23, 0.25, 0.29, 1.0);
uniform bool wears_top = true;
uniform float top_low;
uniform float top_high;
uniform float top_half_width;
uniform float shorts_low;
uniform float shorts_high;
uniform float shorts_half_width;

varying vec2 rest;

void vertex() {
	rest = UV2;
}

float inside(float value, float low, float high, float soft) {
	return smoothstep(low - soft, low + soft, value) * (1.0 - smoothstep(high - soft, high + soft, value));
}

void fragment() {
	float soft = max(0.5 * (fwidth(rest.y) + fwidth(rest.x)), 0.0003);
	float x = abs(rest.x);
	float top = inside(rest.y, top_low, top_high, soft) * (1.0 - smoothstep(top_half_width - soft, top_half_width + soft, x));
	float shorts = inside(rest.y, shorts_low, shorts_high, soft) * (1.0 - smoothstep(shorts_half_width - soft, shorts_half_width + soft, x));
	float cover = max(wears_top ? top : 0.0, shorts);

	ALBEDO = mix(skin_color.rgb, underwear_color.rgb, cover);
	ROUGHNESS = mix(0.6, 0.85, cover);
	RIM = 0.15 * (1.0 - cover);
	RIM_TINT = 0.7;
}
