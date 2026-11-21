using Godot;
using System.Collections.Generic;
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
	private ActorInteractionComponent _interaction;
	private bool _isPaused;
	/// <summary>The readouts this HUD hid for the run menu: only these come back, so one hidden for its own reasons stays hidden.</summary>
	private readonly List<Control> _hiddenForPause = [];

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
		_interaction = player?.InteractionComponent;
		_unsubscribeInteraction = this.SubscribeUntilExit(
			_interaction,
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
		RefreshInteraction();
	}

	/// <summary>
	/// The run menu covers the paused game and shows its readouts itself, while keeping the message feed visible for
	/// actions taken in the menu. Resuming brings back only the readouts pausing hid, such as Vitals, and never a
	/// release build's debug menu.
	/// </summary>
	private void OnGamePaused(bool isPaused)
	{
		_isPaused = isPaused;
		if (isPaused)
		{
			foreach (Node child in GetChildren())
			{
				if (child is Control { Visible: true } control && control is not HudMessageFeed && control != InteractionContainer)
				{
					control.Visible = false;
					_hiddenForPause.Add(control);
				}
			}
		}
		else
		{
			foreach (Control control in _hiddenForPause)
			{
				if (IsInstanceValid(control)) control.Visible = true;
			}
			_hiddenForPause.Clear();
		}
		RefreshInteraction();
	}

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
		RefreshInteraction();
	}

	private void OnInteractionProgress(string verb, float elapsed, float duration) =>
		InteractionProgressBar.Value = duration > 0 ? elapsed / duration * 100 : 100;

	private void OnInteractionEnded(string verb, int result) => RefreshInteraction();
	private void OnInteractionCancelled(int reason) => RefreshInteraction();

	/// <summary>
	/// Shows the bar while the hero channels an interaction in the running game. It reads the live session rather than
	/// remembering itself across a pause, because a session can end while paused: a keeper's trade pauses the game from
	/// inside the interaction's commit, before the session reports its end.
	/// </summary>
	private void RefreshInteraction() =>
		InteractionContainer.Visible = !_isPaused && IsInstanceValid(_interaction) && _interaction.IsActive;

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
