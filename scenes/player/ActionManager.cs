using Godot;
using Godot.Collections;
using System.Collections.Generic;

public partial class ActionManager : Node
{
	public const int PrimarySlotIndex = -1;
	public const int SecondarySlotIndex = -2;

	[Export] public ActionSlot PrimaryAttackSlot { get; set; }
	[Export] public ActionSlot SecondaryTechniqueSlot { get; set; }

	public int ActionSlotCount => _quickItemSlots.Count;
	public string CurrentAnimationId => _currentAction is Weapon ? null : _currentAction?.AnimationId;
	public bool IsBusy
	{
		get
		{
			if (_player?.ActionGate?.IsBusy == true) return true;
			if (PrimaryAttackSlot?.IsDelayed == true || PrimaryAttackSlot?.IsPerformingAction == true) return true;
			if (SecondaryTechniqueSlot?.IsDelayed == true || SecondaryTechniqueSlot?.IsPerformingAction == true) return true;
			if (_quickItemSlots != null)
			{
				for (int i = 0; i < _quickItemSlots.Count; i++)
				{
					var slot = _quickItemSlots[i];
					if (slot.IsDelayed || slot.IsPerformingAction) return true;
				}
			}
			return false;
		}
	}

	private Player _player;
	private readonly List<ActionSlot> _quickItemSlots = new();
	private IPlayerAction _currentAction;
	private ActionSlot _currentSlot;

	public override void _Ready()
	{
		_player = GetParent<Player>();

		// Wire or create dedicated primary slot
		PrimaryAttackSlot ??= GetNodeOrNull<ActionSlot>("PrimaryAttackSlot");
		if (PrimaryAttackSlot == null)
		{
			PrimaryAttackSlot = new ActionSlot { Name = "PrimaryAttackSlot", ActionKind = ActorActionKind.PrimaryAttack };
			AddChild(PrimaryAttackSlot);
		}
		PrimaryAttackSlot.ActionKind = ActorActionKind.PrimaryAttack;
		PrimaryAttackSlot.SlotIndex = PrimarySlotIndex;
		PrimaryAttackSlot.PerformAction += () => OnActionPerformed(PrimaryAttackSlot);
		PrimaryAttackSlot.ActionPerformed += () => OnActionCompleted(PrimaryAttackSlot);

		// Wire or create dedicated secondary technique slot
		SecondaryTechniqueSlot ??= GetNodeOrNull<ActionSlot>("SecondaryTechniqueSlot");
		if (SecondaryTechniqueSlot == null)
		{
			SecondaryTechniqueSlot = new ActionSlot { Name = "SecondaryTechniqueSlot", ActionKind = ActorActionKind.SecondaryTechnique };
			AddChild(SecondaryTechniqueSlot);
		}
		SecondaryTechniqueSlot.ActionKind = ActorActionKind.SecondaryTechnique;
		SecondaryTechniqueSlot.SlotIndex = SecondarySlotIndex;
		SecondaryTechniqueSlot.PerformAction += () => OnActionPerformed(SecondaryTechniqueSlot);
		SecondaryTechniqueSlot.ActionPerformed += () => OnActionCompleted(SecondaryTechniqueSlot);

		// Default primary fallback is unarmed
		AssignPrimary(UnarmedAttackAction.Instance, null);

		// Quick item slots (ActionSlot_1 .. ActionSlot_4)
		_quickItemSlots.Clear();
		for (int i = 1; i <= 4; i++)
		{
			var slot = GetNodeOrNull<ActionSlot>($"ActionSlot_{i}");
			if (slot != null)
			{
				slot.ActionKind = ActorActionKind.ItemUse;
				_quickItemSlots.Add(slot);
			}
		}

		if (_quickItemSlots.Count == 0)
		{
			foreach (var child in GetChildren())
			{
				if (child is ActionSlot slot && slot != PrimaryAttackSlot && slot != SecondaryTechniqueSlot)
				{
					slot.ActionKind = ActorActionKind.ItemUse;
					_quickItemSlots.Add(slot);
					if (_quickItemSlots.Count == 4) break;
				}
			}
		}

		while (_quickItemSlots.Count < 4)
		{
			var newSlot = new ActionSlot { Name = $"ActionSlot_{_quickItemSlots.Count + 1}", ActionKind = ActorActionKind.ItemUse };
			AddChild(newSlot);
			_quickItemSlots.Add(newSlot);
		}

		for (int i = 0; i < _quickItemSlots.Count; i++)
		{
			var actionSlot = _quickItemSlots[i];
			actionSlot.PerformAction += () => OnActionPerformed(actionSlot);
			actionSlot.ActionPerformed += () => OnActionCompleted(actionSlot);
		}
	}

	public ActionSlot GetActionSlot(int slotIndex)
	{
		if (slotIndex == PrimarySlotIndex) return PrimaryAttackSlot;
		if (slotIndex == SecondarySlotIndex) return SecondaryTechniqueSlot;
		if (slotIndex < 0 || slotIndex >= _quickItemSlots.Count) return null;
		return _quickItemSlots[slotIndex];
	}

	public IPlayerAction GetAction(int slotIndex) => GetActionSlot(slotIndex)?.AssignedAction;

	/// <summary>
	/// Assigns a quick slot's action, preview and shown quantity, then notifies the HUD once with the complete state.
	/// </summary>
	public void AssignAction(int slotIndex, IPlayerAction action, PackedScene previewScene, int quantity = 1)
	{
		if (slotIndex < 0 || slotIndex >= _quickItemSlots.Count)
		{
			GD.PushError("Invalid slot index");
			return;
		}

		ActionSlot slot = _quickItemSlots[slotIndex];
		slot.AssignAction(slotIndex, action, previewScene);
		slot.SetQuantity(quantity);
		SignalBus.EmitPlayerActionSlotChanged(slotIndex, slot);
	}

	public void AssignPrimary(ICombatAttackAction action, PackedScene previewScene)
	{
		PrimaryAttackSlot?.AssignAction(PrimarySlotIndex, action as IPlayerAction, previewScene);
		SignalBus.EmitPlayerActionSlotChanged(PrimarySlotIndex, PrimaryAttackSlot);
	}

	public void AssignSecondary(ICombatAttackAction action, PackedScene previewScene)
	{
		SecondaryTechniqueSlot?.AssignAction(SecondarySlotIndex, action as IPlayerAction, previewScene);
		SignalBus.EmitPlayerActionSlotChanged(SecondarySlotIndex, SecondaryTechniqueSlot);
	}

	public bool TryPerformPrimary()
	{
		if (_player == null || _player.IsDead || _player.IsHit) return false;
		return PrimaryAttackSlot?.TryPerformAction(_player) == true;
	}

	/// <summary>Cancel the current primary step's recovery into the next step of the weapon's combo.</summary>
	public bool TryChainPrimary()
	{
		if (_player == null || _player.IsDead || _player.IsHit || _player.AttackController?.CanChainPrimary != true) return false;
		return PrimaryAttackSlot?.TryPerformAction(_player, chain: true) == true;
	}

	public bool TryPerformSecondary()
	{
		if (_player == null || _player.IsDead || _player.IsHit || SecondaryTechniqueSlot?.AssignedAction == null) return false;
		return SecondaryTechniqueSlot.TryPerformAction(_player);
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
		if (IsBusy || _player.AttackController.IsAttacking || _player.IsDead || _player.IsHit || slotIndex < 0 || slotIndex >= _quickItemSlots.Count) return;

		_quickItemSlots[slotIndex].TryPerformAction(_player);
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