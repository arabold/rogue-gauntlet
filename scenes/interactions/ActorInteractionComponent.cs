using Godot;

/// <summary>
/// Actor-local coordinator for timed world interactions.
/// Owns the stationary channel clock, target reservation, cancellation, and gate lease.
/// </summary>
public partial class ActorInteractionComponent : Node
{
	[Signal] public delegate void InteractionStartedEventHandler(string verb, float duration);
	[Signal] public delegate void InteractionProgressEventHandler(string verb, float elapsed, float duration);
	[Signal] public delegate void InteractionCompletedEventHandler(string verb, int result);
	[Signal] public delegate void InteractionCancelledEventHandler(int reason);

	[Export] public Node3D Actor { get; set; }
	[Export] public ActorActionGateComponent ActionGate { get; set; }

	public bool IsActive { get; private set; }
	public IInteractionTarget CurrentTarget { get; private set; }
	public InteractionOffer CurrentOffer { get; private set; }
	public InteractionReservation CurrentReservation { get; private set; }
	public float Elapsed { get; private set; }
	public float Duration { get; private set; }
	public float Progress => Duration > 0 ? Mathf.Clamp(Elapsed / Duration, 0f, 1f) : 1f;
	public ulong Generation { get; private set; }

	private ActorActionLease _currentLease = ActorActionLease.None;
	private Vector3 _startActorPosition = Vector3.Zero;

	public override void _Ready()
	{
		Actor ??= GetOwner<Node3D>() ?? GetParent<Node3D>();
		ActionGate ??= Actor?.GetNodeOrNull<ActorActionGateComponent>("ActorActionGateComponent");
		SetPhysicsProcess(false);
	}

	public InteractionRequestResult TryBegin(in InteractionOffer offer, in ActorInteractionContext context)
	{
		if (IsActive)
		{
			return InteractionRequestResult.Busy;
		}

		if (Actor == null || !IsInstanceValid(Actor))
		{
			return InteractionRequestResult.ActorDead;
		}

		if (Actor is Player player && player.IsDead)
		{
			return InteractionRequestResult.ActorDead;
		}

		if (!offer.IsValid || offer.Target == null || !offer.Target.IsInteractive)
		{
			return InteractionRequestResult.NoOffer;
		}

		if (!context.HasPermission(offer.RequiredPermission))
		{
			return InteractionRequestResult.PermissionDenied;
		}

		if (offer.Target.TargetNode != null)
		{
			Vector3 actorPos = GetActorPosition();
			Vector3 targetPos = GetNodePosition(offer.Target.TargetNode);
			float horizDist = new Vector2(actorPos.X - targetPos.X, actorPos.Z - targetPos.Z).Length();
			float vertDist = Mathf.Abs(actorPos.Y - targetPos.Y);
			if (horizDist > offer.Range || vertDist > 2.5f)
			{
				return InteractionRequestResult.OutOfRange;
			}
		}

		if (Actor is CharacterBody3D body)
		{
			body.Velocity = Vector3.Zero;
		}

		if (!offer.Target.TryReserve(offer, Actor, out var reservation))
		{
			return InteractionRequestResult.ReservationFailed;
		}

		ActorActionLease lease = ActorActionLease.None;
		if (ActionGate != null)
		{
			var gateResult = ActionGate.TryAcquire(
				ActorActionKind.WorldInteraction,
				ActorActionPhase.Channeling,
				reason => Cancel(InteractionCancelReason.ActionInterrupted),
				out lease);

			if (gateResult != ActorActionRequestResult.Success)
			{
				offer.Target.Release(reservation);
				return InteractionRequestResult.GateFailed;
			}
		}

		IsActive = true;
		SetPhysicsProcess(true);
		Generation++;
		CurrentTarget = offer.Target;
		CurrentOffer = offer;
		CurrentReservation = reservation;
		_currentLease = lease;
		Elapsed = 0f;
		Duration = Mathf.Max(0f, offer.Duration);
		_startActorPosition = GetActorPosition();

		EmitSignalInteractionStarted(CurrentOffer.VerbLabel, Duration);

		if (Duration <= 0f)
		{
			Commit();
		}

		return InteractionRequestResult.Success;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsActive)
		{
			return;
		}

		if (Actor == null || !IsInstanceValid(Actor))
		{
			Cancel(InteractionCancelReason.ActorDied);
			return;
		}

		if (Actor is Player player && player.IsDead)
		{
			Cancel(InteractionCancelReason.ActorDied);
			return;
		}

		// Movement displacement check
		float displacement = GetActorPosition().DistanceTo(_startActorPosition);
		if (displacement > 0.4f)
		{
			Cancel(InteractionCancelReason.Movement);
			return;
		}

		// Target validity & range check
		if (CurrentTarget == null || !CurrentTarget.IsInteractive)
		{
			Cancel(InteractionCancelReason.TargetInvalidated);
			return;
		}

		if (CurrentTarget.TargetNode != null)
		{
			float dist = GetActorPosition().DistanceTo(GetNodePosition(CurrentTarget.TargetNode));
			if (dist > CurrentOffer.Range + 0.5f)
			{
				Cancel(InteractionCancelReason.OutOfRange);
				return;
			}
		}

		Elapsed += (float)delta;
		EmitSignalInteractionProgress(CurrentOffer.VerbLabel, Elapsed, Duration);

		if (Elapsed >= Duration)
		{
			Commit();
		}
	}

	private void TeardownSession(out IInteractionTarget target, out InteractionReservation reservation)
	{
		target = CurrentTarget;
		reservation = CurrentReservation;
		var lease = _currentLease;

		IsActive = false;
		SetPhysicsProcess(false);
		CurrentTarget = null;
		CurrentReservation = InteractionReservation.None;
		_currentLease = ActorActionLease.None;
		Elapsed = 0f;

		if (ActionGate != null && lease.IsValid)
		{
			ActionGate.Release(lease);
		}

		target?.Release(reservation);
	}

	public void Cancel(InteractionCancelReason reason)
	{
		if (!IsActive)
		{
			return;
		}

		Generation++;
		TeardownSession(out _, out _);
		EmitSignalInteractionCancelled((int)reason);
	}

	private void Commit()
	{
		var offer = CurrentOffer;
		var result = CurrentTarget?.TryCommit(CurrentReservation) ?? InteractionCommitResult.TargetInvalidated;

		TeardownSession(out _, out _);
		EmitSignalInteractionCompleted(offer.VerbLabel, (int)result);
	}

	// Variant-compatible debug helpers
	public void CancelCurrent() => Cancel(InteractionCancelReason.InputReleased);
	public float GetCurrentProgress() => Progress;
	public bool IsChanneling() => IsActive;
private Vector3 GetActorPosition() => GetNodePosition(Actor);
	private static Vector3 GetNodePosition(Node3D node) => node == null ? Vector3.Zero : (node.IsInsideTree() ? node.GlobalPosition : node.Position);
}
