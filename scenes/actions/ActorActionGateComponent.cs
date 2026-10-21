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

	public ActorActionKind CurrentKind { get; private set; } = ActorActionKind.None;
	public ActorActionPhase CurrentPhase { get; private set; } = ActorActionPhase.None;
	public ulong CurrentGeneration { get; private set; } = 0;
	public ActorActionLease CurrentLease { get; private set; } = ActorActionLease.None;

	private Action<ActionInterruptReason> _currentOnInterrupted;
	private ulong _leaseSequence = 0;

	public bool IsBusy => CurrentKind != ActorActionKind.None;

	public ActorActionRequestResult TryAcquire(
		ActorActionKind kind,
		ActorActionPhase phase,
		Action<ActionInterruptReason> onInterrupted,
		out ActorActionLease lease)
	{
		lease = ActorActionLease.None;

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

		EmitSignalActionAcquired((int)kind, (int)phase);
		return ActorActionRequestResult.Success;
	}

	public void ReportPhase(ActorActionLease lease, ActorActionPhase phase)
	{
		if (!IsLeaseCurrent(lease)) return;
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

		// Attack wind-up: dodge/guard may cancel; interaction, another attack and item use are denied
		if (currentKind == ActorActionKind.PrimaryAttack || currentKind == ActorActionKind.SecondaryTechnique)
		{
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