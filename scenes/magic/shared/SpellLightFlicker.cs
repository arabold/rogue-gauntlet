using Godot;

/// <summary>Drives spell lights whose energy an effect owns, applying their <see cref="LightFlicker"/> as a multiplier.</summary>
public static class SpellLightFlicker
{
	/// <summary>
	/// Drives a spell light: base energy shaped by a light curve, whose values above 1 apply only when an
	/// <see cref="ImpactFlash"/> was granted so dense effects never strobe, and by the flicker. A light that
	/// contributes nothing is hidden, since a zero-energy light still costs per-object light culling.
	/// </summary>
	public static void Apply(OmniLight3D light, float energy, float curve, bool flash, LightFlicker flicker, float time, float offset)
	{
		float value = energy * (flash ? curve : Mathf.Min(curve, 1f)) * (flicker?.Sample(time, offset) ?? 1f);
		light.LightEnergy = value;
		light.Visible = value > .001f;
	}
}