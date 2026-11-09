using Godot;

/// <summary>One melee or projectile attack executed by the actor's <see cref="AttackController"/>.</summary>
[Tool]
[GlobalClass]
public partial class MonsterAttackAction : MonsterTimedAction
{
	[ExportGroup("Attack")]
	[Export] public AttackDefinition Attack { get; set; }
	/// <summary>A defense may answer a blocked attack with this one, after its guard recovery.</summary>
	[Export] public bool RiposteEligible { get; set; }

	protected override AttackDefinition Definition => Attack;

	/// <summary>Projectile range, or the melee contact's engagement distance; an attack without one never caps range.</summary>
	public override float Reach => Attack == null ? float.PositiveInfinity
		: Attack.IsRanged ? Attack.Range : Attack.MeleeContact?.EngagementDistance ?? float.PositiveInfinity;

	public override AttackSlotKind? SlotKind => Attack?.IsRanged == true ? AttackSlotKind.Ranged : AttackSlotKind.Melee;
	public override bool CanRiposte => RiposteEligible;

	public override ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run)
	{
		run = null;
		var controller = context.Actor.AttackController;
		if (Attack == null) return ActorActionRequestResult.InvalidDefinition;
		if (!context.TryReserveSlot(ReservedSlot, out var slots)) return ActorActionRequestResult.Busy;
		if (!controller.TryPrepareAttack(Attack, Style, Duration, out var prepared)
			|| !controller.StartAttack(prepared, ResolvePower(context), HostileMask))
		{
			slots?.Release(context.Actor, yield: false);
			return ActorActionRequestResult.InvalidDefinition;
		}
		context.NotifyAttackAccepted(controller.CurrentExecutionId);
		run = new MonsterTimedRun(this, target, slots, controller, controller.CurrentExecutionId);
		return ActorActionRequestResult.Success;
	}

	public override void Validate(MonsterValidationContext context)
	{
		base.Validate(context);
		context.Check(IsOffensive, "attack actions must target a hostile.");
		context.Check(Duration > 0, "attack Duration must be positive.");
		if (!context.Check(Attack != null, "assign an AttackDefinition.")) return;
		ValidateDelivery(context);
		ValidateTiming(context, Attack, Duration, context.ContactStart(ResolvedStyle) is { } start ? Duration * start : null);
	}

	/// <summary>The payload must reach something: a projectile scene, or melee contact with the actor's detector and warning.</summary>
	private void ValidateDelivery(MonsterValidationContext context)
	{
		foreach (var status in Attack.OnHitStatuses ?? [])
			context.Check(status?.IsValid == true, "contains an invalid on-hit status payload (refreshing statuses need a saved buff file).");
		context.Check(Attack.IsRanged ? Attack.ProjectileScene != null : Attack.MeleeContact != null,
			"must bind a projectile scene or a melee contact profile.");
		if (!Attack.IsRanged)
			context.CheckWiring(MonsterValidationContext.Object<Node>(context.AttackController, "MeleeDetector") != null,
				"AttackController.MeleeDetector: assign a hit detector.");
		context.CheckWiring(context.Local(MonsterValidationContext.Object<Node>(context.AttackController, "Indicator")),
			"AttackController.Indicator: assign this actor's shared attack warning.");
	}

	/// <summary>The attack definition's own rules, against the wind-up this action and its binding prepare.</summary>
	private static void ValidateTiming(MonsterValidationContext context, AttackDefinition attack, float duration, float? windUp)
	{
		context.Check(attack.HasValidImpactValues, "Attack: poise damage, knockback, hit-stop and wind-up turn rate must be nonnegative and finite.");
		context.Check(attack.HasValidArmorWindow, "Attack.HyperArmorStart/End: require 0 <= start <= end <= 1.");
		context.Check(attack.HasValidFacingLock(windUp), "Attack.FacingLockLead: must be nonnegative, melee only and no longer than the prepared wind-up.");
		context.Check(attack.HasValidFlight(windUp, windUp != null ? duration : null),
			"Attack flight: stagger, homing and launch ramp must be finite and nonnegative with a launch scale in (0, 1]; stagger and homing belong to aimed single/spread shots whose last projectile leaves within the attack.");
		var tier = attack.ThreatTier;
		context.Check(!(windUp < tier.MinimumWindUp() - .001f), $"Attack: {tier} attacks need at least {tier.MinimumWindUp()} s of wind-up before contact.");
		float aimLock = attack.ProjectileAimLockDuration;
		context.Check(MonsterValidationContext.IsNonnegative(aimLock), "Attack.ProjectileAimLockDuration: must be nonnegative and finite.");
		if (aimLock <= 0) return;
		context.Check(attack.IsRanged && attack.ProjectilePattern is ProjectilePattern.Single or ProjectilePattern.Spread,
			"Attack.ProjectileAimLockDuration: only aimed single/spread shots support locking.");
		context.Check(windUp == null || aimLock <= windUp, "Attack.ProjectileAimLockDuration: cannot exceed the prepared wind-up.");
	}
}
