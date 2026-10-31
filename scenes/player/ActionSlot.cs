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

	public override void _Ready()
	{
		SetProcess(false);
		SetPhysicsProcess(false);
	}

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
		SetProcess(false);
		SetPhysicsProcess(false);
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

	/// <summary>
	/// Resolve weapon animation/timing before accepting an action or publishing its cooldown, and report whether
	/// it was accepted. <paramref name="chain"/> lets the next primary combo step replace this slot's own attack;
	/// the action gate decides whether the attack's current phase allows that.
	/// </summary>
	public bool TryPerformAction(Player player, bool chain = false)
	{
		bool replacing = chain && IsPerformingAction && !IsDelayed && !IsOnCooldown;
		if (AssignedAction == null || IsDelayed || (IsPerformingAction && !replacing) || IsOnCooldown || player.IsDead || player.IsHit) return false;
		var action = AssignedAction;
		var weapon = action as Weapon;
		var attackAction = action as ICombatAttackAction;
		PreparedAttack attack = null;

		if (attackAction != null)
		{
			if (weapon != null && !IsEquipped(player, weapon)) return false;
			if (!attackAction.TryPrepare(player, out attack)) return false;
		}

		double duration = attack?.Duration ?? action.PerformDuration;
		double delay = action.Delay;
		double cooldown = action.CooldownDuration;
		if (!double.IsFinite(delay) || !double.IsFinite(duration) || !double.IsFinite(cooldown)
			|| delay < 0 || duration < 0 || cooldown < 0) return false;

		// Acquire shared action gate lease; a replaced attack is cancelled by the gate before this returns.
		ActorActionKind kind = ActionKind;
		ActorActionPhase initialPhase = (delay > 0 || attackAction != null) ? ActorActionPhase.WindUp : ActorActionPhase.Active;
		var lease = ActorActionLease.None;
		if (player.ActionGate != null
			&& player.ActionGate.TryAcquire(kind, initialPhase, reason => CancelExecution(reason), out lease) != ActorActionRequestResult.Success)
		{
			return false;
		}

		// A new generation also ends the replaced execution's wait without completing it.
		_actor = player;
		_activeLease = lease;
		ulong generation = ++_generation;
		Execute(player, action, attack, generation, delay, duration, cooldown);
		return true;
	}

	private async void Execute(Player player, IPlayerAction action, PreparedAttack attack, ulong generation, double delay, double duration, double cooldown)
	{
		var weapon = action as Weapon;
		var attackAction = action as ICombatAttackAction;
		TotalDuration = delay + duration + cooldown;
		RemainingTime = TotalDuration;
		SetProcess(true);
		SetPhysicsProcess(true);
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

	private static bool IsEquipped(Player player, Weapon weapon) => ReferenceEquals(player?.Inventory?.EquippedWeapon, weapon);

	private bool IsCurrent(ulong generation) => GodotObject.IsInstanceValid(this) && IsInsideTree() && generation == _generation;

	private void Reset()
	{
		IsDelayed = IsPerformingAction = IsOnCooldown = false;
		RemainingTime = 0;
		_actor = null;
		_activeLease = ActorActionLease.None;
		SetProcess(false);
		SetPhysicsProcess(false);
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