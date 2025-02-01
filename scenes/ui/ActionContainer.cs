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

		SignalBus.Instance.PlayerActionSlotChanged += OnActionSlotChanged;
		SignalBus.Instance.CooldownUpdated += OnCooldownUpdated;

		OnCooldownUpdated(Slot, 0, 0);
	}

	private void OnActionSlotChanged(int slotIndex, ActionSlot action)
	{
		if (slotIndex == Slot)
		{
			_preview.SetScene(action?.PreviewScene);
			_keyBinding.IsDisabled = action == null;
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
