using Godot;

/// <summary>
/// Ambient magic on a fountain's pool: sparkles rising off the liquid, a drifting surface
/// shimmer and a soft glow, all in one <see cref="Tint"/>. Place the node at the center of
/// the liquid surface and scale its X/Z axes to the pool radius in metres; the sparkle
/// shader sizes its stars from the vertical axis only, so stretching never distorts them.
/// Purely presentation: no gameplay state, and its particles use no gameplay randomness.
/// </summary>
[Tool]
[GlobalClass]
public partial class FountainSparkles : Node3D
{
	private static readonly StringName TintParameter = "tint";

	private Color _tint = Colors.White;

	/// <summary>Particles rising off the pool; their draw material reads the instance tint.</summary>
	[Export] public GpuParticles3D Sparkles { get; set; }

	/// <summary>Additive shimmer disc just above the liquid surface.</summary>
	[Export] public GeometryInstance3D Shimmer { get; set; }

	/// <summary>Optional light that spills the liquid's color onto the surrounding stone.</summary>
	[Export] public OmniLight3D Glow { get; set; }

	/// <summary>Liquid color shared by the sparkles, shimmer and glow.</summary>
	[Export]
	public Color Tint
	{
		get => _tint;
		set
		{
			_tint = value;
			if (!IsNodeReady()) return;
			ApplyTint();
		}
	}

	public override void _Ready() => ApplyTint();

	private void ApplyTint()
	{
		Sparkles?.SetInstanceShaderParameter(TintParameter, _tint);
		Shimmer?.SetInstanceShaderParameter(TintParameter, _tint);
		if (Glow != null)
			Glow.LightColor = _tint;
	}
}