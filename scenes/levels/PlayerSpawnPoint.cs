using Godot;
using System;

/// <summary>
/// Starting point for the player character in the map
/// </summary>
[Tool]
public partial class PlayerSpawnPoint : Node3D, IProgressionAnchor
{
	[Export] public GameSession.LevelTravelDirection SpawnAfterTravelDirection { get; set; } = GameSession.LevelTravelDirection.Down;
	[Export] public Vector3 AutoWalkDirection { get; set; } = Vector3.Zero;
	/// <summary>Where an arriving hero walks off the stairs, in world space.</summary>
	public Vector3 WorldAutoWalkDirection => GlobalTransform.Basis * AutoWalkDirection;
	/// <summary>How far an arrival walks off the stairs, in meters, before the player takes control.</summary>
	[Export(PropertyHint.Range, "0,20,0.1,suffix:m")] public float AutoWalkDistance { get; set; } = 3.4f;
	/// <summary>Where an arrival steps off the stairs onto the floor; floor reachability floods from here. Unset, from the spawn point.</summary>
	[Export] public Node3D Landing { get; set; }
	/// <summary>Where an arrival first stands on the floor: the <see cref="Landing"/>, or the spawn point without one.</summary>
	public Vector3 LandingPosition => (Landing ?? this).GlobalPosition;

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
		bool hasPendingTravel = GameSession.Instance == null || GameSession.Instance.PendingTravelDirection.HasValue;
		var player = SpawnPoint.Spawn() as Player;
		if (player == null)
		{
			return null;
		}

		GameSession.Instance?.RegisterStairArrival(player);
		if (AutoWalkDirection != Vector3.Zero && hasPendingTravel)
		{
			StairAutoWalk.Start(player, WorldAutoWalkDirection, AutoWalkDistance);
		}

		SignalBus.EmitPlayerSpawned(player);

		return player;
	}

	private void OnLevelLoaded(Level level)
	{
		if (level != this.GetAncestorOrNull<Level>()) return;
		var travelDirection = GameSession.Instance?.PendingTravelDirection ?? GameSession.LevelTravelDirection.Down;
		bool shouldSpawn = travelDirection == SpawnAfterTravelDirection;

		if (!shouldSpawn)
		{
			return;
		}

		Spawn();
	}
}