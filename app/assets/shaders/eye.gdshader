// Procedural eye: sclera, iris and pupil where the eyeball faces forward (+Z in
// model space). The anime style uses a larger, graded iris with a catch light.
shader_type spatial;

uniform vec4 iris_color : source_color = vec4(0.32, 0.2, 0.11, 1.0);
uniform bool toon = false;

varying vec3 model_normal;

void vertex() {
	model_normal = NORMAL;
}

void fragment() {
	vec3 n = normalize(model_normal);
	float facing = n.z;
	vec3 colour = vec3(0.95, 0.94, 0.92);
	if (toon) {
		float iris = smoothstep(0.66, 0.69, facing);
		vec3 graded = mix(iris_color.rgb * 0.45, iris_color.rgb * 1.25, smoothstep(0.08, -0.12, n.y));
		colour = mix(colour, graded, iris);
		colour = mix(colour, iris_color.rgb * 0.2, smoothstep(0.93, 0.945, facing));
		float catchlight = smoothstep(0.985, 0.995, dot(n, normalize(vec3(-0.25, 0.3, 1.0))));
		colour = mix(colour, vec3(1.0), catchlight);
	} else {
		colour = mix(colour, iris_color.rgb, smoothstep(0.84, 0.86, facing));
		colour = mix(colour, vec3(0.02), smoothstep(0.955, 0.965, facing));
	}
	ALBEDO = colour;
	ROUGHNESS = 0.12;
	SPECULAR = toon ? 0.0 : 0.7;
}
