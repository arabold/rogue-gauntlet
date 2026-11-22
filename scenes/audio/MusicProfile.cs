using Godot;

/// <summary>One authored, loopable music stream and its level before the shared Music bus.</summary>
[GlobalClass]
public partial class MusicProfile : Resource
{
	[Export] public AudioStream Stream { get; set; }
	[Export(PropertyHint.Range, "-48,6,0.5")] public float GainDb { get; set; } = -10f;

	/// <summary>Rejects tracks that cannot sustain a seamless loop before a deck starts them.</summary>
	public bool Validate(out string error)
	{
		bool loops = Stream switch
		{
			AudioStreamOggVorbis ogg => ogg.Loop,
			AudioStreamMP3 mp3 => mp3.Loop,
			AudioStreamWav wav => wav.LoopMode != AudioStreamWav.LoopModeEnum.Disabled,
			_ => false
		};
		if (Stream == null || !loops || !float.IsFinite(GainDb))
		{
			error = "A loopable music stream and finite track gain are required.";
			return false;
		}

		error = null;
		return true;
	}
}