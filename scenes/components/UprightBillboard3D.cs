using Godot;

/// <summary>
/// Turns a 3D visual around world Y to face the camera, so it stays upright and readable however its owner is turned:
/// the node counterpart of a Y-billboard material, for meshes whose shaders have no billboard mode of their own.
/// </summary>
[GlobalClass]
public partial class UprightBillboard3D : Node
{
	/// <summary>The visual to turn, explicitly assigned by its scene or creator.</summary>
	[Export] public Node3D Target { get; set; }

	private float _cameraYaw = float.NaN;
	private Basis _parentBasis;

	public override void _Ready()
	{
		if (Target == null)
		{
			SetProcess(false);
			return;
		}
		_Process(0);
	}

	public override void _Process(double delta)
	{
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null || !IsInstanceValid(Target)) return;
		float yaw = camera.GlobalRotation.Y;
		Basis parent = Target.GetParentNode3D()?.GlobalBasis ?? Basis.Identity;
		// Neither the camera nor the owner turns most frames; an unchanged rotation would still propagate a transform change.
		if (yaw == _cameraYaw && parent == _parentBasis) return;
		_cameraYaw = yaw;
		_parentBasis = parent;
		Target.GlobalRotation = new Vector3(0, yaw, 0);
	}
}