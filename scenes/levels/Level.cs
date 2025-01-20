using Godot;

[Tool]
public partial class Level : Node
{
	public MapGenerator MapGenerator { get; private set; }

	public override void _Ready()
	{
		GD.Print("Initializing level...");
		MapGenerator = GetNode<MapGenerator>("MapGenerator");
		MapGenerator.GenerateMap();

		GD.Print("Level is ready");
		SignalBus.EmitLevelLoaded(this);
	}
}
