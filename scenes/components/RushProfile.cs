using Godot;

/// <summary>Immutable timing, distance and animation style for a movement-owned rush.</summary>
[GlobalClass]
public partial class RushProfile : Resource
{
	[Export] public StringName Style { get; set; } = "rush";
	[Export(PropertyHint.Range, "0,3,0.01,suffix:s")] public float WindUpDuration { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0.1,20,0.1,suffix:m")] public float Distance { get; set; } = 4;
	[Export(PropertyHint.Range, "0.05,3,0.01,suffix:s")] public float TravelDuration { get; set; } = 0.55f;
	[Export(PropertyHint.Range, "0,3,0.01,suffix:s")] public float RecoveryDuration { get; set; } = 0.3f;

	/// <summary>Reject incomplete timing or a missing animation style before acquiring action ownership.</summary>
	public bool IsValid => !string.IsNullOrWhiteSpace(Style)
		&& float.IsFinite(WindUpDuration) && WindUpDuration >= 0
		&& float.IsFinite(Distance) && Distance > 0
		&& float.IsFinite(TravelDuration) && TravelDuration > 0
		&& float.IsFinite(RecoveryDuration) && RecoveryDuration >= 0;
}
