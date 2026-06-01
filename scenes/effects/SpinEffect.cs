using Godot;

/// <summary>
/// Rotates a visual node so projectile meshes feel physically thrown instead of static.
/// </summary>
public partial class SpinEffect : Node3D
{
	[Export] public Vector3 RotationSpeedDegrees { get; set; } = new(260.0f, 420.0f, 180.0f);

	public override void _Process(double delta)
	{
		Rotation += new Vector3(
			Mathf.DegToRad(RotationSpeedDegrees.X),
			Mathf.DegToRad(RotationSpeedDegrees.Y),
			Mathf.DegToRad(RotationSpeedDegrees.Z)) * (float)delta;
	}
}
