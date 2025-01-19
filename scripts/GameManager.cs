using Godot;
using Godot.Collections;
using System;
using System.Linq;

public partial class GameManager : Node
{
	// Singleton instance for global access
	public static GameManager Instance { get; private set; }

	public Array<Player> PlayersInScene { get; private set; }
	public Array<EnemyBase> EnemiesInScene { get; private set; }
	public Array<Node3D> DamageablesInScene { get; private set; }

	public Random Random { get; private set; } = new Random();
	public Player Player { get; private set; }
	public PlayerStats PlayerStats { get; private set; }
	public Level Level { get; private set; }

	public override void _Ready()
	{
		// Ensure this is the only instance
		if (Instance != null)
		{
			GD.PrintErr("Multiple instances of GameManager detected!");
			QueueFree();
			return;
		}

		Instance = this;
		PlayerStats = GD.Load<PlayerStats>("res://scenes/player/resources/player_stats.tres");

		// Connect to signals
		OnSceneTreeChanged();
		GetTree().TreeChanged += OnSceneTreeChanged;

		SignalBus.Instance.PlayerSpawned += player => Player = player;
		SignalBus.Instance.LevelLoaded += level => Level = level;

		GD.Print("GameManager initialized.");
	}

	// Method to update cooldown status
	public void UpdateCooldown(int slotIndex, float remainingTime, float totalTime)
	{
		SignalBus.EmitCooldownUpdated(slotIndex, remainingTime, totalTime);
	}

	// Restart the game
	public void RestartGame()
	{
		GD.Print("Restarting Game...");

		// Load the main scene
		GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
	}

	private void OnSceneTreeChanged()
	{
		var tree = GetTree();
		if (tree != null)
		{
			PlayersInScene = new Array<Player>(tree.GetNodesInGroup("player").Cast<Player>().ToArray());
			EnemiesInScene = new Array<EnemyBase>(tree.GetNodesInGroup("enemy").Cast<EnemyBase>().ToArray());
			DamageablesInScene = new Array<Node3D>(tree.GetNodesInGroup("damageable").Cast<Node3D>().ToArray());
		}
	}
}
