using Godot;

/// <summary>Timing, distance and animation style of a committed dash: wind-up, travel, recovery.</summary>
[Tool]
[GlobalClass]
public partial class DashProfile : Resource
{
	/// <summary>Attack animation style whose clips pose the wind-up and travel; recovery uses the gate's recovery clip.</summary>
	[Export] public StringName Style { get; set; } = "rush";
	[Export(PropertyHint.Range, "0,3,0.01,suffix:s")] public float WindUpDuration { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0.1,20,0.1,suffix:m")] public float Distance { get; set; } = 4;
	[Export(PropertyHint.Range, "0.05,3,0.01,suffix:s")] public float TravelDuration { get; set; } = 0.55f;
	[Export(PropertyHint.Range, "0,3,0.01,suffix:s")] public float RecoveryDuration { get; set; } = 0.3f;

	/// <summary>Optional travel sound, played only once the dash actually displaces its actor.</summary>
	[Export] public SoundCueProfile TravelCue { get; set; }

	public float TravelSpeed => Distance / TravelDuration;

	/// <summary>Reject incomplete timing or a missing animation style before acquiring action ownership.</summary>
	public bool IsValid => !string.IsNullOrWhiteSpace(Style)
		&& float.IsFinite(WindUpDuration) && WindUpDuration >= 0
		&& float.IsFinite(Distance) && Distance > 0
		&& float.IsFinite(TravelDuration) && TravelDuration > 0
		&& float.IsFinite(RecoveryDuration) && RecoveryDuration >= 0;
}