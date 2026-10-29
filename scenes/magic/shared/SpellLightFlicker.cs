using Godot;

public enum LightFlickerStyle { None, Flame, Electric }

/// <summary>
/// Brightness multipliers for spell lights: a soft waver for fire and a hard random strobe for
/// electricity. Seeds keep neighbouring lights from flickering in sync.
/// </summary>
public static class SpellLightFlicker
{
	/// <summary>A per-effect seed, so neighbouring effects never flicker in step.</summary>
	public static float Seed(GodotObject owner) => owner.GetInstanceId() % 997;

	/// <summary>
	/// Drives a spell light: base energy shaped by a light curve, whose values above 1 apply only when an
	/// <see cref="ImpactFlash"/> was granted so dense effects never strobe, and by the flicker style. A light that
	/// contributes nothing is hidden, since a zero-energy light still costs per-object light culling.
	/// </summary>
	public static void Apply(OmniLight3D light, float energy, float curve, bool flash, LightFlickerStyle style, float time, float seed)
	{
		float value = energy * (flash ? curve : Mathf.Min(curve, 1f)) * Sample(style, time, seed);
		light.LightEnergy = value;
		light.Visible = value > .001f;
	}

	public static float Sample(LightFlickerStyle style, float time, float seed) => style switch
	{
		LightFlickerStyle.Flame => 1f + .2f * (.6f * Mathf.Sin(time * 9.7f + seed) + .4f * Mathf.Sin(time * 23.1f + seed * 2.9f)),
		LightFlickerStyle.Electric => .3f + Hash(Mathf.Floor(time * 18f) + seed * 17.3f),
		_ => 1f,
	};

	private static float Hash(float x) => Mathf.PosMod(Mathf.Sin(x * 127.1f) * 43758.5453f, 1f);
}
