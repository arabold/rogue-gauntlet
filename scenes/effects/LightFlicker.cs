using Godot;

/// <summary>
/// A light's flicker: a brightness multiplier read from noise over time. Presets are shared and stateless; each
/// light keeps its own clock and a <see cref="RandomOffset"/>, so lights spawned together (every torch on a floor)
/// never flicker in step.
/// </summary>
[GlobalClass]
public partial class LightFlicker : Resource
{
	/// <summary>Seconds of noise a light may skip ahead; far longer than any flicker's features.</summary>
	private const float OffsetRange = 1000f;

	/// <summary>Shapes the flicker. It is read along one axis, so its frequency and octaves set how busy it looks;
	/// cellular noise returning cell values gives evenly spread random levels for a strobe.</summary>
	[Export] public FastNoiseLite Noise { get; set; }
	/// <summary>Noise units travelled per second.</summary>
	[Export] public float Speed { get; set; } = 1f;
	/// <summary>Multiplier at the low end of the noise.</summary>
	[Export] public float Min { get; set; } = .8f;
	/// <summary>Multiplier at the high end of the noise.</summary>
	[Export] public float Max { get; set; } = 1.2f;
	/// <summary>Reads the noise's magnitude, so the light rests near <see cref="Min"/> and flares toward
	/// <see cref="Max"/> in sharp bounces like a guttering torch, instead of wavering around the middle.</summary>
	[Export] public bool Fold { get; set; }
	/// <summary>Levels per second held as hard steps for a strobe; 0 keeps the flicker smooth.</summary>
	[Export(PropertyHint.Range, "0,60,0.5,or_greater")] public float StepRate { get; set; }

	/// <summary>The brightness multiplier at a light's clock time plus its start offset; 1 without noise.</summary>
	public float Sample(float time, float offset)
	{
		if (Noise == null) return 1f;
		float t = time + offset;
		if (StepRate > 0) t = Mathf.Floor(t * StepRate) / StepRate;
		float noise = Noise.GetNoise1D(t * Speed);
		return Mathf.Lerp(Min, Max, Fold ? Mathf.Abs(noise) : (noise + 1f) * .5f);
	}

	/// <summary>A cosmetic start offset seeded by the light's owner: it differs between lights spawned in the same
	/// frame and draws from no gameplay random stream.</summary>
	public static float RandomOffset(GodotObject owner)
	{
		using var random = new RandomNumberGenerator { Seed = owner.GetInstanceId() * 7919UL };
		return random.Randf() * OffsetRange;
	}
}