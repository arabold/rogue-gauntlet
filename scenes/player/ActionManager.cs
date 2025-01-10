using Godot;
using Godot.Collections;
using System;

public partial class ActionManager : Node
{
	public int ActionSlotCount => _actionSlots.Count;
	public string CurrentActionId => _currentAction?.Id;

	private Player _player;
	private Array<ActionSlot> _actionSlots;
	private Array<float> _cooldownRemainingTime;
	private IAction _currentAction;

	public override void _Ready()
	{
		_player = GetParent<Player>();
		_actionSlots = new Array<ActionSlot>
		{
			GetNode<ActionSlot>("ActionSlot_1"),
			GetNode<ActionSlot>("ActionSlot_2"),
			GetNode<ActionSlot>("ActionSlot_3"),
			GetNode<ActionSlot>("ActionSlot_4")
		};

		foreach (var actionSlot in _actionSlots)
		{
			actionSlot.ActionPerformed += () => OnActionPerformed(actionSlot);
			actionSlot.CooldownStarted += () => OnCooldownStarted(actionSlot);
			actionSlot.CooldownEnded += () => OnCooldownEnded(actionSlot);
		}

		_cooldownRemainingTime = new Array<float>(new float[_actionSlots.Count]);
	}

	public override void _Process(double delta)
	{
		// Update cooldowns
		for (int i = 0; i < _actionSlots.Count; i++)
		{
			_cooldownRemainingTime[i] = Math.Max(0, _cooldownRemainingTime[i] - (float)delta);
			UpdateCooldownUI(i);
		}
	}

	public void AssignAction(int slotIndex, IAction action)
	{
		if (slotIndex < 0 || slotIndex >= _actionSlots.Count)
		{
			GD.PushError("Invalid slot index");
			return;
		}

		ActionSlot slot = _actionSlots[slotIndex];
		slot.AssignAction(action);

		// Reset cooldown
		_cooldownRemainingTime[slotIndex] = 0;
	}

	public bool TryPerformAction(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= _actionSlots.Count)
		{
			GD.PushError("Invalid slot index");
			return false;
		}

		if (!CanPerformAction(slotIndex))
		{
			return false;
		}

		var action = _actionSlots[slotIndex].AssignedAction;
		if (action != null)
		{
			_actionSlots[slotIndex].TriggerAction(_player);
			_currentAction = action;

			var totalDuration = action.PerformDuration + action.CooldownDuration;
			_cooldownRemainingTime[slotIndex] = totalDuration;
		}
		return true;
	}

	public bool CanPerformAction(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= _actionSlots.Count)
		{
			GD.PushError("Invalid slot index");
			return false;
		}

		if (_currentAction != null)
		{
			return false;
		}

		return !_actionSlots[slotIndex].IsOnCooldown;
	}

	private void UpdateCooldownUI(int slotIndex)
	{
		ActionSlot slot = _actionSlots[slotIndex];
		IAction action = slot.AssignedAction;
		if (action == null)
		{
			GameManager.Instance.UpdateCooldown(slotIndex, 0, 0);
			return;
		}

		var totalDuration = action.PerformDuration + action.CooldownDuration;
		var remainingTime = Math.Clamp(_cooldownRemainingTime[slotIndex], 0, totalDuration);

		// Update UI
		GameManager.Instance.UpdateCooldown(slotIndex, remainingTime, totalDuration);
	}

	private void OnActionPerformed(ActionSlot actionSlot)
	{
		_currentAction = null;
	}

	private void OnCooldownStarted(ActionSlot actionSlot)
	{
	}

	private void OnCooldownEnded(ActionSlot actionSlot)
	{
		int slot = _actionSlots.IndexOf(actionSlot);
		_cooldownRemainingTime[slot] = 0;
	}
}
