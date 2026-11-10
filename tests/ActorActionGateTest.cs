namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class ActorActionGateTest
{
	/// <summary>An acquired lease owns the gate, with its kind and phase, until that lease is released.</summary>
	[TestCase]
	public void AcquireOwnsTheGateUntilTheLeaseIsReleased()
	{
		var gate = AutoFree(new ActorActionGateComponent());
		var result = gate.TryAcquire(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, null, out var lease);
		AssertInt((int)result).IsEqual((int)ActorActionRequestResult.Success);
		AssertBool(lease.IsValid).IsTrue();
		AssertBool(gate.IsBusy).IsTrue();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.PrimaryAttack);
		AssertInt((int)gate.CurrentPhase).IsEqual((int)ActorActionPhase.WindUp);
		gate.Release(lease);
		AssertBool(gate.IsBusy).IsFalse();
		AssertInt((int)gate.CurrentKind).IsEqual((int)ActorActionKind.None);
	}

	/// <summary>
	/// A busy gate rejects competing voluntary actions: another attack or an item during an attack's wind-up, and an
	/// attack or a dodge during a committed item use.
	/// </summary>
	[TestCase]
	public void ABusyGateRejectsCompetingVoluntaryActions()
	{
		foreach (var (held, phase, request, requestPhase) in new[]
		{
			(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp),
			(ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp, ActorActionKind.ItemUse, ActorActionPhase.Channeling),
			(ActorActionKind.ItemUse, ActorActionPhase.Channeling, ActorActionKind.PrimaryAttack, ActorActionPhase.WindUp),
			(ActorActionKind.ItemUse, ActorActionPhase.Channeling, ActorActionKind.Dodge, ActorActionPhase.Active),
		})
		{
			var gate = AutoFree(new ActorActionGateComponent());
			gate.TryAcquire(held, phase, null, out var current);
			var result = gate.TryAcquire(request, requestPhase, null, out var rejected);
			AssertInt((int)result).OverrideFailureMessage($"{request} during {held} {phase}").IsEqual((int)ActorActionRequestResult.Busy);
			AssertBool(rejected.IsValid).IsFalse();
			AssertBool(gate.IsLeaseCurrent(current)).IsTrue();
		}
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
			AssertBool(ActorActionGateComponent.CanTransition(secondary, ActorActionPhase.Recovery, secondary, cancels)).IsEqual(cancels);
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