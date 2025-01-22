using Godot;
using System;

/// <summary>
/// Starting point for the player character in the map
/// </summary>
[Tool]
public partial class PlayerSpawnPoint : Node3D
{
	public SpawnPoint SpawnPoint;

	public override void _Ready()
	{
		base._Ready();

		SpawnPoint = GetNode<SpawnPoint>("SpawnPoint");
		SpawnPoint.SpawnOnStart = false;

		if (!Engine.IsEditorHint())
		{
			SignalBus.Instance.LevelLoaded += OnLevelLoaded;
		}
	}

	public Player Spawn()
	{
		var player = SpawnPoint.Spawn() as Player;
		SignalBus.EmitPlayerSpawned(player);

		return player;
	}

	private void OnLevelLoaded(Level level)
	{
		Spawn();
	}
}
