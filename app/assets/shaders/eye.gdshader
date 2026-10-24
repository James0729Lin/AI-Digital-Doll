// Procedural eye for the body editor: white sclera, coloured iris and pupil
// placed where the eyeball faces forward (+Z in model space).
shader_type spatial;

uniform vec4 iris_color : source_color = vec4(0.32, 0.2, 0.11, 1.0);

varying vec3 model_normal;

void vertex() {
	model_normal = NORMAL;
}

void fragment() {
	float facing = normalize(model_normal).z;
	vec3 colour = vec3(0.93, 0.92, 0.9);
	colour = mix(colour, iris_color.rgb, smoothstep(0.84, 0.86, facing));
	colour = mix(colour, vec3(0.02), smoothstep(0.955, 0.965, facing));
	ALBEDO = colour;
	ROUGHNESS = 0.12;
	SPECULAR = 0.7;
}
