using Godot;
using System;

/// <summary>
/// Starting point for the player character in the map
/// </summary>
[Tool]
public partial class PlayerSpawnPoint : Node3D
{
	[Export] public GameSession.LevelTravelDirection SpawnAfterTravelDirection { get; set; } = GameSession.LevelTravelDirection.None;
	[Export] public Vector3 AutoWalkDirection { get; set; } = Vector3.Zero;
	[Export] public double AutoWalkSeconds { get; set; } = 0.75;

	public SpawnPoint SpawnPoint;

	public override void _Ready()
	{
		base._Ready();

		SpawnPoint = GetNode<SpawnPoint>("SpawnPoint");
		SpawnPoint.SpawnOnStart = false;

		if (!Engine.IsEditorHint())
		{
			this.SubscribeUntilExit(
				SignalBus.Instance,
				signalBus => signalBus.LevelLoaded += OnLevelLoaded,
				signalBus => signalBus.LevelLoaded -= OnLevelLoaded);
		}
	}

	public Player Spawn()
	{
		var travelDirection = GameSession.Instance?.PendingTravelDirection ?? GameSession.LevelTravelDirection.None;
		var player = SpawnPoint.Spawn() as Player;
		if (player == null)
		{
			return null;
		}

		GameSession.Instance?.RegisterStairArrival(player);
		if (AutoWalkDirection != Vector3.Zero
			&& (GameSession.Instance == null
				|| travelDirection != GameSession.LevelTravelDirection.None
				|| GameSession.Instance.ActiveDungeonDepth == 1 && SpawnAfterTravelDirection == GameSession.LevelTravelDirection.None))
		{
			StairAutoWalk.Start(player, GlobalTransform.Basis * AutoWalkDirection, AutoWalkSeconds);
		}

		SignalBus.EmitPlayerSpawned(player);

		return player;
	}

	private void OnLevelLoaded(Level level)
	{
		var travelDirection = GameSession.Instance?.PendingTravelDirection ?? GameSession.LevelTravelDirection.None;
		bool shouldSpawn = travelDirection == SpawnAfterTravelDirection
			|| travelDirection == GameSession.LevelTravelDirection.Down
				&& SpawnAfterTravelDirection == GameSession.LevelTravelDirection.None;

		if (!shouldSpawn)
		{
			return;
		}

		Spawn();
	}
}
