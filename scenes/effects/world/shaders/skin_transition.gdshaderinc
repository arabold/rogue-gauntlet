// Blend across the entire envelope, with soft irregular patches so the material appears to spread.
// Both endpoints are exact, allowing the original material to be restored without a visible jump.
float skin_transition(vec3 position, float amount) {
	float pattern = 0.5 + 0.5 * sin(position.x * 5.3 + position.z * 3.7)
		* sin(position.y * 6.1 - position.z * 2.9);
	float threshold = mix(0.2, 0.8, pattern);
	float spread = smoothstep(threshold - 0.18, threshold + 0.18, amount);
	return mix(amount, spread, 0.7);
}
