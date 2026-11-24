using Godot;

/// <summary>
/// A started charge: faces the target through the wind-up while warning with the run's lane, then runs along the
/// committed facing for the contact window. The attack's own melee contact moves with the actor, so it hits whatever
/// the run reaches. A head-on wall ends the attack and staggers the actor; any other end releases the run.
/// </summary>
public sealed class MonsterChargeRun : MonsterActionRun
{
	/// <summary>Height of the ray that measures how far the lane runs before a wall.</summary>
	private const float LaneSightHeight = 1;
	/// <summary>A wall stops a charge only when its normal faces the run within about 60°, so a glancing wall slides it along.</summary>
	private const float HeadOnShare = 0.5f;

	private readonly MonsterChargeAction _action;
	private readonly AttackController _controller;
	private readonly AttackIdentity _execution;
	private EnemyBase _actor;
	private MovementComponent _movement;
	private bool _running;

	public MonsterChargeRun(MonsterChargeAction action, Node3D target, AttackSlotsComponent heldSlots,
		AttackController controller, AttackIdentity execution) : base(action, target, heldSlots)
	{
		_action = action;
		_controller = controller;
		_execution = execution;
		_controller.AttackFinished += OnAttackFinished;
	}

	public override bool IsFinished => !GodotObject.IsInstanceValid(_controller) || !_controller.IsAttacking
		|| _controller.CurrentExecutionId != _execution;

	public override void Tick(EnemyContext context, double delta)
	{
		if (IsFinished) return;
		_actor = context.Actor;
		_movement = context.Movement;
		switch (_controller.Phase)
		{
			case ActorActionPhase.WindUp:
				if (GodotObject.IsInstanceValid(Target) && !ReferenceEquals(Target, _actor))
				{
					if (_controller.HasCommittedFacing) _movement.FaceDirection(_controller.CommittedFacing);
					else _movement.FaceDirection(Target.GlobalPosition - _actor.GlobalPosition, _controller.CurrentAttack?.WindUpTurnRate ?? 0);
				}
				_controller.ShowWarningLane(_execution, LaneLength());
				break;
			case ActorActionPhase.Active:
				Vector3 heading = _controller.HasCommittedFacing ? _controller.CommittedFacing : _movement.LookAtDirection;
				if (_running && MetWall(heading))
				{
					Impact();
					return;
				}
				_running = true;
				_movement.FaceDirection(heading);
				_movement.SetLocomotionOverride(this, heading, _action.Speed);
				break;
			default:
				StopRunning();
				break;
		}
	}

	public override void Cancel()
	{
		StopRunning();
		if (!IsFinished) _controller.CancelAttack();
	}

	/// <summary>The run plus the contact's reach beyond where it stops, cut short by the first wall along the facing.</summary>
	private float LaneLength()
	{
		var attack = _controller.CurrentAttack;
		if (attack?.MeleeContact == null) return 0;
		float length = _action.Speed * Mathf.Max(0, attack.HitWindowEnd - attack.HitWindowStart) + attack.MeleeContact.Reach;
		Vector3 facing = _controller.HasCommittedFacing ? _controller.CommittedFacing : _movement.LookAtDirection;
		return WorldSight.ClearDistance(_actor.GetWorld3D(), _actor.GlobalPosition + Vector3.Up * LaneSightHeight, facing, length);
	}

	/// <summary>Whether last frame's move met solid level geometry head-on; bodies (the hero, other monsters) never stop a charge.</summary>
	private bool MetWall(Vector3 heading)
	{
		if (_movement.LastMotionKind != MovementMotionKind.Override) return false;
		for (int i = 0; i < _actor.GetSlideCollisionCount(); i++)
		{
			var collision = _actor.GetSlideCollision(i);
			if (collision.GetCollider() is CharacterBody3D) continue;
			Vector3 normal = collision.GetNormal();
			if (Mathf.Abs(normal.Y) < 0.5f && normal.Dot(-heading) > HeadOnShare) return true;
		}
		return false;
	}

	/// <summary>End the charge against the wall: the attack stops and the actor reels for the authored stagger.</summary>
	private void Impact()
	{
		StopRunning();
		_controller.CancelAttack();
		if (CombatTrace.Enabled) CombatTrace.Write(_actor, "charge.impact", ("execution", _execution.Sequence),
			("position", CombatTrace.Position(_actor)));
		_action.ImpactShake?.EmitAt(_actor.GlobalPosition);
		if (_action.ImpactStagger > 0) _actor.TryStagger(_action.ImpactStagger);
	}

	private void OnAttackFinished(AttackIdentity execution, bool interrupted)
	{
		if (execution != _execution) return;
		StopRunning();
		if (GodotObject.IsInstanceValid(_controller)) _controller.AttackFinished -= OnAttackFinished;
	}

	private void StopRunning()
	{
		if (!_running) return;
		_running = false;
		if (GodotObject.IsInstanceValid(_movement)) _movement.ClearLocomotionOverride(this);
	}
}
