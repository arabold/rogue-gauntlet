using Godot;
using Godot.Collections;
using System.Linq;

public partial class GameManager : Node
{
	// Singleton instance for global access
	public static GameManager Instance { get; private set; }

	// Game state variables
	public int Score { get; private set; }
	public int Health { get; private set; }
	public int CurrentLevel { get; private set; }
	public bool IsGamePaused { get; private set; }

	public Array<Player> PlayersInScene { get; private set; }
	public Array<Enemy> EnemiesInScene { get; private set; }

	[Signal]
	public delegate void ScoreUpdatedEventHandler(int newScore);

	[Signal]
	public delegate void HealthChangedEventHandler(int newHealth);

	[Signal]
	public delegate void LevelChangedEventHandler(int newLevel);

	[Signal]
	public delegate void GamePausedEventHandler(bool isPaused);

	[Signal]
	public delegate void CooldownUpdatedEventHandler(int actionIndex, float remainingTime, float totalTime);

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
		Score = 0;
		Health = 100;
		CurrentLevel = 1;
		IsGamePaused = false;

		PlayersInScene = new Array<Player>(GetTree().GetNodesInGroup("player").Cast<Player>().ToArray());
		EnemiesInScene = new Array<Enemy>(GetTree().GetNodesInGroup("enemy").Cast<Enemy>().ToArray());
		GetTree().TreeChanged += OnSceneTreeChanged;

		GD.Print("GameManager initialized.");
	}

	// Method to increase the score
	public void AddScore(int points)
	{
		Score += points;
		EmitSignal(SignalName.ScoreUpdated, Score);
		GD.Print($"Score Updated: {Score}");
	}

	// Method to change the health
	public void ChangeHealth(int health)
	{
		Health = health;
		EmitSignal(SignalName.HealthChanged, health);
		GD.Print($"Health changed: {health}");
	}

	// Method to update cooldown status
	public void UpdateCooldown(int slotIndex, float remainingTime, float totalTime)
	{
		EmitSignal(SignalName.CooldownUpdated, slotIndex, remainingTime, totalTime);
	}

	// Method to change the level
	public void ChangeLevel(int level)
	{
		CurrentLevel = level;
		EmitSignal(SignalName.LevelChanged, level);

		// Load new level scene
		GD.Print($"Changing to Level {level}");
		//GetTree().ChangeSceneToFile($"res://scenes/levels/level{level}.tscn");
	}

	// Method to pause or unpause the game
	public void TogglePause()
	{
		IsGamePaused = !IsGamePaused;
		EmitSignal(SignalName.GamePaused, IsGamePaused);

		GetTree().Paused = IsGamePaused;
		GD.Print($"Game Paused: {IsGamePaused}");
	}

	// Restart the game
	public void RestartGame()
	{
		Score = 0;
		CurrentLevel = 1;
		IsGamePaused = false;
		GD.Print("Restarting Game...");

		// Load the main scene
		GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
	}

	private void OnSceneTreeChanged()
	{
		PlayersInScene = new Array<Player>(GetTree().GetNodesInGroup("player").Cast<Player>().ToArray());
		EnemiesInScene = new Array<Enemy>(GetTree().GetNodesInGroup("enemy").Cast<Enemy>().ToArray());
	}
}
