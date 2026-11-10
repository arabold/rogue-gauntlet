using Godot;

/// <summary>A started attack or cast: lasts as long as its execution on the actor's <see cref="AttackController"/>.</summary>
public sealed class MonsterTimedRun(MonsterAction action, Node3D target, AttackSlotsComponent heldSlots,
	AttackController controller, AttackIdentity execution) : MonsterActionRun(action, target, heldSlots)
{
	public AttackIdentity Execution { get; } = execution;

	public override bool IsFinished => !GodotObject.IsInstanceValid(controller) || !controller.IsAttacking
		|| controller.CurrentExecutionId != Execution;

	/// <summary>
	/// Track the target through the wind-up (movement rotation is off while acting), no faster than the attack's authored
	/// turn rate, then hold the committed facing. An early facing lock plus a capped turn lets a target sidestep a swing.
	/// </summary>
	public override void Tick(EnemyContext context, double delta)
	{
		if (IsFinished || !GodotObject.IsInstanceValid(Target) || ReferenceEquals(Target, context.Actor)) return;
		if (controller.HasCommittedFacing) context.Movement.FaceDirection(controller.CommittedFacing);
		else context.Movement.FaceDirection(Target.GlobalPosition - context.Actor.GlobalPosition, controller.CurrentAttack?.WindUpTurnRate ?? 0);
	}

	public override void Cancel()
	{
		if (!IsFinished) controller.CancelAttack();
	}
}
