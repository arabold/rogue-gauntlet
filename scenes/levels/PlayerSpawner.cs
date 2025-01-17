using Godot;

public partial class PlayerSpawner : Node
{
	[Export] private PackedScene PlayerScene;

	public override void _Ready()
	{
		base._Ready();
		SignalBus.Instance.LevelLoaded += OnLevelLoaded;
	}

	public Node3D SpawnPlayer(Vector3 spawnPosition, float rotation = 0)
	{
		if (PlayerScene == null)
		{
			GD.PrintErr("Player scene is not assigned!");
			return null;
		}

		GD.Print("Spawning player...");
		var player = PlayerScene.Instantiate<Player>();
		AddChild(player);

		player.GlobalTransform = new Transform3D(Basis.Identity, spawnPosition);
		player.RotateY(Mathf.DegToRad(rotation));

		GD.Print($"Player spawned at {spawnPosition}");
		SignalBus.EmitPlayerSpawned(player);

		return player;
	}

	private void OnLevelLoaded(Level level)
	{
		GD.Print("Level loaded. Spawning player...");
		Vector3 playerSpawnPoint = level.MapGenerator.PlayerSpawnPoint;
		SpawnPlayer(playerSpawnPoint);
	}
}
