using Godot;

public partial class Preview : SubViewport
{
	[Signal] public delegate void TextureBakedEventHandler(Texture2D texture);
	[Export] public PackedScene Scene { get; private set; }

	private Node3D _object;
	private Camera3D _camera;

	public override void _Ready()
	{
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
		Aabb aabb = new Aabb();
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
		Aabb aabb = ComputeAABB(_object);
		Vector3 ofs = aabb.GetCenter();

		_object.Translate(ofs);
		_camera.Size = Mathf.Max(aabb.Size.X, aabb.Size.Y) * 1.5f;
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
