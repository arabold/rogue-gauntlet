namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Plain turn-taking rules behind the target-owned attack slots.</summary>
[TestSuite]
public class AttackSlotLedgerTest
{
	[TestCase]
	public void CapacityIsPerKindAndReservationIsIdempotent()
	{
		var ledger = new AttackSlotLedger(2, 1, .75f);
		AssertBool(ledger.TryReserve(1, AttackSlotKind.Melee)).IsTrue();
		AssertBool(ledger.TryReserve(1, AttackSlotKind.Melee)).IsTrue();
		AssertBool(ledger.TryReserve(2, AttackSlotKind.Melee)).IsTrue();
		AssertBool(ledger.TryReserve(3, AttackSlotKind.Melee)).IsFalse();
		AssertBool(ledger.TryReserve(3, AttackSlotKind.Ranged)).IsTrue();
		AssertBool(ledger.TryReserve(4, AttackSlotKind.Ranged)).IsFalse();
		AssertInt(ledger.Held(AttackSlotKind.Melee)).IsEqual(2);
	}

	[TestCase]
	public void ARecentAttackerYieldsOnlyWhileAnotherWaits()
	{
		var ledger = new AttackSlotLedger(1, 1, .75f);
		AssertBool(ledger.TryReserve(1, AttackSlotKind.Melee)).IsTrue();
		ledger.Release(1);
		// Alone, the same attacker may strike again at its normal cadence.
		AssertBool(ledger.CanReserve(1, AttackSlotKind.Melee)).IsTrue();
		// Once another attacker waits, the recent one yields until its delay expires.
		AssertBool(ledger.CanReserve(2, AttackSlotKind.Melee)).IsTrue();
		AssertBool(ledger.CanReserve(1, AttackSlotKind.Melee)).IsFalse();
		ledger.Tick(.8);
		AssertBool(ledger.CanReserve(1, AttackSlotKind.Melee)).IsTrue();
	}

	[TestCase]
	public void AFailedStartReleasesWithoutYieldingAndForgetDropsEverything()
	{
		var ledger = new AttackSlotLedger(1, 1, .75f);
		ledger.Engage(2, AttackSlotKind.Melee);
		AssertBool(ledger.TryReserve(1, AttackSlotKind.Melee)).IsTrue();
		ledger.Release(1, yield: false);
		AssertBool(ledger.CanReserve(1, AttackSlotKind.Melee)).IsTrue();
		AssertBool(ledger.TryReserve(1, AttackSlotKind.Melee)).IsTrue();
		ledger.Forget(1);
		AssertBool(ledger.Holds(1)).IsFalse();
		AssertInt(ledger.EngagedCount).IsEqual(1);
		AssertBool(ledger.TryReserve(2, AttackSlotKind.Melee)).IsTrue();
	}

	[TestCase]
	public void RingIndicesFollowArrivalAndCloseUpWhenAnAttackerLeaves()
	{
		var ledger = new AttackSlotLedger(2, 1, .75f);
		AssertInt(ledger.Engage(10, AttackSlotKind.Melee)).IsEqual(0);
		AssertInt(ledger.Engage(20, AttackSlotKind.Melee)).IsEqual(1);
		AssertInt(ledger.Engage(30, AttackSlotKind.Ranged)).IsEqual(2);
		AssertInt(ledger.Engage(20, AttackSlotKind.Melee)).IsEqual(1);
		ledger.Forget(10);
		AssertInt(ledger.Engage(30, AttackSlotKind.Ranged)).IsEqual(1);
		AssertInt(ledger.EngagedCount).IsEqual(2);
	}
}