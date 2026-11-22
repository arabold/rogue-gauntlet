using Godot;
using System;

/// <summary>
/// One short positional voice that returns to the shared scene pool when finished.
/// </summary>
public partial class WorldSound : AudioStreamPlayer3D, IPooledNode
{
	private Action<WorldSound> _finishedCallback;

	public override void _Ready()
	{
		Finished += OnFinished;
	}

	/// <summary>Configures and starts a world cue with values sampled by the audio controller.</summary>
	public void PlayCue(AudioStream stream, SoundCueProfile profile, Vector3 position, float pitch, Action<WorldSound> finishedCallback)
	{
		Stream = stream;
		Bus = "Effects";
		VolumeDb = profile.GainDb;
		PitchScale = pitch;
		PanningStrength = profile.PanningStrength;
		UnitSize = profile.UnitSize;
		MaxDistance = profile.MaxDistance;
		GlobalPosition = position;
		_finishedCallback = finishedCallback;
		Play();
	}

	public void OnSpawnedFromPool() => ResetVoice();
	public void OnDespawnedToPool() => ResetVoice();

	private void ResetVoice()
	{
		Stop();
		Stream = null;
		_finishedCallback = null;
	}

	private void OnFinished()
	{
		_finishedCallback?.Invoke(this);
	}
}