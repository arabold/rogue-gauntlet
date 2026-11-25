using Godot;

/// <summary>Stateless, spatially coherent indoor drafts. Neighboring props sample the same slowly changing force.</summary>
[Tool] // Exported by the tool Airflow script: a tool script's exports must be tool types in the editor.
[GlobalClass]
public partial class AirflowProfile : ObservableResource
{
	[Export] public Vector2 Direction { get; set => SetValue(ref field, value); } = new(.35f, 1);
	[Export(PropertyHint.Range, "0,2,0.01")] public float Strength { get; set => SetValue(ref field, Mathf.Max(0, value)); } = .08f;
	[Export(PropertyHint.Range, "0,2,0.01")] public float GustStrength { get; set => SetValue(ref field, Mathf.Max(0, value)); } = .95f;
	[Export(PropertyHint.Range, "2,30,0.1")] public float GustPeriod { get; set => SetValue(ref field, Mathf.Max(2, value)); } = 7.5f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float SpatialScale { get; set => SetValue(ref field, Mathf.Max(0, value)); } = .12f;
	[Export(PropertyHint.Range, "0,30,0.5")] public float DirectionVariationDegrees { get; set => SetValue(ref field, Mathf.Clamp(value, 0, 30)); } = 12;

	public Vector2 Sample(Vector3 position, float time)
	{
		float phase = (position.X * .7f + position.Z * .4f) * SpatialScale;
		float t = time * Mathf.Tau / Mathf.Max(2, GustPeriod);
		float wave = .62f * Mathf.Sin(t + phase) + .28f * Mathf.Sin(t * 1.71f + phase * .8f + 1.9f)
			+ .1f * Mathf.Sin(t * .43f - phase + .6f);
		float gust = Mathf.SmoothStep(-.75f, .8f, wave);
		float angle = Mathf.DegToRad(DirectionVariationDegrees) * Mathf.Sin(t * .39f + phase);
		return Direction.Normalized().Rotated(angle) * Mathf.Max(0, Strength + GustStrength * gust);
	}
}