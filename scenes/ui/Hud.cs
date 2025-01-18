using Godot;

public partial class Hud : Control
{
	private ProgressBar _healthBar;
	private Label _xpLabel;
	private Label _goldLabel;
	private ProgressBar[] _cooldownBars;

	public override void _Ready()
	{
		// Get references to the child nodes
		_healthBar = GetNode<ProgressBar>("HealthBar");
		_xpLabel = GetNode<Label>("XpLabel");
		_goldLabel = GetNode<Label>("GoldLabel");

		// Connect signals from GameManager to update HUD elements
		SignalBus.Instance.XpUpdated += OnXpUpdated;
		SignalBus.Instance.GoldUpdated += OnGoldUpdated;
		SignalBus.Instance.HealthChanged += OnHealthChanged;
		SignalBus.Instance.GamePaused += OnGamePaused;
		SignalBus.Instance.CooldownUpdated += OnCooldownUpdated;

		// Initialize the HUD with the current game state
		var playerStats = GameManager.Instance.PlayerStats;
		OnXpUpdated(playerStats.Xp);
		OnGoldUpdated(playerStats.Gold);
		OnHealthChanged(playerStats.Health, playerStats.MaxHealth);

		// Initialize cooldown bars array
		_cooldownBars = new ProgressBar[]
		{
			GetNode<ProgressBar>("ActionCooldown1"),
			GetNode<ProgressBar>("ActionCooldown2"),
			GetNode<ProgressBar>("ActionCooldown3"),
			GetNode<ProgressBar>("ActionCooldown4")
		};

		// Set initial values
		foreach (var bar in _cooldownBars)
		{
			bar.MinValue = 0;
			bar.MaxValue = 100;  // Changed from 1 to 100
			bar.Value = 0;
		}
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

	private void OnCooldownUpdated(int slotIndex, float remainingTime, float totalTime)
	{
		if (slotIndex >= 0 && slotIndex < _cooldownBars.Length)
		{
			_cooldownBars[slotIndex].Value = 100 * remainingTime / totalTime;
		}
	}
}
