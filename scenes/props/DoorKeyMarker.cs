using Godot;

/// <summary>A visible lock cue that turns in place and takes its color from the required key.</summary>
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
		if (Engine.IsEditorHint()) return;
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
