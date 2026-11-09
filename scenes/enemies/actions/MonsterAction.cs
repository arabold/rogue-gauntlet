using Godot;

/// <summary>
/// One authored thing a monster can do: when it may start (position, target, conditions), how likely it is (priority,
/// weight, weight modifiers) and how it starts (<see cref="TryStart"/>). Each subclass wraps one existing executor, so a
/// new kind of action is a new subclass and never an edit to the behavior host.
/// </summary>
/// <remarks>
/// Actions are shared, immutable definitions: cooldowns, condition state and history live in the actor's
/// <see cref="MonsterActionRuntime"/>, and a started action's state lives in its <see cref="MonsterActionRun"/>.
/// Range is measured to the action's selected target, so it means the same for attacks, self spells and ally spells.
/// </remarks>
[Tool]
[GlobalClass]
public abstract partial class MonsterAction : Resource
{
	[ExportGroup("Selection")]
	/// <summary>Only the highest priority among eligible actions competes; weights choose within that tier.</summary>
	[Export] public int Priority { get; set; }
	[Export(PropertyHint.Range, "0,100,0.05")] public float Weight { get; set; } = 1;
	[Export(PropertyHint.Range, "0,120,0.1,suffix:s")] public float Cooldown { get; set; } = 1;

	[ExportGroup("Position")]
	[Export(PropertyHint.Range, "0,40,0.1,suffix:m")] public float MinimumRange { get; set; }
	/// <summary>Farthest start distance; the executor's own reach (<see cref="Reach"/>) caps it.</summary>
	[Export(PropertyHint.Range, "0,60,0.1,suffix:m")] public float MaximumRange { get; set; } = 3;
	/// <summary>Widest angle between the actor's facing and its target at which the action may start; 180 = any.</summary>
	[Export(PropertyHint.Range, "0,180,1,suffix:°")] public float MaximumStartAngle { get; set; } = 180;

	[ExportGroup("Targeting")]
	/// <summary>Who the action is aimed at; empty means the current hostile target, which must be in sight.</summary>
	[Export] public MonsterTargetSelector Target { get; set; }
	/// <summary>Take a turn through the target's attack slots. Bosses that must never wait behind minions turn it off.</summary>
	[Export] public bool UsesAttackSlot { get; set; } = true;
	/// <summary>A defense may answer a blocked attack with this action.</summary>
	[Export] public bool RiposteEligible { get; set; }

	[ExportGroup("Conditions")]
	/// <summary>Every condition must hold for the selected target.</summary>
	[Export] public Godot.Collections.Array<MonsterActionCondition> Conditions { get; set; } = [];
	/// <summary>Multipliers applied to <see cref="Weight"/> while their condition holds.</summary>
	[Export] public Godot.Collections.Array<MonsterActionWeightModifier> WeightModifiers { get; set; } = [];

	/// <summary>The authored selector, or a default hostile selector created for this action on first use.</summary>
	public MonsterTargetSelector EffectiveTarget => Target ?? (_defaultTarget ??= new HostileTargetSelector());
	private MonsterTargetSelector _defaultTarget;
	/// <summary>Offensive actions obey the tactical attack permission and attack slots; support and evasive ones do not.</summary>
	public virtual bool IsOffensive => EffectiveTarget.IsHostile;
	/// <summary>The executor's own reach; actions without one reach anywhere.</summary>
	public virtual float Reach => float.PositiveInfinity;
	public float EffectiveMaximumRange => Mathf.Min(MaximumRange, Reach);
	/// <summary>The slot kind the executor would occupy; null means the action never takes a slot.</summary>
	public virtual AttackSlotKind? SlotKind => null;
	/// <summary>The slot this action reserves when it starts, if any.</summary>
	public AttackSlotKind? ReservedSlot => UsesAttackSlot && IsOffensive ? SlotKind : null;

	/// <summary>Whether the actor has what the action needs right now (components, mana).</summary>
	public virtual bool IsAvailable(EnemyContext context) => true;

	/// <summary>Whether starting against <paramref name="target"/> would change anything, e.g. a heal on a hurt ally.</summary>
	public virtual bool IsUsefulOn(EnemyContext context, Node3D target) => true;

	/// <summary>Range, facing and sight against one candidate target; conditions and cooldown are checked by the runtime.</summary>
	public bool IsInPosition(EnemyContext context, Node3D target)
	{
		if (!GodotObject.IsInstanceValid(target)) return false;
		if (ReferenceEquals(target, context.Actor)) return MinimumRange <= 0;
		float distance = context.Actor.GlobalPosition.HorizontalDistanceTo(target.GlobalPosition);
		if (distance < MinimumRange || distance > EffectiveMaximumRange) return false;
		return MaximumStartAngle >= 180
			|| context.Movement.LookAtDirection.HorizontalAngleTo(target.GlobalPosition - context.Actor.GlobalPosition) <= MaximumStartAngle;
	}

	/// <summary>
	/// Start the executor against <paramref name="target"/>. A failure leaves no side effects: no slot, mana,
	/// cooldown, animation or gate ownership.
	/// </summary>
	public abstract ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run);

	/// <summary>Authoring problems for this action against the actor it is assigned to.</summary>
	public virtual void Validate(MonsterValidationContext context)
	{
		context.Check(float.IsFinite(Weight) && Weight > 0, "Weight must be positive.");
		context.Check(float.IsFinite(Cooldown) && Cooldown >= 0, "Cooldown must be nonnegative.");
		context.Check(float.IsFinite(MinimumRange) && MinimumRange >= 0 && float.IsFinite(MaximumRange) && MaximumRange >= MinimumRange,
			"require 0 <= MinimumRange <= MaximumRange.");
		context.Check(float.IsFinite(MaximumStartAngle) && MaximumStartAngle is >= 0 and <= 180, "MaximumStartAngle must lie between 0 and 180.");
		context.Check(!RiposteEligible || this is MonsterAttackAction, "only attack actions can be riposte eligible.");
		EffectiveTarget.Validate(context);
		foreach (var condition in Conditions)
		{
			context.Check(condition != null, "Conditions has an empty entry.");
			condition?.Validate(context);
		}
		foreach (var modifier in WeightModifiers)
		{
			context.Check(modifier?.Condition != null && float.IsFinite(modifier.Multiplier) && modifier.Multiplier >= 0,
				"WeightModifiers need a condition and a nonnegative multiplier.");
			modifier?.Condition?.Validate(context);
		}
	}

	/// <summary>Name for traces and validation messages.</summary>
	public string Label => !string.IsNullOrEmpty(ResourceName) ? ResourceName
		: !string.IsNullOrEmpty(ResourcePath) ? ResourcePath.GetFile() : GetType().Name;
}
