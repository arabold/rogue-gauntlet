using Godot;

/// <summary>A soft, view-dependent glint for a projected surface mark.</summary>
[GlobalClass]
public partial class CameraSheen : Node
{
	[Export] public SurfaceMark Mark { get; set; }
	[Export] public Color Highlight { get; set; } = new(.55f, .7f, .72f);
	[Export(PropertyHint.Range, "0,1,0.01")] public float Strength { get; set; } = .22f;
	/// <summary>Distance between glints; each mark selects a stable value in this range.</summary>
	[Export] public Vector2 SpacingRange { get; set; } = new(5f, 7f);
	/// <summary>Distance over which the glint fades in and out.</summary>
	[Export(PropertyHint.Range, "0.05,4,0.05,or_greater")] public float GlintLength { get; set; } = 2.5f;

	private Vector3 _lastLocation = Vector3.Inf;
	private float _variation;
	private Vector2 _direction;

	public override void _Process(double delta)
	{
		if (Mark?.Profile == null || GetViewport().GetCamera3D() is not { } camera) return;
		Vector3 location = Mark.GlobalPosition;
		if (location != _lastLocation)
		{
			_lastLocation = location;
			_variation = Mathf.PosMod(Mathf.Sin(location.X * 12.9898f + location.Z * 78.233f) * 43758.5453f, 1f);
			float angle = (_variation - .5f) * 1.2f;
			_direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
		}
		float minSpacing = Mathf.Max(.1f, Mathf.Min(SpacingRange.X, SpacingRange.Y));
		float maxSpacing = Mathf.Max(minSpacing, Mathf.Max(SpacingRange.X, SpacingRange.Y));
		float spacing = Mathf.Lerp(minSpacing, maxSpacing, _variation);
		// A fixed spatial pattern reverses naturally when the camera retraces its path.
		Vector3 offset = camera.GlobalPosition - location;
		float alongView = offset.X * _direction.X + offset.Z * _direction.Y;
		float phase = Mathf.PosMod(alongView + _variation * spacing, spacing);
		float fromCenter = Mathf.Abs(phase - spacing * .5f);
		float halfLength = Mathf.Max(.025f, Mathf.Min(GlintLength, spacing) * .5f);
		float sheen = 1f - Mathf.SmoothStep(0f, halfLength, fromCenter);
		Color tint = Mark.Profile.Tint;
		Color highlight = new(Highlight.R, Highlight.G, Highlight.B, tint.A);
		Color result = tint.Lerp(highlight, Mathf.Clamp(sheen * Strength, 0f, 1f));
		if (Mark.Modulate != result) Mark.Modulate = result;
	}
}