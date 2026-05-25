using Godot;

[Tool]
public partial class Level : Node
{
	public MapGenerator MapGenerator { get; private set; }

	public void AddWorldNode(Node node)
	{
		AddChild(node);
	}

	public void AddWorldNode(Node3D node, Vector3 globalPosition)
	{
		AddWorldNode(node);
		node.GlobalPosition = globalPosition;
	}

	public override void _Ready()
	{
		MapGenerator = GetNode<MapGenerator>("MapGenerator");
		if (Engine.IsEditorHint())
		{
			return;
		}

		GD.Print("Initializing level...");
		MapGenerator.GenerateMap();

		GD.Print("Level is ready");
		SignalBus.EmitLevelLoaded(this);
	}
}
