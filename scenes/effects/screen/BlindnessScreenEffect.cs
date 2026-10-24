using Godot;

/// <summary>
/// Blindness overlay that keeps a small circle of gray sight around a world-space <see cref="Focus"/>
/// (normally the player). The sight radius is authored in world units and re-projected every frame, so it
/// stays the same physical size regardless of camera zoom, rotation or the focus moving on screen.
/// </summary>
[GlobalClass]
public partial class BlindnessScreenEffect : ScreenEffect
{
	private static readonly StringName FocusParameter = "focus_uv";
	private static readonly StringName RadiusParameter = "sight_radius";

	/// <summary>
	/// Node the remaining sight is centered on. Runtime context: assign it when the focus actor spawns.
	/// Without a focus the sight stays centered on the screen.
	/// </summary>
	[Export] public Node3D Focus { get; set; }

	/// <summary>Radius of the remaining sight in world units.</summary>
	[Export(PropertyHint.Range, "0.5,20,0.1,suffix:m")] public float SightRadius { get; set; } = 3f;

	/// <summary>Height above the focus origin to center on, e.g. the actor's chest instead of its feet.</summary>
	[Export(PropertyHint.Range, "0,3,0.05,suffix:m")] public float FocusHeight { get; set; } = 0.8f;

	public override void _Process(double delta)
	{
		ShaderMaterial material = EffectMaterial;
		Camera3D camera = GetViewport().GetCamera3D();
		if (!Visible || material == null || camera == null || !IsInstanceValid(Focus))
		{
			return;
		}

		Vector3 center = Focus.GlobalPosition + (Vector3.Up * FocusHeight);
		if (camera.IsPositionBehind(center))
		{
			return;
		}

		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 screenCenter = camera.UnprojectPosition(center);
		Vector2 screenEdge = camera.UnprojectPosition(center + (camera.GlobalBasis.X * SightRadius));

		material.SetShaderParameter(FocusParameter, screenCenter / viewportSize);
		material.SetShaderParameter(RadiusParameter, screenCenter.DistanceTo(screenEdge) / viewportSize.Y);
	}
}