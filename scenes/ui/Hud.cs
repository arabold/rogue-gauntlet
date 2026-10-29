using Godot;
using System.Linq;

public partial class Hud : Control
{
	private ProgressBar _healthBar;
	private Label _xpLabel;
	private Label _goldLabel;
	private Control _interactionContainer;
	private ProgressBar _interactionProgressBar;
	private System.Action _unsubscribeInteraction = () => { };

	public override void _Ready()
	{
		// Get references to the child nodes
		_healthBar = GetNode<ProgressBar>("%HealthBar");
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
	}
}