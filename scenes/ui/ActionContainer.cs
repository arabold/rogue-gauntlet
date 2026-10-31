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

	/// <summary>Color of a spell slot's mana cost, shown where item slots show their stack count.</summary>
	[Export] public Color ManaCostColor { get; set; } = new("8fb8ff");

	private ProgressBar _cooldownProgressBar;
	private KeyBinding _keyBinding;
	private Preview _preview;
	private SubViewportContainer _viewportContainer;
	private Label _quantityLabel;
	private ActionSlot _actionSlot;
	private ManaComponent _mana;
	private Action _unsubscribeMana = () => { };

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
		// A spell slot dims while the pool cannot pay for it, so it follows the player's mana.
		_unsubscribeMana();
		_mana = player?.ManaComponent;
		_unsubscribeMana = this.SubscribeUntilExit(_mana, m => m.ManaChanged += OnManaChanged, m => m.ManaChanged -= OnManaChanged);

		var actionSlot = player?.ActionManager?.GetActionSlot(Slot);
		if (actionSlot != null)
		{
			OnActionSlotChanged(Slot, actionSlot);
		}
	}

	private void OnActionSlotChanged(int slotIndex, ActionSlot action)
	{
		if (slotIndex != Slot) return;
		_actionSlot = action;
		// Attack actions (a weapon, or its martial technique) preview the weapon, which lies diagonally like in the inventory.
		_preview.Diagonal = action?.AssignedAction is ICombatAttackAction;
		// The preview scene is already the run appearance model (see PlayerQuickItemController).
		_preview.Scene = action?.PreviewScene;
		Refresh();
	}

	private void OnManaChanged(float currentMana, float maxMana) => Refresh();

	/// <summary>Greys out a used-up stack or an unaffordable spell and labels the stack count or the spell's cost.</summary>
	private void Refresh()
	{
		IPlayerAction assigned = _actionSlot?.AssignedAction;
		float manaCost = (assigned as SpellCastAction)?.ManaCost ?? 0;
		bool isDepleted = assigned != null && (_actionSlot.Quantity == 0 || !(_mana?.CanSpend(manaCost) ?? manaCost == 0));
		if (_viewportContainer != null)
		{
			_viewportContainer.Modulate = isDepleted ? new Color(0.4f, 0.4f, 0.4f, 0.6f) : new Color(1, 1, 1, 1);
		}
		_keyBinding.IsDisabled = Slot != ActionManager.PrimarySlotIndex && (assigned == null || isDepleted);

		if (_quantityLabel != null)
		{
			bool isStackable = assigned is Item { CanStack: true };
			_quantityLabel.Text = manaCost > 0 ? manaCost.ToString("0") : isStackable ? _actionSlot.Quantity.ToString() : "";
			_quantityLabel.SelfModulate = manaCost > 0 ? ManaCostColor : Colors.White;
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
