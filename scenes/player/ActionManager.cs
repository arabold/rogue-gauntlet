using Godot;
using Godot.Collections;
using System;

public partial class ActionManager : Node
{
	public int ActionSlotCount => _actionSlots.Count;
	public string CurrentAnimationId => _currentAction is Weapon ? null : _currentAction?.AnimationId;
	public bool IsBusy => _actionSlots != null && System.Linq.Enumerable.Any(_actionSlots, slot => slot.IsDelayed || slot.IsPerformingAction);

	private Player _player;
	private Array<ActionSlot> _actionSlots;
	private Array<float> _cooldownRemainingTime;
	private IPlayerAction _currentAction;
	private ActionSlot _currentSlot;

	public override void _Ready()
	{
		_player = GetParent<Player>();
		_actionSlots = new Array<ActionSlot>();
		foreach (var child in GetChildren())
		{
			if (child is ActionSlot actionSlot)
			{
				_actionSlots.Add(actionSlot);
			}
		}

		foreach (var actionSlot in _actionSlots)
		{
			actionSlot.PerformAction += () => OnActionPerformed(actionSlot);
			actionSlot.ActionPerformed += () => OnActionCompleted(actionSlot);
		}

		_cooldownRemainingTime = new Array<float>(new float[_actionSlots.Count]);
	}

	public IPlayerAction GetAction(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= _actionSlots.Count)
		{
			GD.PushError("Invalid slot index");
			return null;
		}

		return _actionSlots[slotIndex].AssignedAction;
	}

	public void AssignAction(int slotIndex, IPlayerAction action, PackedScene previewScene)
	{
		if (slotIndex < 0 || slotIndex >= _actionSlots.Count)
		{
			GD.PushError("Invalid slot index");
			return;
		}

		ActionSlot slot = _actionSlots[slotIndex];
		slot.AssignAction(slotIndex, action, previewScene);
		SignalBus.EmitPlayerActionSlotChanged(slotIndex, slot);

		// Reset cooldown
		_cooldownRemainingTime[slotIndex] = 0;
	}

	public bool AssignFirstAvailableAction(IPlayerAction action, PackedScene previewScene)
	{
		for (int i = 0; i < ActionSlotCount; i++)
		{
			if (GetAction(i) == null)
			{
				AssignAction(i, action, previewScene);
				return true;
			}
		}

		return false;
	}

	public bool ClearAction(IPlayerAction action)
	{
		for (int i = 0; i < ActionSlotCount; i++)
		{
			if (GetAction(i) == action)
			{
				AssignAction(i, null, null);
				return true;
			}
		}

		return false;
	}

	public void ClearActions()
	{
		for (int i = 0; i < ActionSlotCount; i++)
		{
			AssignAction(i, null, null);
		}
	}

	public void TryPerformAction(int slotIndex)
	{
		if (IsBusy || _player.AttackController.IsAttacking || _player.IsDead || _player.IsHit || slotIndex < 0 || slotIndex >= _actionSlots.Count) return;

		_actionSlots[slotIndex].TryPerformAction(_player);
	}

	private void OnActionPerformed(ActionSlot actionSlot)
	{
		_currentSlot = actionSlot;
		_currentAction = actionSlot.AssignedAction;
	}

	private void OnActionCompleted(ActionSlot slot)
	{
		if (_currentSlot != slot) return;
		_currentSlot = null;
		_currentAction = null;
	}
}