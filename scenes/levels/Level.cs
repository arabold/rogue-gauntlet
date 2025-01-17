using Godot;

[Tool]
public partial class Level : Node
{
	public MapGenerator MapGenerator { get; private set; }
	public PlayerSpawner PlayerSpawner { get; private set; }

	public override void _Ready()
	{
		GD.Print("Initializing level...");
		MapGenerator = GetNode<MapGenerator>("MapGenerator");
		if (!Engine.IsEditorHint())
		{
			PlayerSpawner = GetNode<PlayerSpawner>("PlayerSpawner");
		}

		MapGenerator.GenerateMap();

		GD.Print("Level is ready");
		SignalBus.EmitLevelLoaded(this);
	}
}
