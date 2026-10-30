using Godot;

/// <summary>
/// Keeps a floating UI visual at its authored screen size while an orthographic camera zooms.
/// Only scales the visual; its world-space anchor continues to follow its actor or prop.
/// </summary>
[GlobalClass]
public partial class ConstantScreenSize3D : Node
{
	/// <summary>The label or health-bar visual to scale, explicitly assigned by its scene or creator.</summary>
	[Export] public Node3D Target { get; set; }

	/// <summary>Camera size at which the target's authored scale is used (the gameplay default is 20).</summary>
	[Export(PropertyHint.Range, "0.001,100,0.001,or_greater")]
	public float ReferenceCameraSize { get; set; } = 20f;

	private Vector3 _authoredScale;
	private float _factor = float.NaN;

	public override void _Ready()
	{
		// Main updates zoom at priority 0; compensate before PhantomCameraHost renders at 300.
		ProcessPriority = 100;
		if (Target == null || ReferenceCameraSize <= 0f)
		{
			SetProcess(false);
			return;
		}
		_authoredScale = Target.Scale;
		_Process(0);
	}

	/// <summary>Compensates from the original scale so repeated zoom changes never accumulate drift.</summary>
	public override void _Process(double delta)
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null || !IsInstanceValid(Target)) return;
		float factor = camera.Projection == Camera3D.ProjectionType.Orthogonal ? camera.Size / ReferenceCameraSize : 1f;
		// Zoom changes only on input; an unchanged scale would still propagate a transform change every frame.
		if (factor == _factor) return;
		_factor = factor;
		Target.Scale = _authoredScale * factor;
	}
}