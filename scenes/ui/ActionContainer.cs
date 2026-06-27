using Godot;
using System;

public partial class ActionContainer : PanelContainer
{
	[Export] public int Slot;
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
			if (IsNodeReady()) { _preview.SetScene(value); }
		}
	}

	private ProgressBar _cooldownProgressBar;
	private KeyBinding _keyBinding;
	private Preview _preview;

	public override void _Ready()
	{
		_cooldownProgressBar = GetNode<ProgressBar>("%CooldownProgressBar");

		_keyBinding = GetNode<KeyBinding>("%KeyBinding");
		_keyBinding.ActionBinding = ActionBinding;
		_keyBinding.IsDisabled = true;

		_preview = GetNode<Preview>("%Preview");
		_preview.SetScene(PreviewScene);

		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.PlayerActionSlotChanged += OnActionSlotChanged,
			signalBus => signalBus.PlayerActionSlotChanged -= OnActionSlotChanged);
		this.SubscribeUntilExit(
			SignalBus.Instance,
			signalBus => signalBus.CooldownUpdated += OnCooldownUpdated,
			signalBus => signalBus.CooldownUpdated -= OnCooldownUpdated);

		OnCooldownUpdated(Slot, 0, 0);
	}

	private void OnActionSlotChanged(int slotIndex, ActionSlot action)
	{
		if (slotIndex == Slot)
		{
			// Tint disguised consumables so the action bar matches the inventory preview.
			Color? tint = action?.AssignedAction is Item item ? ItemIdentity.ResolveTint(item) : null;
			_preview.SetScene(action?.PreviewScene, tint);
			_keyBinding.IsDisabled = action?.AssignedAction == null;
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
