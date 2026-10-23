using Godot;

/// <summary>Accepts actions atomically and tracks preparation, execution and cooldown.</summary>
public partial class ActionSlot : Node
{
	[Signal] public delegate void PerformActionEventHandler();
	[Signal] public delegate void ActionPerformedEventHandler();
	[Signal] public delegate void CooldownStartedEventHandler();
	[Signal] public delegate void CooldownEndedEventHandler();

	[Export] public ActorActionKind ActionKind { get; set; } = ActorActionKind.ItemUse;
	[Export] public string CooldownKey { get; set; } = "";

	public IPlayerAction AssignedAction { get; private set; }
	public PackedScene PreviewScene { get; private set; }
	public bool IsDelayed { get; private set; }
	public bool IsPerformingAction { get; private set; }
	public bool IsOnCooldown { get; private set; }
	public double TotalDuration { get; private set; }
	public double RemainingTime { get; private set; }
	public int SlotIndex { get; set; }
	public int Quantity { get; private set; } = 1;

	private ulong _generation;
	private Player _actor;
	private ActorActionLease _activeLease = ActorActionLease.None;

	public void SetQuantity(int quantity)
	{
		Quantity = quantity;
	}

	/// <summary>Invalidate old asynchronous work when equipment or slot ownership changes.</summary>
	public void AssignAction(int slotIndex, IPlayerAction action, PackedScene previewScene)
	{
		if (IsDelayed || IsPerformingAction) CancelExecution(ActionInterruptReason.EquipChanged);
		_generation++;
		ReleaseGateLease();
		IsDelayed = IsPerformingAction = IsOnCooldown = false;
		RemainingTime = 0;
		SlotIndex = slotIndex;
		AssignedAction = action;
		PreviewScene = previewScene;
	}

	public void CancelExecution(ActionInterruptReason reason)
	{
		bool wasPerforming = IsPerformingAction;
		_generation++;
		if (_actor != null && _actor.AttackController.IsAttacking)
		{
			_actor.AttackController.CancelAttack();
		}
		ReleaseGateLease();
		Reset();
		if (wasPerforming) EmitSignalActionPerformed();
	}

	/// <summary>Resolve weapon animation/timing before accepting an action or publishing its cooldown.</summary>
	public async void TryPerformAction(Player player)
	{
		if (AssignedAction == null || IsDelayed || IsPerformingAction || IsOnCooldown || player.IsDead || player.IsHit) return;
		var action = AssignedAction;
		var weapon = action as Weapon;
		var attackAction = action as ICombatAttackAction;
		PreparedAttack attack = null;

		if (attackAction != null)
		{
			if (weapon != null && !IsEquipped(player, weapon)) return;
			if (action is MartialTechniqueAction martial && !IsEquipped(player, martial.Weapon)) return;
			if (!attackAction.TryPrepare(player, out attack)) return;
		}

		_actor = player;
		ulong generation = ++_generation;
		double duration = attack?.Duration ?? action.PerformDuration;
		double delay = action.Delay;
		double cooldown = action.CooldownDuration;
		if (!double.IsFinite(delay) || !double.IsFinite(duration) || !double.IsFinite(cooldown)
			|| delay < 0 || duration < 0 || cooldown < 0) return;

		// Acquire shared action gate lease
		ActorActionKind kind = ActionKind;
		ActorActionPhase initialPhase = (delay > 0 || attackAction != null) ? ActorActionPhase.WindUp : ActorActionPhase.Active;

		if (player.ActionGate != null)
		{
			var gateResult = player.ActionGate.TryAcquire(kind, initialPhase, reason => CancelExecution(reason), out _activeLease);
			if (gateResult != ActorActionRequestResult.Success)
			{
				return;
			}
		}

		TotalDuration = delay + duration + cooldown;
		RemainingTime = TotalDuration;
		SignalBus.EmitCooldownUpdated(SlotIndex, (float)RemainingTime, (float)TotalDuration);

		if (delay > 0)
		{
			IsDelayed = true;
			await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
			if (!IsCurrent(generation)) return;
		}
		IsDelayed = false;
		if (player.IsDead || player.IsHit || (weapon != null && !IsEquipped(player, weapon)))
		{
			ReleaseGateLease();
			Reset();
			return;
		}

		if (attackAction == null && player.ActionGate != null && _activeLease.IsValid)
		{
			player.ActionGate.ReportPhase(_activeLease, ActorActionPhase.Active);
		}

		if (attackAction != null)
		{
			if (!attackAction.StartPrepared(player, attack, _activeLease))
			{
				ReleaseGateLease();
				Reset();
				return;
			}
		}

		IsPerformingAction = true;
		EmitSignalPerformAction();
		if (attackAction == null)
		{
			action.PerformAction(player);
			await ToSignal(GetTree().CreateTimer(duration, false), SceneTreeTimer.SignalName.Timeout);
		}
		else
		{
			// The executor owns recovery and cancellation; no independent weapon perform timer.
			while (IsCurrent(generation) && player.AttackController.IsAttacking)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
		}

		if (!IsCurrent(generation)) return;
		ReleaseGateLease();
		IsPerformingAction = false;
		EmitSignalActionPerformed();

		if (cooldown > 0 && !player.IsDead)
		{
			IsOnCooldown = true;
			EmitSignalCooldownStarted();
			await ToSignal(GetTree().CreateTimer(cooldown, false), SceneTreeTimer.SignalName.Timeout);
			if (!IsCurrent(generation)) return;
			IsOnCooldown = false;
			EmitSignalCooldownEnded();
		}
		Reset();
	}

	private void ReleaseGateLease()
	{
		if (_actor?.ActionGate != null && _activeLease.IsValid)
		{
			_actor.ActionGate.Release(_activeLease);
		}
		_activeLease = ActorActionLease.None;
	}

	private static bool IsEquipped(Player player, Weapon weapon) =>
		player?.Inventory != null && player.Inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var slot) && ReferenceEquals(slot?.Item, weapon);

	private bool IsCurrent(ulong generation) => GodotObject.IsInstanceValid(this) && IsInsideTree() && generation == _generation;

	private void Reset()
	{
		IsDelayed = IsPerformingAction = IsOnCooldown = false;
		RemainingTime = 0;
		_actor = null;
		_activeLease = ActorActionLease.None;
		SignalBus.EmitCooldownUpdated(SlotIndex, 0, 0);
	}

	public override void _ExitTree() => _generation++;

	/// <summary>Interrupt preparation immediately, even if knockback ends before its timer would fire.</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (IsDelayed && (_actor.IsDead || _actor.IsHit))
		{
			_generation++;
			ReleaseGateLease();
			Reset();
		}
	}

	public override void _Process(double delta)
	{
		if (IsDelayed || IsPerformingAction || IsOnCooldown)
		{
			RemainingTime = System.Math.Max(RemainingTime - delta, 0);
			SignalBus.EmitCooldownUpdated(SlotIndex, (float)RemainingTime, (float)TotalDuration);
		}
	}
}