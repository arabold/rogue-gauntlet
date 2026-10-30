using Godot;

/// <summary>Shared rule for spell lights whose energy an effect drives.</summary>
public static class EffectLight
{
	/// <summary>
	/// Drives a light whose energy an effect owns: base energy shaped by a light curve, whose values above 1 apply
	/// only when an <see cref="ImpactFlash"/> was granted so dense effects never strobe, then by the flicker. A light
	/// that contributes nothing is hidden, since a zero-energy light still costs per-object light culling.
	/// </summary>
	public static void Apply(OmniLight3D light, float energy, float curve, bool flash, LightFlicker flicker, float time)
	{
		float value = energy * (flash ? curve : Mathf.Min(curve, 1f));
		if (value > .001f && flicker != null) value *= flicker.Sample(time);
		light.LightEnergy = value;
		light.Visible = value > .001f;
	}
}