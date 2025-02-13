using Godot;

[Tool]
public partial class Preview : SubViewport
{
	[Signal] public delegate void TextureBakedEventHandler(Texture2D texture);
	[Export] public PackedScene Scene { get; private set; }

	private Node3D _object;
	private Camera3D _camera;

	public override void _Ready()
	{
		OwnWorld3D = true;

		_camera = GetNode<Camera3D>("Camera3D");
		Refresh();
	}

	public void SetScene(PackedScene scene)
	{
		Scene = scene;
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

	private void CenterObjectToCamera()
	{
		var aabb = ComputeAABB(_object);
		var distance = Mathf.Max(aabb.Size.X, aabb.Size.Y);

		// If the object is lying down, rotate it to stand up
		if (aabb.Size.X > aabb.Size.Y)
		{
			_object.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2);
			distance = Mathf.Max(aabb.Size.Z, aabb.Size.Y);
		}
		else if (aabb.Size.Z > aabb.Size.Y)
		{
			_object.RotateObjectLocal(Vector3.Forward, Mathf.Pi / 2);
			distance = Mathf.Max(aabb.Size.X, aabb.Size.Z);
		}

		// Center the object to the camera
		var center = aabb.GetCenter();
		_object.TranslateObjectLocal(-center);
		_camera.Size = distance * 1.25f;
	}

	public void Refresh()
	{
		_object?.QueueFree();
		_object = Scene?.Instantiate<Node3D>();
		if (_object != null && _camera != null)
		{
			GD.Print($"Rendering preview for {_object.Name}");
			AddChild(_object);
			CenterObjectToCamera();
		}
	}
}
