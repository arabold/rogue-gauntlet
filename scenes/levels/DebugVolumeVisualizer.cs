using Godot;

/// <summary>
/// Generates a preview mesh representing the boundaries of its parent Area3D's BoxShape3D.
/// </summary>
[Tool]
public partial class DebugVolumeVisualizer : MeshInstance3D
{
	[Export] public Color DebugColor { get; set; } = new Color(1, 0, 0, 0.3f);

	public override void _Ready()
	{
		UpdatePreview();
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			UpdatePreview();
		}
	}

	private void UpdatePreview()
	{
		var parent = GetParent() as Area3D;
		if (parent == null)
		{
			return;
		}

		var collisionShape = parent.FindChild("*CollisionShape*", true, false) as CollisionShape3D;
		if (collisionShape == null || collisionShape.Shape is not BoxShape3D boxShape)
		{
			return;
		}

		// Configure Mesh
		if (Mesh is not BoxMesh boxMesh)
		{
			boxMesh = new BoxMesh();
			Mesh = boxMesh;
		}

		boxMesh.Size = boxShape.Size;
		Transform = collisionShape.Transform;

		// Configure Material
		if (MaterialOverride is not StandardMaterial3D material)
		{
			material = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled
			};
			MaterialOverride = material;
		}

		if (material.AlbedoColor != DebugColor)
		{
			material.AlbedoColor = DebugColor;
		}
	}
}
