using Godot;

public partial class Hud : Control
{
	private ProgressBar _healthBar;
	private Label _xpLabel;
	private Label _goldLabel;

	public override void _Ready()
	{
		// Get references to the child nodes
		_healthBar = GetNode<ProgressBar>("%HealthBar");
		_xpLabel = GetNode<Label>("%XpLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");

		// Connect signals from GameManager to update HUD elements
		SignalBus.Instance.XpUpdated += OnXpUpdated;
		SignalBus.Instance.GoldUpdated += OnGoldUpdated;
		SignalBus.Instance.HealthChanged += OnHealthChanged;
		SignalBus.Instance.GamePaused += OnGamePaused;

		// Initialize the HUD with the current game state
		var playerStats = GameManager.Instance.PlayerStats;
		OnXpUpdated(playerStats.Xp);
		OnGoldUpdated(playerStats.Gold);
		OnHealthChanged(playerStats.Health, playerStats.MaxHealth);
	}

	private void OnXpUpdated(int xp)
	{
		_xpLabel.Text = $"XP: {xp}";
	}

	private void OnGoldUpdated(int gold)
	{
		_goldLabel.Text = $"Gold: {gold}";
	}

	// Updates the health bar when the health changes
	private void OnHealthChanged(int health, int maxHealth)
	{
		_healthBar.MaxValue = maxHealth;
		_healthBar.Value = health;
	}

	// Handles game pause (optional UI feedback)
	private void OnGamePaused(bool isPaused)
	{
		GD.Print($"Game Paused: {isPaused}");
		// You can add additional UI changes here, like showing a "Paused" label
	}
}
