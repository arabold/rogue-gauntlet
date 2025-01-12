using Godot;

public partial class PlayerSpawner : Node
{
	[Export] private PackedScene PlayerScene;

	[Signal] public delegate void PlayerSpawnedEventHandler(Player player);

	public Node3D SpawnPlayer(Vector3 spawnPosition, float rotation = 0)
	{
		if (PlayerScene == null)
		{
			GD.PrintErr("Player scene is not assigned!");
			return null;
		}

		var player = PlayerScene.Instantiate<Node3D>();
		AddChild(player);

		player.GlobalTransform = new Transform3D(Basis.Identity, spawnPosition);
		player.RotateY(Mathf.DegToRad(rotation));

		EmitSignal(SignalName.PlayerSpawned, player);

		GD.Print($"Player spawned at {spawnPosition}");
		return player;
	}
}
