// Inverted-hull outline for the anime style: the back faces are pushed out along the
// normal by a constant number of pixels, whatever the zoom.
shader_type spatial;
render_mode cull_front, unshaded;

uniform vec4 outline_color : source_color = vec4(0.25, 0.15, 0.12, 1.0);
uniform float width_px = 1.4;

#include "res://assets/shaders/jiggle.gdshaderinc"

void vertex() {
	// Follow the body's jiggle so the outline stays on its edge.
	VERTEX += jiggle(vec3(UV2.x, UV2.y, UV.x));
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec3 view_normal = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec2 offset = (PROJECTION_MATRIX * vec4(view_normal, 0.0)).xy;
	if (length(offset) > 0.00001) {
		clip.xy += normalize(offset) * width_px * 2.0 / VIEWPORT_SIZE * clip.w;
	}
	POSITION = clip;
}

void fragment() {
	ALBEDO = outline_color.rgb;
}
