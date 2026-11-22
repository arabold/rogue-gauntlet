using Godot;

/// <summary>
/// A visible lock cue that turns in place and takes its color from the required key. Its scene draws it in the
/// transparent pass after the fog (<see cref="FloorDiscovery.AboveFogRenderPriority"/> for the ink, one above for the
/// gem) without a depth test: the fog shades whatever the depth buffer holds, so an opaque gem hovering over the
/// unexplored side of its door would darken. The door shows it only once the door's area is revealed.
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

	private StandardMaterial3D _finish;

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
		MaterialOverride is StandardMaterial3D ? [] : ["Assign a StandardMaterial3D override to tint from the required key."];

	public override void _Process(double delta) =>
		RotateY(Mathf.DegToRad(TurnSpeed) * (float)delta);

	private void UpdateProcessing() => SetProcess(IsVisibleInTree() && !Mathf.IsZeroApprox(TurnSpeed));

	internal void SetTint(Color color)
	{
		if (_finish == null)
		{
			if (MaterialOverride is not StandardMaterial3D material) return;
			_finish = (StandardMaterial3D)material.Duplicate();
			MaterialOverride = _finish;
		}
		_finish.AlbedoColor = color;
		_finish.Emission = color;
	}
}
