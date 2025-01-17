using Godot;

[Tool]
public partial class Level : Node
{
	private MapGenerator _mapGenerator;
	private PlayerSpawner _playerSpawner;

	public override void _Ready()
	{
		GD.Print("Initializing level...");
		_mapGenerator = GetNode<MapGenerator>("MapGenerator");
		if (!Engine.IsEditorHint())
		{
			_playerSpawner = GetNode<PlayerSpawner>("PlayerSpawner");
		}

		_mapGenerator.MapGenerated += OnMapGenerated;
		_mapGenerator.GenerateMap();
	}

	private void OnMapGenerated()
	{
		GD.Print("Map generated signal received");
		if (!Engine.IsEditorHint())
		{
			// Spawn the player
			Vector3 playerSpawnPoint = _mapGenerator.PlayerSpawnPoint;
			_playerSpawner.SpawnPlayer(playerSpawnPoint);
		}
	}
}
