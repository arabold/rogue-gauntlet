using Godot;
using Godot.Collections;
using System;
using System.Linq;

public partial class GameManager : Node
{
	// Singleton instance for global access
	public static GameManager Instance { get; private set; }

	// Game state variables
	public int Xp { get; private set; }
	public int Gold { get; private set; }
	public int Health { get; private set; }
	public int MaxHealth { get; private set; }
	public int CurrentLevel { get; private set; }
	public bool IsGamePaused { get; private set; }

	public Array<Player> PlayersInScene { get; private set; }
	public Array<Enemy> EnemiesInScene { get; private set; }
	public Array<Node3D> DamageablesInScene { get; private set; }

	public Random Random { get; private set; } = new Random();
	public Player Player { get; private set; }
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

		// Initialize default values
		Xp = 0;
		Gold = 0;
		Health = 100;
		MaxHealth = 100;
		CurrentLevel = 1;
		IsGamePaused = false;

		// Connect to signals
		OnSceneTreeChanged();
		GetTree().TreeChanged += OnSceneTreeChanged;

		SignalBus.Instance.PlayerSpawned += player => Player = player;
		SignalBus.Instance.LevelLoaded += level => Level = level;

		GD.Print("GameManager initialized.");
	}

	// Method to increase the experience points
	public void AddXp(int xp)
	{
		Xp += xp;
		SignalBus.EmitXpUpdated(Xp);
		GD.Print($"XP Updated: {Xp}");
	}

	// Method to increase the gold
	public void AddGold(int gold)
	{
		Gold += gold;
		SignalBus.EmitGoldUpdated(Gold);
		GD.Print($"Gold Updated: {Gold}");
	}

	// Method to change the health
	public void UpdateHealth(int health, int maxHealth)
	{
		Health = health;
		SignalBus.EmitHealthChanged(health, maxHealth);
		GD.Print($"Health changed: {health}");
	}

	// Method to update cooldown status
	public void UpdateCooldown(int slotIndex, float remainingTime, float totalTime)
	{
		SignalBus.EmitCooldownUpdated(slotIndex, remainingTime, totalTime);
	}

	// Method to change the level
	public void ChangeLevel(int level)
	{
		CurrentLevel = level;
		SignalBus.EmitLevelChanged(level);

		// Load new level scene
		GD.Print($"Changing to Level {level}");
		//GetTree().ChangeSceneToFile($"res://scenes/levels/level{level}.tscn");
	}

	// Method to pause or unpause the game
	public void TogglePause()
	{
		IsGamePaused = !IsGamePaused;
		SignalBus.EmitGamePaused(IsGamePaused);

		GetTree().Paused = IsGamePaused;
		GD.Print($"Game Paused: {IsGamePaused}");
	}

	// Restart the game
	public void RestartGame()
	{
		Xp = 0;
		CurrentLevel = 1;
		IsGamePaused = false;
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
			EnemiesInScene = new Array<Enemy>(tree.GetNodesInGroup("enemy").Cast<Enemy>().ToArray());
			DamageablesInScene = new Array<Node3D>(tree.GetNodesInGroup("damageable").Cast<Node3D>().ToArray());
		}
	}
}
