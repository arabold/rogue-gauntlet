using Godot;

[Tool]
public partial class Preview : SubViewport
{
	[Signal] public delegate void TextureBakedEventHandler(Texture2D texture);
	[Export] public PackedScene Scene { get; private set; }

	private Node3D _pivot;
	private Node3D _object;
	private Camera3D _camera;
	private Color? _tint;

	public override void _Ready()
	{
		OwnWorld3D = true;

		_camera = GetNode<Camera3D>("Camera3D");
		Refresh();
	}

	public void SetScene(PackedScene scene)
	{
		Scene = scene;
		_tint = null;
		Refresh();
	}

	public void SetScene(PackedScene scene, Color? tint)
	{
		Scene = scene;
		_tint = tint;
		Refresh();
	}

	private Aabb ComputeAABB(Node3D node)
	{
		Aabb aabb = new();
		if (node is VisualInstance3D visualInstance)
		{
			aabb = visualInstance.GlobalTransform * visualInstance.GetAabb();
		}
		foreach (Node child in node.GetChildren())
		{
			if (child is Node3D childNode)
			{
				Aabb childAabb = ComputeAABB(childNode);
				aabb = aabb.Merge(childAabb);
			}
		}
		return aabb;
	}

	/// <summary>
	/// Rotates/centers <paramref name="pivot"/> to frame <paramref name="model"/> in the
	/// camera. Acts on a wrapper pivot rather than the model itself: some item scenes bake a
	/// non-identity scale into their own root (e.g. the warhammers), and
	/// RotateObjectLocal/TranslateObjectLocal move a node along its own basis — on a scaled
	/// root, that basis scales the correction too, overshooting and clipping the model. The
	/// pivot always starts at identity, so framing is correct regardless of the loaded
	/// scene's own root transform.
	/// </summary>
	private void CenterObjectToCamera(Node3D model, Node3D pivot)
	{
		var aabb = ComputeAABB(model);
		var distance = Mathf.Max(aabb.Size.X, aabb.Size.Y);

		// If the object is lying down, rotate it to stand up
		if (aabb.Size.X > aabb.Size.Y)
		{
			pivot.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2);
			distance = Mathf.Max(aabb.Size.Z, aabb.Size.Y);
		}
		else if (aabb.Size.Z > aabb.Size.Y)
		{
			pivot.RotateObjectLocal(Vector3.Forward, Mathf.Pi / 2);
			distance = Mathf.Max(aabb.Size.X, aabb.Size.Z);
		}

		// Center the object to the camera
		var center = aabb.GetCenter();
		pivot.TranslateObjectLocal(-center);
		_camera.Size = distance * 1.25f;
	}

	public void Refresh()
	{
		_pivot?.QueueFree();
		_pivot = null;
		_object = Scene?.Instantiate<Node3D>();
		if (_object != null && _camera != null)
		{
			GD.Print($"Rendering preview for {_object.Name}");
			_pivot = new Node3D();
			AddChild(_pivot);
			_pivot.AddChild(_object);
			if (_tint.HasValue)
			{
				ItemIdentity.ApplyTint(_object, _tint.Value);
			}

			CenterObjectToCamera(_object, _pivot);
		}
	}
}
