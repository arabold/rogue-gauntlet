using Godot;
using Godot.Collections;
using System.Collections.Generic;

public partial class ActionManager : Node
{
	public const int PrimarySlotIndex = -1;
	public const int SecondarySlotIndex = -2;
	public const int OffhandSlotIndex = -3;

	[Export] public ActionSlot PrimaryAttackSlot { get; set; }
	[Export] public ActionSlot SecondaryTechniqueSlot { get; set; }
	[Export] public ActionSlot OffhandActionSlot { get; set; }

	public int ActionSlotCount => _quickItemSlots.Count;
	/// <summary>
	/// The running item use's id for the AnimationTree. Combat actions (weapon attacks, unarmed strikes, techniques
	/// and spell casts) have none: they pose through <see cref="CombatAnimationComponent"/> on their contact clock.
	/// </summary>
	public string CurrentAnimationId => (_currentAction as ConsumableItem)?.AnimationId;
	public bool IsBusy
	{
		get
		{
			if (_player?.ActionGate?.IsBusy == true) return true;
			if (PrimaryAttackSlot?.IsDelayed == true || PrimaryAttackSlot?.IsPerformingAction == true) return true;
			if (SecondaryTechniqueSlot?.IsDelayed == true || SecondaryTechniqueSlot?.IsPerformingAction == true) return true;
			if (OffhandActionSlot?.IsDelayed == true || OffhandActionSlot?.IsPerformingAction == true) return true;
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
	private readonly System.Collections.Generic.Dictionary<string, (double Remaining, double Total)> _wandCooldowns = new();
	private readonly List<string> _activeWandKeys = [];
	private readonly List<string> _expiredWandKeys = [];

	public bool IsWandOnCooldown(string instanceId) =>
		!string.IsNullOrEmpty(instanceId) && _wandCooldowns.TryGetValue(instanceId, out var cd) && cd.Remaining > 0;

	public void TrackWandCooldown(string instanceId, double remaining, double total)
	{
		if (string.IsNullOrEmpty(instanceId) || remaining <= 0) return;
		_wandCooldowns[instanceId] = (remaining, total);
		SetProcess(true);
	}

	public bool TryGetWandCooldown(string instanceId, out (double Remaining, double Total) cd)
	{
		if (!string.IsNullOrEmpty(instanceId) && _wandCooldowns.TryGetValue(instanceId, out cd) && cd.Remaining > 0)
		{
			return true;
		}
		cd = default;
		return false;
	}

	public System.Collections.Generic.Dictionary<string, float> CaptureWandCooldowns()
	{
		var dict = new System.Collections.Generic.Dictionary<string, float>();
		foreach (var (key, (remaining, _)) in _wandCooldowns)
		{
			if (remaining > 0) dict[key] = (float)remaining;
		}
		return dict;
	}

	public void RestoreWandCooldowns(System.Collections.Generic.Dictionary<string, float> cooldowns)
	{
		_wandCooldowns.Clear();
		if (cooldowns == null) return;
		foreach (var (key, remaining) in cooldowns)
		{
			if (!string.IsNullOrEmpty(key) && float.IsFinite(remaining) && remaining > 0)
			{
				_wandCooldowns[key] = (remaining, remaining);
				SetProcess(true);
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_wandCooldowns.Count == 0)
		{
			SetProcess(false);
			return;
		}

		_activeWandKeys.Clear();
		foreach (string key in _wandCooldowns.Keys) _activeWandKeys.Add(key);
		_expiredWandKeys.Clear();
		for (int i = 0; i < _activeWandKeys.Count; i++)
		{
			string key = _activeWandKeys[i];
			var (remaining, total) = _wandCooldowns[key];
			remaining -= delta;
			if (remaining <= 0)
			{
				_expiredWandKeys.Add(key);
			}
			else
			{
				_wandCooldowns[key] = (remaining, total);
			}
		}
		for (int i = 0; i < _expiredWandKeys.Count; i++)
		{
			_wandCooldowns.Remove(_expiredWandKeys[i]);
		}
		if (_wandCooldowns.Count == 0)
		{
			SetProcess(false);
		}
	}

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

		// Wire dedicated offhand action slot
		OffhandActionSlot ??= GetNodeOrNull<ActionSlot>("OffhandActionSlot");
		if (OffhandActionSlot == null)
		{
			OffhandActionSlot = new ActionSlot { Name = "OffhandActionSlot", ActionKind = ActorActionKind.SecondaryTechnique };
			AddChild(OffhandActionSlot);
		}
		OffhandActionSlot.ActionKind = ActorActionKind.SecondaryTechnique;
		OffhandActionSlot.SlotIndex = OffhandSlotIndex;
		OffhandActionSlot.PerformAction += () => OnActionPerformed(OffhandActionSlot);
		OffhandActionSlot.ActionPerformed += () => OnActionCompleted(OffhandActionSlot);

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
				if (child is ActionSlot slot && slot != PrimaryAttackSlot && slot != SecondaryTechniqueSlot && slot != OffhandActionSlot)
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
		if (slotIndex == OffhandSlotIndex) return OffhandActionSlot;
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

	private void AssignCombatSlot(ActionSlot slot, int slotIndex, ICombatAttackAction action, PackedScene previewScene)
	{
		slot?.AssignAction(slotIndex, action as IPlayerAction, previewScene);
		if (action is SpellCastAction spellCast && TryGetWandCooldown(spellCast.Focus?.InstanceId, out var cd))
		{
			slot?.ResumeCooldown(cd.Remaining, cd.Total);
		}
		SignalBus.EmitPlayerActionSlotChanged(slotIndex, slot);
	}

	public void AssignSecondary(ICombatAttackAction action, PackedScene previewScene) =>
		AssignCombatSlot(SecondaryTechniqueSlot, SecondarySlotIndex, action, previewScene);

	public void AssignOffhand(ICombatAttackAction action, PackedScene previewScene) =>
		AssignCombatSlot(OffhandActionSlot, OffhandSlotIndex, action, previewScene);

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

	private bool TryPerformCombatSlot(ActionSlot slot)
	{
		if (_player == null || _player.IsDead || _player.IsHit || slot?.AssignedAction == null) return false;
		return slot.TryPerformAction(_player);
	}

	public bool TryPerformSecondary() => TryPerformCombatSlot(SecondaryTechniqueSlot);

	public bool TryPerformOffhand() => TryPerformCombatSlot(OffhandActionSlot);

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