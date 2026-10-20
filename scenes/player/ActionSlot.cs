using Godot;

/// <summary>Accepts actions atomically and tracks preparation, execution and cooldown.</summary>
public partial class ActionSlot : Node
{
	[Signal] public delegate void PerformActionEventHandler();
	[Signal] public delegate void ActionPerformedEventHandler();
	[Signal] public delegate void CooldownStartedEventHandler();
	[Signal] public delegate void CooldownEndedEventHandler();

	public IPlayerAction AssignedAction { get; private set; }
	public PackedScene PreviewScene { get; private set; }
	public bool IsDelayed { get; private set; }
	public bool IsPerformingAction { get; private set; }
	public bool IsOnCooldown { get; private set; }
	public double TotalDuration { get; private set; }
	public double RemainingTime { get; private set; }
	public int SlotIndex { get; private set; }
	private ulong _generation;
	private Player _actor;

	/// <summary>Invalidate old asynchronous work when equipment or slot ownership changes.</summary>
	public void AssignAction(int slotIndex, IPlayerAction action, PackedScene previewScene)
	{
		_generation++;
		if (IsPerformingAction) EmitSignalActionPerformed();
		IsDelayed = IsPerformingAction = IsOnCooldown = false;
		RemainingTime = 0;
		SlotIndex = slotIndex;
		AssignedAction = action;
		PreviewScene = previewScene;
	}

	/// <summary>Resolve weapon animation/timing before accepting an action or publishing its cooldown.</summary>
	public async void TryPerformAction(Player player)
	{
		if (AssignedAction == null || IsDelayed || IsPerformingAction || IsOnCooldown || player.IsDead || player.IsHit) return;
		var action = AssignedAction;
		var weapon = action as Weapon;
		PreparedAttack attack = null;
		if (weapon != null && (!IsEquipped(player, weapon) || !player.AttackController.TryPrepareWeaponAttack(weapon, out attack))) return;

		_actor = player;
		ulong generation = ++_generation;
		double duration = attack?.Duration ?? action.PerformDuration;
		double delay = action.Delay;
		double cooldown = action.CooldownDuration;
		if (!double.IsFinite(delay) || !double.IsFinite(duration) || !double.IsFinite(cooldown)
			|| delay < 0 || duration < 0 || cooldown < 0) return;
		TotalDuration = delay + duration + cooldown;
		RemainingTime = TotalDuration;
		SignalBus.EmitCooldownUpdated(SlotIndex, (float)TotalDuration, (float)RemainingTime);

		if (delay > 0)
		{
			IsDelayed = true;
			await ToSignal(GetTree().CreateTimer(delay, false), SceneTreeTimer.SignalName.Timeout);
			if (!IsCurrent(generation)) return;
		}
		IsDelayed = false;
		if (player.IsDead || player.IsHit || (weapon != null && !IsEquipped(player, weapon)))
		{
			Reset();
			return;
		}

		if (weapon != null && !player.AttackController.StartPreparedWeaponAttack(attack, weapon))
		{
			Reset();
			return;
		}
		IsPerformingAction = true;
		EmitSignalPerformAction();
		if (weapon == null)
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

	private static bool IsEquipped(Player player, Weapon weapon) =>
		player.Inventory.EquippedItems.TryGetValue(EquipmentSlot.WeaponHand, out var slot) && ReferenceEquals(slot?.Item, weapon);

	private bool IsCurrent(ulong generation) => GodotObject.IsInstanceValid(this) && IsInsideTree() && generation == _generation;

	private void Reset()
	{
		IsDelayed = IsPerformingAction = IsOnCooldown = false;
		RemainingTime = 0;
		_actor = null;
		SignalBus.EmitCooldownUpdated(SlotIndex, 0, 0);
	}

	public override void _ExitTree() => _generation++;

	/// <summary>Interrupt preparation immediately, even if knockback ends before its timer would fire.</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (IsDelayed && (_actor.IsDead || _actor.IsHit))
		{
			_generation++;
			Reset();
		}
	}

	public override void _Process(double delta)
	{
		if (IsDelayed || IsPerformingAction || IsOnCooldown)
		{
			RemainingTime = System.Math.Max(RemainingTime - delta, 0);
			SignalBus.EmitCooldownUpdated(SlotIndex, (float)TotalDuration, (float)RemainingTime);
		}
	}
}