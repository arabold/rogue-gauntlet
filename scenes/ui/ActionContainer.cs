using Godot;
using System;

public partial class ActionContainer : PanelContainer
{
	[Export] public int Slot;
	[Export] public string ActionBinding;
	// [Export] public ActionBase Action;
	[Export] public PackedScene PreviewScene;

	private ProgressBar _cooldownProgressBar;

	public override void _Ready()
	{
		_cooldownProgressBar = GetNode<ProgressBar>("%CooldownProgressBar");

		var keyBinding = GetNode<KeyBinding>("%KeyBinding");
		keyBinding.ActionBinding = ActionBinding;

		var preview = GetNode<Preview>("%Preview");
		preview.SetScene(PreviewScene);

		SignalBus.Instance.CooldownUpdated += OnCooldownUpdated;

		OnCooldownUpdated(Slot, 0, 0);
	}

	public void SetAction(ActionBase action)
	{ }

	private void OnCooldownUpdated(int slotIndex, float remainingTime, float totalTime)
	{
		if (slotIndex == Slot)
		{
			_cooldownProgressBar.Value = totalTime > 0 ? 100 * remainingTime / totalTime : 0;
		}
	}
}
