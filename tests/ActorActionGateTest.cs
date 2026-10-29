namespace RogueGauntlet.Tests;

using System;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class ActorActionGateTest
{
	[TestCase]
	public void InitialGateIsIdle()
	{
		var gate = new ActorActionGateComponent();
		AssertBool(gate.IsBusy).IsFalse();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.None);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.None);
	}

	[TestCase]
	public void AcquireSetsOwnershipAndReturnsValidLease()
	{
		var gate = new ActorActionGateComponent();
		var result = gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		AssertInt((int)result).IsEqual((int)ActorActionRequestResult.Success);
		AssertBool(lease.IsValid).IsTrue();
		AssertBool(gate.IsBusy).IsTrue();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.PrimaryAttack);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.WindUp);
	}

	[TestCase]
	public void CompetingAttackDuringWindUpIsRejectedAsBusy()
	{
		var gate = new ActorActionGateComponent();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease1);
		var result = gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease2);
		AssertInt((int)result).IsEqual((int)ActorActionRequestResult.Busy);
		AssertBool(lease2.IsValid).IsFalse();
	}

	[TestCase]
	public void ItemUseDuringAttackIsRejected()
	{
		var gate = new ActorActionGateComponent();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out _);
		var result = gate.TryAcquire(ActorActionKind.ItemUse, ActorActionPhase.Channeling, null, out var lease);
		AssertInt((int)result).IsEqual((int)ActorActionRequestResult.Busy);
		AssertBool(lease.IsValid).IsFalse();
	}

	[TestCase]
	public void ForcedReactionInterruptsActiveActionAndCallsInterruptionCallback()
	{
		var gate = new ActorActionGateComponent();
		ActionInterruptReason capturedReason = ActionInterruptReason.None;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.Active, reason => capturedReason = reason, out var attackLease);

		var result = gate.TryAcquire(ActorActionKind.ForcedReaction, ActorActionPhase.Stagger, null, out var reactionLease);
		AssertInt((int)result).IsEqual((int)ActorActionRequestResult.Success);
		AssertBool(reactionLease.IsValid).IsTrue();
		AssertInt((int)capturedReason).IsEqual((int)ActionInterruptReason.NewAction);
		AssertBool(gate.IsLeaseCurrent(attackLease)).IsFalse();
	}

	[TestCase]
	public void ReleaseClearsCurrentAction()
	{
		var gate = new ActorActionGateComponent();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.Active, null, out var lease);
		gate.Release(lease);
		AssertBool(gate.IsBusy).IsFalse();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.None);
	}

	[TestCase]
	public void StaleLeaseCannotReleaseOrReportPhase()
	{
		var gate = new ActorActionGateComponent();
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var oldLease);
		gate.CancelCurrent(ActionInterruptReason.Damage);

		gate.TryAcquire(ActorActionKind.ItemUse, ActorActionPhase.Channeling, null, out var newLease);
		gate.ReportPhase(oldLease, ActorActionPhase.Active);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.Channeling);

		gate.Release(oldLease);
		AssertBool(gate.IsBusy).IsTrue();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.ItemUse);
	}

	[TestCase]
	public void CommittedItemUseDeniesVoluntaryCancellation()
	{
		var gate = new ActorActionGateComponent();
		gate.TryAcquire(ActorActionKind.ItemUse, ActorActionPhase.Channeling, null, out _);

		var attackResult = gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out _);
		AssertInt((int)attackResult).IsEqual((int)ActorActionRequestResult.Busy);

		var dodgeResult = gate.TryAcquire(ActorActionKind.Dodge, ActorActionPhase.Active, null, out _);
		AssertInt((int)dodgeResult).IsEqual((int)ActorActionRequestResult.Busy);
	}

	/// <summary>Guard and the next primary step replace attack recovery only on gates that opt in (the player).</summary>
	[TestCase]
	public void RecoveryCancelsAreOptIn()
	{
		const ActorActionKind primary = ActorActionKind.PrimaryAttack, secondary = ActorActionKind.SecondaryTechnique;
		foreach (bool cancels in new[] { false, true })
		{
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Recovery, ActorActionKind.Guard, cancels)).IsEqual(cancels);
			AssertBool(ActorActionGateComponent.CanTransition(secondary, ActorActionPhase.Recovery, ActorActionKind.Guard, cancels)).IsEqual(cancels);
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Recovery, primary, cancels)).IsEqual(cancels);
			// Only a primary step chains, and only out of primary recovery.
			AssertBool(ActorActionGateComponent.CanTransition(secondary, ActorActionPhase.Recovery, primary, cancels)).IsFalse();
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Recovery, secondary, cancels)).IsFalse();
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Recovery, ActorActionKind.ItemUse, cancels)).IsFalse();
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Recovery, ActorActionKind.Dodge, cancels)).IsTrue();
			// Active contact stays committed; wind-up still yields only to defense.
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Active, ActorActionKind.Guard, cancels)).IsFalse();
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.Active, primary, cancels)).IsFalse();
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.WindUp, primary, cancels)).IsFalse();
			AssertBool(ActorActionGateComponent.CanTransition(primary, ActorActionPhase.WindUp, ActorActionKind.Guard, cancels)).IsTrue();
		}
	}

	[TestCase]
	public void ChainedStepInterruptsTheRecoveringStepWithANewLease()
	{
		var gate = new ActorActionGateComponent { RecoveryCancels = true };
		ActionInterruptReason reason = ActionInterruptReason.None;
		gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, r => reason = r, out var first);
		gate.ReportPhase(first, ActorActionPhase.Recovery);

		var result = gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var second);
		AssertInt((int)result).IsEqual((int)ActorActionRequestResult.Success);
		AssertInt((int)reason).IsEqual((int)ActionInterruptReason.NewAction);
		AssertBool(gate.IsLeaseCurrent(first)).IsFalse();
		AssertBool(gate.IsLeaseCurrent(second)).IsTrue();
		gate.Free();
	}
}