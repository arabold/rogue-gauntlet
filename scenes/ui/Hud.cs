using Godot;
using System.Linq;

/// <summary>
/// The in-run readouts, one job per corner: the hero's level, health, mana and XP top left; the run's depth and gold top
/// right; the message feed bottom center; the hands and quick slots bottom right. The bottom-left corner stays empty
/// for a touch screen's steering stick. Everything is bare text and bars on the game, with no panels.
/// </summary>
public partial class Hud : Control
{
	[Export] public ProgressBar HealthBar { get; set; }
	[Export] public Label HealthLabel { get; set; }
	[Export] public ProgressBar ManaBar { get; set; }
	[Export] public Label ManaLabel { get; set; }
	/// <summary>Progress through the hero's current level.</summary>
	[Export] public ProgressBar XpBar { get; set; }
	[Export] public Label LevelLabel { get; set; }
	[Export] public Label GoldLabel { get; set; }
	/// <summary>The floor's depth; hidden where no floor is loaded, as in the combat arena.</summary>
	[Export] public Label DepthLabel { get; set; }
	[Export] public Control InteractionContainer { get; set; }
	[Export] public ProgressBar InteractionProgressBar { get; set; }
	/// <summary>The mana bar's tint when a spell is refused for lack of mana; it fades back over <see cref="ManaShortfallFlashSeconds"/>.</summary>
	[Export] public Color ManaShortfallColor { get; set; } = new(1f, 0.35f, 0.35f);
	[Export(PropertyHint.Range, "0.05,2,0.05")] public float ManaShortfallFlashSeconds { get; set; } = 0.45f;

	private System.Action _unsubscribeInteraction = () => { };
	private System.Action _unsubscribeMana = () => { };
	private Tween _manaFlash;

	public override void _Ready()
	{
		InteractionContainer.Visible = false;
		DepthLabel.Visible = false;

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus =>
			{
				signalBus.PlayerStatsChanged += OnPlayerStatsChanged;
				signalBus.PlayerSpawned += OnPlayerSpawned;
				signalBus.LevelLoaded += OnLevelLoaded;
				signalBus.GamePaused += OnGamePaused;
			},
			signalBus =>
			{
				signalBus.PlayerStatsChanged -= OnPlayerStatsChanged;
				signalBus.PlayerSpawned -= OnPlayerSpawned;
				signalBus.LevelLoaded -= OnLevelLoaded;
				signalBus.GamePaused -= OnGamePaused;
			});

		var player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player != null)
		{
			OnPlayerSpawned(player);
		}
	}

	private void OnLevelLoaded(Level level)
	{
		DepthLabel.Text = $"Depth {level.Depth}";
		DepthLabel.Visible = true;
	}

	private void OnPlayerSpawned(Player player)
	{
		OnPlayerStatsChanged(player.Stats);
		_unsubscribeInteraction();
		_unsubscribeInteraction = this.SubscribeUntilExit(
			player?.InteractionComponent,
			c =>
			{
				c.InteractionStarted += OnInteractionStarted;
				c.InteractionProgress += OnInteractionProgress;
				c.InteractionCompleted += OnInteractionEnded;
				c.InteractionCancelled += OnInteractionCancelled;
			},
			c =>
			{
				c.InteractionStarted -= OnInteractionStarted;
				c.InteractionProgress -= OnInteractionProgress;
				c.InteractionCompleted -= OnInteractionEnded;
				c.InteractionCancelled -= OnInteractionCancelled;
			});
		_unsubscribeMana();
		_unsubscribeMana = this.SubscribeUntilExit(
			player?.ManaComponent,
			m => m.ManaShortfall += OnManaShortfall,
			m => m.ManaShortfall -= OnManaShortfall);
	}

	/// <summary>The run menu covers the paused game and shows these readouts itself, so the HUD steps aside for it.</summary>
	private void OnGamePaused(bool isPaused) => Visible = !isPaused;

	/// <summary>
	/// Flashes the mana bar when a cast is refused. A buffered press retries for a few frames, so a flash already
	/// playing is not restarted: one press reads as one flash.
	/// </summary>
	private void OnManaShortfall(float cost)
	{
		if (_manaFlash?.IsRunning() == true) return;
		ManaBar.Modulate = ManaShortfallColor;
		_manaFlash = CreateTween();
		_manaFlash.TweenProperty(ManaBar, "modulate", Colors.White, ManaShortfallFlashSeconds);
	}

	private void OnInteractionStarted(string verb, float duration)
	{
		InteractionProgressBar.Value = 0;
		InteractionContainer.Visible = true;
	}

	private void OnInteractionProgress(string verb, float elapsed, float duration) =>
		InteractionProgressBar.Value = duration > 0 ? elapsed / duration * 100 : 100;

	private void OnInteractionEnded(string verb, int result) => InteractionContainer.Visible = false;
	private void OnInteractionCancelled(int reason) => InteractionContainer.Visible = false;

	private void OnPlayerStatsChanged(PlayerStats stats)
	{
		var (gained, needed) = stats.LevelProgress;
		LevelLabel.Text = stats.XpLevel.ToString();
		XpBar.MaxValue = Mathf.Max(1, needed);
		XpBar.Value = needed > 0 ? gained : 0;
		GoldLabel.Text = stats.Gold.ToString();
		HealthBar.MaxValue = stats.MaxHealth;
		HealthBar.Value = stats.Health;
		HealthLabel.Text = Mathf.CeilToInt(stats.Health).ToString();
		ManaBar.MaxValue = stats.MaxMana;
		ManaBar.Value = stats.Mana;
		ManaLabel.Text = Mathf.FloorToInt(stats.Mana).ToString();
	}
}
