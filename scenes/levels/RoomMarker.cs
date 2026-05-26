using Godot;

/// <summary>
/// Base class for editor-visible semantic markers authored inside room scenes.
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomMarker : Node3D
{
	private const string VisualTileName = "__MarkerTile";
	private const string VisualLabelName = "__MarkerLabel";

	[Export]
	public bool Enabled
	{
		get;
		set
		{
			field = value;
			UpdateEditorVisual();
		}
	} = true;

	/// <summary>
	/// Optional editor label override. Leave empty to use the marker type label. A value of "&" hides the label.
	/// </summary>
	[Export]
	public string EditorLabel
	{
		get;
		set
		{
			field = value;
			UpdateEditorVisual();
		}
	} = "";

	[Export(PropertyHint.Range, "0.5,2.0,0.1")]
	public float EditorVisualScale
	{
		get;
		set
		{
			field = value;
			UpdateEditorVisual();
		}
	} = 1f;

	public override void _Ready()
	{
		UpdateEditorVisual();
	}

	protected virtual string GetDefaultEditorLabel()
	{
		return "Marker";
	}

	protected virtual Color GetEditorColor()
	{
		return new Color(0.7f, 0.7f, 0.7f, 0.45f);
	}

	protected virtual Vector3 GetVisualSize()
	{
		return new Vector3(3.2f, 0.1f, 3.2f) * EditorVisualScale;
	}

	protected virtual Vector3 GetVisualOffset()
	{
		return new Vector3(-2f, 0.08f, -2f);
	}

	protected void UpdateEditorVisual()
	{
		if (!IsNodeReady())
		{
			return;
		}

		if (!Engine.IsEditorHint())
		{
			RemoveEditorVisual(VisualTileName);
			RemoveEditorVisual(VisualLabelName);
			return;
		}

		var tile = GetOrCreateEditorMesh(VisualTileName);
		tile.Visible = Enabled;
		tile.Mesh = new BoxMesh { Size = GetVisualSize() };
		tile.Position = GetVisualOffset();
		tile.MaterialOverride = CreateEditorMaterial(GetEditorColor());

		var label = GetOrCreateEditorLabel(VisualLabelName);
		label.Visible = Enabled && EditorLabel != "&";
		label.Text = string.IsNullOrWhiteSpace(EditorLabel) ? GetDefaultEditorLabel() : EditorLabel;
		var visualOffset = GetVisualOffset();
		label.Position = new Vector3(visualOffset.X, 1.25f * EditorVisualScale, visualOffset.Z);
		label.FontSize = 36;
		label.Modulate = GetEditorColor().Lightened(0.35f);
	}

	protected MeshInstance3D GetOrCreateEditorMesh(string nodeName)
	{
		var mesh = GetNodeOrNull<MeshInstance3D>(nodeName);
		if (mesh != null)
		{
			return mesh;
		}

		mesh = new MeshInstance3D { Name = nodeName };
		AddChild(mesh, false, InternalMode.Back);
		return mesh;
	}

	protected Label3D GetOrCreateEditorLabel(string nodeName)
	{
		var label = GetNodeOrNull<Label3D>(nodeName);
		if (label != null)
		{
			return label;
		}

		label = new Label3D { Name = nodeName };
		AddChild(label, false, InternalMode.Back);
		return label;
	}

	protected void RemoveEditorVisual(string nodeName)
	{
		var node = GetNodeOrNull<Node>(nodeName);
		if (node == null)
		{
			return;
		}

		RemoveChild(node);
		node.QueueFree();
	}

	protected StandardMaterial3D CreateEditorMaterial(Color color, bool transparent = true, bool noDepthTest = false)
	{
		var material = new StandardMaterial3D
		{
			AlbedoColor = color,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = noDepthTest,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};

		if (transparent)
		{
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		}

		return material;
	}
}
