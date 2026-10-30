using Godot;

/// <summary>A light's flicker: a brightness multiplier read from noise over time. Presets are shared and stateless.</summary>
[GlobalClass]
public partial class LightFlicker : Resource
{
	/// <summary>Shapes the flicker. It is read along one axis in seconds, so its frequency is how busy the flicker
	/// is per second; cellular noise returning cell values gives evenly spread random levels for a strobe.</summary>
	[Export] public FastNoiseLite Noise { get; set; }
	/// <summary>Multiplier at the low end of the noise.</summary>
	[Export] public float Min { get; set; } = .8f;
	/// <summary>Multiplier at the high end of the noise.</summary>
	[Export] public float Max { get; set; } = 1.2f;
	/// <summary>Reads the noise's magnitude, so the light rests near <see cref="Min"/> and flares toward
	/// <see cref="Max"/> in sharp bounces like a guttering torch, instead of wavering around the middle.</summary>
	[Export] public bool Fold { get; set; }
	/// <summary>Levels per second held as hard steps for a strobe; 0 keeps the flicker smooth.</summary>
	[Export(PropertyHint.Range, "0,60,0.5,or_greater")] public float StepRate { get; set; }

	/// <summary>The brightness multiplier at a light's flicker clock; 1 without noise.</summary>
	public float Sample(float time)
	{
		if (Noise == null) return 1f;
		if (StepRate > 0) time = Mathf.Floor(time * StepRate) / StepRate;
		float noise = Noise.GetNoise1D(time);
		return Mathf.Lerp(Min, Max, Fold ? Mathf.Abs(noise) : (noise + 1f) * .5f);
	}

	/// <summary>
	/// A cosmetic start for a light's flicker clock, derived from its owner's instance so lights spawned in the same
	/// frame (every torch on a floor) never flicker in step, without drawing from a gameplay random stream.
	/// </summary>
	public static float RandomOffset(GodotObject owner) =>
		GenerationRandom.DeriveSeed(owner.GetInstanceId(), "light-flicker") % 1_000_000 * .001f;
}