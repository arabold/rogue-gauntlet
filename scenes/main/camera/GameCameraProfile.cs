using Godot;

/// <summary>
/// The game's view of the hero: an orthographic camera at a fixed angle and distance, with its zoom range and the speed
/// of the rotate and zoom actions. The game camera, the combat arena and the workbenches' game view all read it, so every
/// scene shows the hero from the same perspective.
/// </summary>
[GlobalClass]
public partial class GameCameraProfile : Resource
{
	/// <summary>Initial heading around the hero, in degrees; 45 looks along the dungeon's diagonal.</summary>
	[Export(PropertyHint.Range, "-180,180,1")] public float YawDegrees { get; set; } = 45;

	/// <summary>How far the camera looks down, in degrees.</summary>
	[Export(PropertyHint.Range, "10,89,1")] public float PitchDegrees { get; set; } = 50;

	/// <summary>Distance from the hero along the view direction; orthographic, so it only affects near-plane clipping.</summary>
	[Export] public float Distance { get; set; } = 15;

	/// <summary>Visible height of the orthographic view, in metres.</summary>
	[Export] public float Size { get; set; } = 20;

	[Export] public float MinSize { get; set; } = 10;
	[Export] public float MaxSize { get; set; } = 32;

	/// <summary>Turn speed of the camera rotate actions, in degrees per second.</summary>
	[Export] public float RotationDegreesPerSecond { get; set; } = 180;

	/// <summary>Change of <see cref="Size"/> per second while a zoom action is held.</summary>
	[Export] public float ZoomPerSecond { get; set; } = 12;

	/// <summary>The view's starting orientation; the camera looks along its negative Z axis.</summary>
	public Basis ViewBasis =>
		new Basis(Vector3.Up, Mathf.DegToRad(YawDegrees)) * new Basis(Vector3.Right, -Mathf.DegToRad(PitchDegrees));

	/// <summary>Where the camera stands to look at <paramref name="target"/> with <paramref name="basis"/>.</summary>
	public Vector3 ViewPosition(Vector3 target, Basis basis) => target + basis.Z * Distance;
}
