using Godot;

/// <summary>Shared smooth attack and release for short-lived visual effects.</summary>
public static class EffectEnvelope
{
	public static float Evaluate(float progress, float attackEnd, float releaseStart) =>
		Mathf.SmoothStep(0, attackEnd, progress) * (1f - Mathf.SmoothStep(releaseStart, 1, progress));
}
