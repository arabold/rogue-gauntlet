using Godot;

/// <summary>
/// A presentation-only camera shake with a falloff radius and an envelope. Gameplay sources (area spells, slams)
/// reference one profile and request it at a world position; the camera controller arbitrates against active shakes.
/// </summary>
[Tool]
[GlobalClass]
public partial class CameraShakeProfile : Resource
{
	/// <summary>Impulse at the source; it fades linearly to nothing at <see cref="Radius"/>.</summary>
	[Export(PropertyHint.Range, "0,3,0.01")] public float Strength { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "0.1,40,0.1,suffix:m")] public float Radius { get; set; } = 10;
	[Export(PropertyHint.Range, "0,1,0.01,suffix:s")] public float GrowthTime { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01,suffix:s")] public float HoldTime { get; set; }
	[Export(PropertyHint.Range, "0.01,2,0.01,suffix:s")] public float DecayTime { get; set; } = 0.18f;

	public bool IsValid => float.IsFinite(Strength) && Strength > 0 && float.IsFinite(Radius) && Radius > 0
		&& float.IsFinite(GrowthTime) && GrowthTime >= 0 && float.IsFinite(HoldTime) && HoldTime >= 0
		&& float.IsFinite(DecayTime) && DecayTime > 0;

	/// <summary>Request this shake at <paramref name="position"/>.</summary>
	public void EmitAt(Vector3 position)
	{
		var request = new CameraImpulseRequest(Strength, Radius, position, GrowthTime, HoldTime, DecayTime);
		SignalBus.EmitCameraImpulseRequested(in request);
	}
}
