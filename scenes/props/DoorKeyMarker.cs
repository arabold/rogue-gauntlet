using Godot;

/// <summary>
/// A visible lock cue that turns in place and takes its color from the required key. Its scene (<c>key_marker.tscn</c>)
/// shades it with the self-lit world marker shader and draws it above the fog
/// (<see cref="FloorDiscovery.AboveFogRenderPriority"/> for the ink, one above for the gem).
/// </summary>
[Tool]
[GlobalClass]
public partial class DoorKeyMarker : MeshInstance3D
{
	[Export(PropertyHint.Range, "-180,180,1,suffix:°/s")]
	public float TurnSpeed
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady() && !Engine.IsEditorHint()) UpdateProcessing();
		}
	} = 42f;

	private static readonly StringName BaseColor = "base_color";

	public override void _Ready()
	{
		// Godot enables processing for an overridden _Process before _Ready; an editor marker never turns.
		if (Engine.IsEditorHint())
		{
			SetProcess(false);
			return;
		}
		VisibilityChanged += UpdateProcessing;
		UpdateProcessing();
	}

	public override string[] _GetConfigurationWarnings() =>
		MaterialOverride is ShaderMaterial ? [] : ["Assign a world marker material override; the required key tints its base_color."];

	public override void _Process(double delta) =>
		RotateY(Mathf.DegToRad(TurnSpeed) * (float)delta);

	private void UpdateProcessing() => SetProcess(IsVisibleInTree() && !Mathf.IsZeroApprox(TurnSpeed));

	internal void SetTint(Color color) => SetInstanceShaderParameter(BaseColor, color);
}
