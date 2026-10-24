// Soft-tissue jiggle for the edited body. `base` is the vertex on the neutral base
// mesh (UV2 = x, y; UV.x = z), so the regions stay put whatever the body shape.
// Offsets come from springs simulated on the CPU (BodyJiggle.cs), in model space;
// they are zero on every other mesh that uses these shaders.
uniform vec3 bust_centre = vec3(0.0);
uniform vec3 bust_radii = vec3(1.0);
uniform vec3 bust_offset_l = vec3(0.0);
uniform vec3 bust_offset_r = vec3(0.0);
uniform vec3 buttocks_centre = vec3(0.0);
uniform vec3 buttocks_radii = vec3(1.0);
uniform vec3 buttocks_offset_l = vec3(0.0);
uniform vec3 buttocks_offset_r = vec3(0.0);

// Ellipsoid falloff, gated so only the part in front of the centre (+z) moves:
// nothing at the ribcage or pelvis.
float jiggle_weight(vec3 p, vec3 centre, vec3 radii) {
	float w = 1.0 - smoothstep(0.3, 1.0, length((p - centre) / radii));
	w *= smoothstep(centre.z - 0.9 * radii.z, centre.z + 0.2 * radii.z, p.z);
	return w * w;
}

vec3 jiggle(vec3 base) {
	vec3 p = vec3(abs(base.x), base.y, base.z);
	bool left = base.x >= 0.0;
	vec3 behind = vec3(1.0, 1.0, -1.0);
	float bust = jiggle_weight(p, bust_centre, bust_radii);
	float buttocks = jiggle_weight(p * behind, buttocks_centre * behind, buttocks_radii);
	return bust * (left ? bust_offset_l : bust_offset_r) + buttocks * (left ? buttocks_offset_l : buttocks_offset_r);
}
