using Godot;

public partial class Hud : Control
{
	private ProgressBar _healthBar;
	private Label _scoreLabel;
	private ProgressBar[] _cooldownBars;

	public override void _Ready()
	{
		// Get references to the child nodes
		_healthBar = GetNode<ProgressBar>("HealthBar");
		_scoreLabel = GetNode<Label>("ScoreLabel");

		// Connect signals from GameManager to update HUD elements
		GameManager.Instance.Connect(
			GameManager.SignalName.ScoreUpdated,
			Callable.From<int>(OnScoreUpdated)
		);
		GameManager.Instance.Connect(
			GameManager.SignalName.HealthChanged,
			Callable.From<int>(OnHealthChanged)
		);
		GameManager.Instance.Connect(
			GameManager.SignalName.GamePaused,
			Callable.From<bool>(OnGamePaused)
		);
		GameManager.Instance.Connect(
			GameManager.SignalName.CooldownUpdated,
			Callable.From<int, float, float>(OnCooldownUpdated)
		);

		// Initialize the HUD with the current game state
		OnScoreUpdated(GameManager.Instance.Score);
		OnHealthChanged(GameManager.Instance.Health);

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

	// Updates the score label when the score changes
	private void OnScoreUpdated(int newScore)
	{
		_scoreLabel.Text = $"Score: {newScore}";
	}

	// Updates the health bar when the health changes
	private void OnHealthChanged(int newHealth)
	{
		_healthBar.Value = newHealth; // Assuming health is between 0 and _healthBar.MaxValue
	}

	// Handles game pause (optional UI feedback)
	private void OnGamePaused(bool isPaused)
	{
		GD.Print($"Game Paused: {isPaused}");
		// You can add additional UI changes here, like showing a "Paused" label
	}

	private void OnCooldownUpdated(int actionIndex, float remainingTime, float totalTime)
	{
		int arrayIndex = actionIndex - 1;
		if (arrayIndex >= 0 && arrayIndex < _cooldownBars.Length)
		{
			_cooldownBars[arrayIndex].Value = 100 * remainingTime / totalTime;
		}
	}
}
