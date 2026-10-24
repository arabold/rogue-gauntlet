namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Decision regressions for continuous pressure, blocked escapes and execution ownership.</summary>
[TestSuite]
public class EnemyTacticalSelectionTest
{
	private static readonly EnemyMovementIntent Retreat = new(EnemyMovementGoal.Retreat, false, 7);
	private static EnemyTacticalSelector Shooter() => new(.9f, 1.2f, 2.2f, true);

	[TestCase]
	public void ContinuousPursuitCannotSuppressShootingIndefinitely()
	{
		var state = Shooter();
		AssertBool(state.Select(Retreat, 3, true).Intent.AllowAttack).IsFalse();
		state.Tick(1);
		AssertString(state.Select(Retreat, 3, true).Reason).IsEqual("retreat_deadline");
		state.Tick(20);
		AssertBool(state.Select(Retreat, 3, true).Intent.AllowAttack).IsTrue();
		state.AttackAccepted(new(1, 1));
		state.Tick(2);
		state.AttackFinished(new(1, 1));
		AssertString(state.Select(Retreat, 3, true).Reason).IsEqual("retreat_opportunity");
	}

	[TestCase]
	public void CorneredShooterImmediatelySelectsAShotAndDoesNotRetryEveryFrame()
	{
		var state = Shooter();
		state.Select(Retreat, 3, true);
		var blocked = state.RetreatBlocked(true);
		AssertBool(blocked.Intent.AllowAttack).IsTrue();
		AssertObject(blocked.Intent.Goal).IsEqual(EnemyMovementGoal.Hold);
		for (int i = 0; i < 180; i++)
		{
			state.Tick(1.0 / 60);
			AssertBool(state.Select(Retreat, 3, true).Intent.AllowAttack).IsTrue();
		}
	}

	[TestCase]
	public void PointBlankPressurePrefersShootingEvenDuringRetreat()
	{
		var state = Shooter();
		state.Select(Retreat, 3, true);
		var decision = state.Select(Retreat, 1.5f, true);
		AssertBool(decision.Intent.AllowAttack).IsTrue();
		AssertBool(state.IsRetreating).IsFalse();
		AssertString(decision.Reason).IsEqual("stand_and_fight");
	}

	[TestCase]
	public void BlockedEscapeWithoutAShotRepositionsInsteadOfFiringBlindly()
	{
		var state = Shooter();
		state.Select(Retreat, 3, false);
		var decision = state.RetreatBlocked(false);
		AssertObject(decision.Intent.Goal).IsEqual(EnemyMovementGoal.Approach);
		AssertBool(decision.Intent.AllowAttack).IsFalse();
	}

	[TestCase]
	public void RejectedOrStaleExecutionsDoNotReleaseTheShotOpportunity()
	{
		var state = Shooter();
		state.Select(Retreat, 3, true);
		state.RetreatBlocked(true);
		state.Tick(2);
		state.AttackFinished(new(1, 1));
		AssertString(state.Select(Retreat, 3, true).Reason).IsEqual("attack_opportunity");
		state.AttackAccepted(new(1, 2));
		state.AttackFinished(new(1, 1));
		AssertBool(state.Select(Retreat, 3, true).Intent.AllowAttack).IsTrue();
		state.AttackFinished(new(1, 2));
		AssertBool(state.Select(Retreat, 3, true).Intent.AllowAttack).IsFalse();
	}

	[TestCase]
	public void CompletedShotStillRespectsRetreatRetryDelay()
	{
		var state = Shooter();
		state.Select(Retreat, 3, true);
		state.RetreatBlocked(true);
		state.AttackAccepted(new(1, 1));
		state.AttackFinished(new(1, 1));
		AssertString(state.Select(Retreat, 3, true).Reason).IsEqual("retreat_retry_delay");
		state.Tick(2);
		AssertBool(state.Select(Retreat, 3, true).Intent.AllowAttack).IsFalse();
	}

	[TestCase]
	public void TargetLossClearsTransientChoicesAndSeparateActorsStayIndependent()
	{
		var first = Shooter();
		var second = Shooter();
		first.Select(Retreat, 3, true);
		first.RetreatBlocked(true);
		AssertBool(second.Select(Retreat, 3, true).Intent.AllowAttack).IsFalse();
		first.ClearTarget();
		AssertBool(first.Select(Retreat, 3, true).Intent.AllowAttack).IsFalse();
	}

	[TestCase]
	public void OptionalPoliciesPreservePostAttackWaitAndAllowHealthRetreat()
	{
		var state = new EnemyTacticalSelector(2, .5f, 0, false);
		AssertBool(state.Select(Retreat, 0, true).Intent.AllowAttack).IsFalse();
		var wait = state.Select(new(EnemyMovementGoal.Hold, false), 4, true);
		AssertBool(wait.Intent.AllowAttack).IsFalse();
		state.Tick(1);
		AssertBool(state.Select(Retreat, 0, true).Intent.AllowAttack).IsFalse();
	}
}