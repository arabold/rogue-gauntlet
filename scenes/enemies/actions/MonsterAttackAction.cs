using Godot;

/// <summary>One melee or projectile attack executed by the actor's <see cref="AttackController"/>.</summary>
[Tool]
[GlobalClass]
public partial class MonsterAttackAction : MonsterTimedAction
{
	[ExportGroup("Attack")]
	[Export] public AttackDefinition Attack { get; set; }

	protected override AttackDefinition Definition => Attack;

	/// <summary>Projectile range, or the melee contact's engagement distance; an attack without one never caps range.</summary>
	public override float Reach => Attack == null ? float.PositiveInfinity
		: Attack.IsRanged ? Attack.Range : Attack.MeleeContact?.EngagementDistance ?? float.PositiveInfinity;

	public override AttackSlotKind? SlotKind => Attack?.IsRanged == true ? AttackSlotKind.Ranged : AttackSlotKind.Melee;

	public override ActorActionRequestResult TryStart(EnemyContext context, Node3D target, out MonsterActionRun run)
	{
		run = null;
		var controller = context.Enemy?.AttackController;
		if (controller == null || Attack == null) return ActorActionRequestResult.Unavailable;
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
		context.CheckAttackDefinition(Attack, Duration, ResolvedStyle);
	}
}
