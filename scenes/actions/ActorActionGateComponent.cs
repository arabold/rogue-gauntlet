using Godot;
using System;

/// <summary>
/// Exclusive committed-action ownership and allowed transitions using generation-stamped leases.
/// </summary>
[GlobalClass]
public partial class ActorActionGateComponent : Node
{
	[Signal] public delegate void ActionAcquiredEventHandler(int kind, int phase);
	[Signal] public delegate void ActionReleasedEventHandler(int kind);
	[Signal] public delegate void ActionInterruptedEventHandler(int kind, int reason);
	[Signal] public delegate void StaggerStartedEventHandler(float duration);
	[Signal] public delegate void StaggerEndedEventHandler();

	public ActorActionKind CurrentKind { get; private set; } = ActorActionKind.None;
	public ActorActionPhase CurrentPhase { get; private set; } = ActorActionPhase.None;
	public ulong CurrentGeneration { get; private set; } = 0;
	public ActorActionLease CurrentLease { get; private set; } = ActorActionLease.None;

	private Action<ActionInterruptReason> _currentOnInterrupted;
	private ulong _leaseSequence = 0;

	[Export] public CombatAnimationComponent AnimationComponent { get; set; }
	[Export] public DefenseAnimationProfile AnimationProfile { get; set; }
	public bool Disabled { get; private set; }
	public float StaggerRemaining => _staggerRemaining;

	/// <summary>Transfer an accepted preparation lease to its combat executor.</summary>
	public bool Adopt(ActorActionLease lease, Action<ActionInterruptReason> interrupted)
	{
		if (!IsLeaseCurrent(lease)) return false;
		_currentOnInterrupted = interrupted;
		return true;
	}

	public void ResetForSimulation()
	{
		CancelCurrent(ActionInterruptReason.Invalidated);
		Disabled = false;
	}

	public void Disable()
	{
		Disabled = true;
		CancelCurrent(ActionInterruptReason.Death);
	}

	public bool AllowsLocomotion => !IsBusy || (CurrentKind == ActorActionKind.Guard && CurrentPhase == ActorActionPhase.Active);
	public bool IsBusy => CurrentKind != ActorActionKind.None;
	public bool IsStaggered => CurrentKind == ActorActionKind.ForcedReaction;
	private float _staggerRemaining;
	private double _staggerElapsed;
	private ActorActionLease _staggerLease = ActorActionLease.None;

	public override void _Ready()
	{
		ProcessPhysicsPriority = CombatPhysics.Defense;
		SetPhysicsProcess(false);
	}

	public bool TryStagger(float duration)
	{
		if (!float.IsFinite(duration) || duration <= 0) return false;
		var result = TryAcquire(
			ActorActionKind.ForcedReaction,
			ActorActionPhase.Active,
			reason =>
			{
				_staggerRemaining = 0f;
				_staggerLease = ActorActionLease.None;
				AnimationComponent?.EndPose(this);
				SetPhysicsProcess(false);
				EmitSignalStaggerEnded();
			},
			out var lease);

		if (result != ActorActionRequestResult.Success)
		{
			return false;
		}

		_staggerLease = lease;
		_staggerRemaining = duration;
		_staggerElapsed = 0;
		AnimationComponent?.BeginPose(this, AnimationProfile?.StaggerClip ?? "", 0.05f);
		SetPhysicsProcess(true);
		EmitSignalStaggerStarted(duration);
		return true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_staggerRemaining > 0)
		{
			_staggerRemaining -= (float)delta;
			AnimationComponent?.SamplePose(this, System.Math.Max(0, _staggerElapsed += delta));
			if (_staggerRemaining <= 0)
			{
				_staggerRemaining = 0;
				if (IsLeaseCurrent(_staggerLease))
				{
					AnimationComponent?.EndPose(this);
					Release(_staggerLease);
					_staggerLease = ActorActionLease.None;
					EmitSignalStaggerEnded();
				}
				SetPhysicsProcess(false);
			}
		}
	}

	public ActorActionRequestResult TryAcquire(
		ActorActionKind kind,
		ActorActionPhase phase,
		Action<ActionInterruptReason> onInterrupted,
		out ActorActionLease lease)
	{
		lease = ActorActionLease.None;

		if (Disabled) return ActorActionRequestResult.Disabled;

		if (kind == ActorActionKind.None)
		{
			return ActorActionRequestResult.InvalidDefinition;
		}

		if (!CanTransition(CurrentKind, CurrentPhase, kind, phase))
		{
			return ActorActionRequestResult.Busy;
		}

		// Allowed transition: synchronously interrupt existing owner
		CancelCurrent(ActionInterruptReason.NewAction);

		CurrentGeneration++;
		_leaseSequence++;
		CurrentKind = kind;
		CurrentPhase = phase;
		_currentOnInterrupted = onInterrupted;
		CurrentLease = new ActorActionLease(CurrentGeneration, kind, _leaseSequence);
		lease = CurrentLease;

		if (CombatTrace.Enabled) CombatTrace.Write(this, "action.acquired", ("kind", kind.ToString()), ("phase", phase.ToString()), ("lease", lease.LeaseId));
		EmitSignalActionAcquired((int)kind, (int)phase);
		return ActorActionRequestResult.Success;
	}

	public void ReportPhase(ActorActionLease lease, ActorActionPhase phase)
	{
		if (!IsLeaseCurrent(lease)) return;
		if (CurrentPhase != phase && CombatTrace.Enabled) CombatTrace.Write(this, "action.phase", ("kind", CurrentKind.ToString()), ("phase", phase.ToString()), ("lease", lease.LeaseId));
		CurrentPhase = phase;
	}

	public void Release(ActorActionLease lease)
	{
		if (!IsLeaseCurrent(lease)) return;
		var oldKind = CurrentKind;
		CurrentGeneration++;
		CurrentLease = ActorActionLease.None;
		CurrentKind = ActorActionKind.None;
		CurrentPhase = ActorActionPhase.None;
		_currentOnInterrupted = null;

		if (CombatTrace.Enabled) CombatTrace.Write(this, "action.released", ("kind", oldKind.ToString()));
		EmitSignalActionReleased((int)oldKind);
	}

	public void CancelCurrent(ActionInterruptReason reason)
	{
		if (CurrentKind == ActorActionKind.None) return;
		var oldCallback = _currentOnInterrupted;
		var oldKind = CurrentKind;
		CurrentGeneration++;
		CurrentLease = ActorActionLease.None;
		CurrentKind = ActorActionKind.None;
		CurrentPhase = ActorActionPhase.None;
		_currentOnInterrupted = null;

		if (CombatTrace.Enabled) CombatTrace.Write(this, "action.interrupted", ("kind", oldKind.ToString()), ("reason", reason.ToString()));
		oldCallback?.Invoke(reason);
		EmitSignalActionInterrupted((int)oldKind, (int)reason);
	}

	public bool IsLeaseCurrent(ActorActionLease lease)
	{
		return lease.IsValid
			&& lease.LeaseId == CurrentLease.LeaseId
			&& lease.Generation == CurrentGeneration
			&& lease.Kind == CurrentKind;
	}

	private static bool CanTransition(
		ActorActionKind currentKind,
		ActorActionPhase currentPhase,
		ActorActionKind newKind,
		ActorActionPhase newPhase)
	{
		if (currentKind == ActorActionKind.None) return true;

		// Forced reaction (damage, stagger, death) overrides all voluntary actions
		if (newKind == ActorActionKind.ForcedReaction) return true;
		if (currentKind == ActorActionKind.ForcedReaction) return false;

		// Item use: committed channel denies voluntary cancellation
		if (currentKind == ActorActionKind.ItemUse) return false;

		// Dodge can also cancel recovery; active contact remains committed and cannot be undone.
		if (currentKind == ActorActionKind.PrimaryAttack || currentKind == ActorActionKind.SecondaryTechnique)
		{
			if (currentPhase == ActorActionPhase.Recovery && newKind == ActorActionKind.Dodge) return true;
			if (currentPhase == ActorActionPhase.WindUp)
			{
				return newKind == ActorActionKind.Dodge || newKind == ActorActionKind.Guard;
			}
			return false;
		}

		// Held guard: dodge may replace it
		if (currentKind == ActorActionKind.Guard)
		{
			return newKind == ActorActionKind.Dodge;
		}

		// World interaction: release, movement, combat or quick item cancels
		if (currentKind == ActorActionKind.WorldInteraction)
		{
			return true;
		}

		return false;
	}
}