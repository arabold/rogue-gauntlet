using Godot;
using System;
using System.Linq;

public partial class ActionContainer : PanelContainer
{
	[Export] public int Slot { get; set; }
	[Export]
	public string ActionBinding
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { _keyBinding.ActionBinding = value; }
		}
	}
	[Export]
	public PackedScene PreviewScene
	{
		get;
		set
		{
			field = value;
			if (IsNodeReady()) { _preview.Scene = value; }
		}
	}

	private ProgressBar _cooldownProgressBar;
	private KeyBinding _keyBinding;
	private Preview _preview;
	private SubViewportContainer _viewportContainer;
	private Label _quantityLabel;

	public override void _Ready()
	{
		_cooldownProgressBar = GetNode<ProgressBar>("%CooldownProgressBar");

		_keyBinding = GetNode<KeyBinding>("%KeyBinding");
		_keyBinding.ActionBinding = ActionBinding;
		_keyBinding.IsDisabled = true;

		_preview = GetNode<Preview>("%Preview");
		_preview.Scene = PreviewScene;
		_viewportContainer = GetNodeOrNull<SubViewportContainer>("SubViewportContainer");
		_quantityLabel = GetNodeOrNull<Label>("%QuantityLabel");

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerActionSlotChanged += OnActionSlotChanged,
			signalBus => signalBus.PlayerActionSlotChanged -= OnActionSlotChanged);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.CooldownUpdated += OnCooldownUpdated,
			signalBus => signalBus.CooldownUpdated -= OnCooldownUpdated);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerSpawned += OnPlayerSpawned,
			signalBus => signalBus.PlayerSpawned -= OnPlayerSpawned);

		var player = GetTree()?.GetNodesInGroup("player").OfType<Player>().FirstOrDefault();
		if (player != null)
		{
			OnPlayerSpawned(player);
		}

		OnCooldownUpdated(Slot, 0, 0);
	}

	private void OnPlayerSpawned(Player player)
	{
		if (player?.ActionManager != null)
		{
			var actionSlot = player.ActionManager.GetActionSlot(Slot);
			if (actionSlot != null)
			{
				OnActionSlotChanged(Slot, actionSlot);
			}
		}
	}

	private void OnActionSlotChanged(int slotIndex, ActionSlot action)
	{
		if (slotIndex == Slot)
		{
			// The preview scene is already the run appearance model (see PlayerQuickItemController).
			Item item = action?.AssignedAction as Item;
			_preview.Scene = action?.PreviewScene;
			bool isDepleted = action != null && action.AssignedAction != null && action.Quantity == 0;
			if (_viewportContainer != null)
			{
				_viewportContainer.Modulate = isDepleted ? new Color(0.4f, 0.4f, 0.4f, 0.6f) : new Color(1, 1, 1, 1);
			}
			bool disabled = Slot != ActionManager.PrimarySlotIndex && (action?.AssignedAction == null || isDepleted);
			_keyBinding.IsDisabled = disabled;

			if (_quantityLabel != null)
			{
				bool isStackable = item != null && item.CanStack;
				_quantityLabel.Text = (isStackable && action != null) ? action.Quantity.ToString() : "";
			}
		}
	}

	private void OnCooldownUpdated(int slotIndex, float remainingTime, float totalTime)
	{
		if (slotIndex == Slot)
		{
			_cooldownProgressBar.Value = totalTime > 0 ? 100 * remainingTime / totalTime : 0;
		}
	}
}
