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
		SignalBus.Instance.PlayerStatsChanged += OnPlayerStatsChanged;

		// Initialize the HUD with the current game state
		// FIXME: Seems backwards. Send initial event from player instead?
		var playerStats = GameManager.Instance.PlayerStats;
		OnPlayerStatsChanged(playerStats);
	}

	private void OnPlayerStatsChanged(PlayerStats stats)
	{
		_xpLabel.Text = $"XP: {stats.Xp}";
		_goldLabel.Text = $"Gold: {stats.Gold}";
		_healthBar.MaxValue = stats.MaxHealth;
		_healthBar.Value = stats.Health;
	}

	// Handles game pause (optional UI feedback)
	private void OnGamePaused(bool isPaused)
	{
		GD.Print($"Game Paused: {isPaused}");
		// You can add additional UI changes here, like showing a "Paused" label
	}
}
