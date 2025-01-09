using Godot;
using System;

public partial class FlickerLight : OmniLight3D
{
	[Export] public NoiseTexture2D noiseTexture;
	[Export] public float Min { get; set; } = 0.25f;
	[Export] public float Max { get; set; } = 1.0f;

	private float _timePassed = 0.0f;

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (noiseTexture == null)
		{
			GD.PrintErr("Noise texture is not set!");
			return;
		}
		_timePassed += (float)delta;

		var sampledNoise = Math.Abs(noiseTexture.Noise.GetNoise1D(_timePassed));
		LightEnergy = Min + sampledNoise * (Max - Min);
	}
}
