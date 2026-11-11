using Godot;
using System;

public enum SoundCueCategory
{
	World,
	Ui
}

/// <summary>
/// Immutable authored playback settings for one semantic sound cue.
/// </summary>
[Tool, GlobalClass]
public partial class SoundCueProfile : Resource
{
	[Export] public AudioStream[] Variants { get; set; } = [];
	[Export] public SoundCueCategory Category { get; set; } = SoundCueCategory.World;
	[Export(PropertyHint.Range, "-48,12,0.5")] public float GainDb { get; set; } = -6f;
	[Export(PropertyHint.Range, "0.5,1.5,0.01")] public float PitchMinimum { get; set; } = 0.96f;
	[Export(PropertyHint.Range, "0.5,1.5,0.01")] public float PitchMaximum { get; set; } = 1.04f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PanningStrength { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "0.1,20,0.1")] public float UnitSize { get; set; } = 3f;
	[Export(PropertyHint.Range, "1,100,1")] public float MaxDistance { get; set; } = 24f;
	[Export(PropertyHint.Range, "0,10,1")] public int Priority { get; set; }
	[Export(PropertyHint.Range, "1,16,1")] public int MaxVoicesPerSource { get; set; } = 2;
	[Export(PropertyHint.Range, "1,32,1")] public int MaxVoicesGlobal { get; set; } = 6;
	[Export(PropertyHint.Range, "0,1,0.01")] public float MinIntervalSeconds { get; set; } = 0.06f;

	/// <summary>Checks the authored values before the controller admits playback.</summary>
	public bool Validate(out string error)
	{
		if (Variants == null || Variants.Length == 0)
		{
			error = "At least one audio stream variant is required.";
			return false;
		}

		foreach (AudioStream stream in Variants)
		{
			if (stream == null)
			{
				error = "Audio stream variants cannot contain null entries.";
				return false;
			}

			bool loops = stream switch
			{
				AudioStreamOggVorbis ogg => ogg.Loop,
				AudioStreamMP3 mp3 => mp3.Loop,
				AudioStreamWav wav => wav.LoopMode != AudioStreamWav.LoopModeEnum.Disabled,
				_ => false
			};
			if (loops)
			{
				error = "One-shot cue variants cannot loop.";
				return false;
			}
		}

		if (!Enum.IsDefined(Category)
			|| !float.IsFinite(GainDb)
			|| !float.IsFinite(PitchMinimum)
			|| !float.IsFinite(PitchMaximum)
			|| PitchMinimum <= 0f
			|| PitchMaximum < PitchMinimum
			|| !float.IsFinite(PanningStrength)
			|| PanningStrength < 0f
			|| PanningStrength > 1f
			|| !float.IsFinite(UnitSize)
			|| UnitSize <= 0f
			|| !float.IsFinite(MaxDistance)
			|| MaxDistance <= 0f
			|| Priority < 0
			|| MaxVoicesPerSource <= 0
			|| MaxVoicesGlobal <= 0
			|| !float.IsFinite(MinIntervalSeconds)
			|| MinIntervalSeconds < 0f)
		{
			error = "Cue gain, pitch, panning, distance, priority, voice limits or interval are invalid.";
			return false;
		}

		error = null;
		return true;
	}
}