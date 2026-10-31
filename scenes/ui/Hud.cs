using Godot;
using System.Linq;

public partial class Hud : Control
{
	/// <summary>The mana bar's tint when a spell is refused for lack of mana; it fades back over <see cref="ManaShortfallFlashSeconds"/>.</summary>
	[Export] public Color ManaShortfallColor { get; set; } = new(1f, 0.35f, 0.35f);
	[Export(PropertyHint.Range, "0.05,2,0.05")] public float ManaShortfallFlashSeconds { get; set; } = 0.45f;

	private ProgressBar _healthBar;
	private ProgressBar _manaBar;
	private Label _xpLabel;
	private Label _goldLabel;
	private Control _interactionContainer;
	private ProgressBar _interactionProgressBar;
	private System.Action _unsubscribeInteraction = () => { };
	private System.Action _unsubscribeMana = () => { };
	private Tween _manaFlash;

	public override void _Ready()
	{
		// Get references to the child nodes
		_healthBar = GetNode<ProgressBar>("%HealthBar");
		_manaBar = GetNode<ProgressBar>("%ManaBar");
		_xpLabel = GetNode<Label>("%XpLabel");
		_goldLabel = GetNode<Label>("%GoldLabel");
		_interactionContainer = GetNodeOrNull<Control>("%InteractionContainer");
		_interactionProgressBar = GetNodeOrNull<ProgressBar>("%InteractionProgressBar");
		if (_interactionContainer != null) _interactionContainer.Visible = false;

		// Connect player signals to update HUD elements.
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerStatsChanged += OnPlayerStatsChanged,
			signalBus => signalBus.PlayerStatsChanged -= OnPlayerStatsChanged);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += OnPlayerSpawned,
			signalBus => signalBus.PlayerSpawned -= OnPlayerSpawned);

		var player = GetTree().GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player != null)
		{
			OnPlayerSpawned(player);
		}
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

	/// <summary>
	/// Flashes the mana bar when a cast is refused. A buffered press retries for a few frames, so a flash already
	/// playing is not restarted: one press reads as one flash.
	/// </summary>
	private void OnManaShortfall(float cost)
	{
		if (_manaFlash?.IsRunning() == true) return;
		_manaBar.Modulate = ManaShortfallColor;
		_manaFlash = CreateTween();
		_manaFlash.TweenProperty(_manaBar, "modulate", Colors.White, ManaShortfallFlashSeconds);
	}

	private void OnInteractionStarted(string verb, float duration)
	{
		if (_interactionContainer != null)
		{
			_interactionContainer.Visible = true;
			if (_interactionProgressBar != null) _interactionProgressBar.Value = 0;
		}
	}

	private void OnInteractionProgress(string verb, float elapsed, float duration)
	{
		if (_interactionProgressBar != null)
		{
			_interactionProgressBar.Value = duration > 0 ? (elapsed / duration) * 100 : 100;
		}
	}

	private void OnInteractionEnded(string verb, int result) => HideInteraction();
	private void OnInteractionCancelled(int reason) => HideInteraction();

	private void HideInteraction()
	{
		if (_interactionContainer != null)
		{
			_interactionContainer.Visible = false;
		}
	}

	private void OnPlayerStatsChanged(PlayerStats stats)
	{
		_xpLabel.Text = $"XP: {stats.Xp}";
		_goldLabel.Text = $"Gold: {stats.Gold}";
		_healthBar.MaxValue = stats.MaxHealth;
		_healthBar.Value = stats.Health;
		_manaBar.MaxValue = stats.MaxMana;
		_manaBar.Value = stats.Mana;
	}
}