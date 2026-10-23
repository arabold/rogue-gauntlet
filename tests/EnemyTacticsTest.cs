namespace RogueGauntlet.Tests;

using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>Pure tactical state contracts: execution ownership, bounded retreats and health hysteresis.</summary>
[TestSuite]
public class EnemyTacticsTest
{
	[TestCase]
	public void OnlyNormalCompletionOfTheTrackedAttackStartsRetreat()
	{
		var state = new EnemyTacticsState();
		var first = new AttackIdentity(1, 1);
		var second = new AttackIdentity(1, 2);
		state.TrackAttack(first);
		state.TrackAttack(second);
		state.AttackFinished(first, false);
		AssertInt((int)state.EvaluatePostAttack(.1, 1, 4, 1, .2f)).IsEqual((int)EnemyRetreatPhase.None);
		state.AttackFinished(second, true);
		AssertInt((int)state.EvaluatePostAttack(.1, 1, 4, 1, .2f)).IsEqual((int)EnemyRetreatPhase.None);
		state.TrackAttack(new(1, 3));
		state.AttackFinished(new(1, 3), false);
		AssertInt((int)state.EvaluatePostAttack(.1, 1, 4, 1, .2f)).IsEqual((int)EnemyRetreatPhase.Retreat);
	}

	[TestCase]
	public void BlockedRetreatStillTimesOutAndReengages()
	{
		var state = new EnemyTacticsState();
		state.TrackAttack(new(1, 1));
		state.AttackFinished(new(1, 1), false);
		for (int i = 0; i < 10; i++) state.EvaluatePostAttack(.1, 1, 4, .3f, .2f);
		AssertInt((int)state.EvaluatePostAttack(.1, 1, 4, .3f, .2f)).IsEqual((int)EnemyRetreatPhase.None);
	}

	[TestCase]
	public void ReachingSeparationStartsTheReengagementWait()
	{
		var state = new EnemyTacticsState();
		state.TrackAttack(new(1, 1));
		state.AttackFinished(new(1, 1), false);
		AssertInt((int)state.EvaluatePostAttack(.1, 5, 4, 1, .5f)).IsEqual((int)EnemyRetreatPhase.Wait);
	}

	[TestCase]
	public void LowHealthRetreatNeedsHealthRecoveryToRearm()
	{
		var state = new EnemyTacticsState();
		AssertBool(state.EvaluateLowHealth(.1, .2f, true, .25f, .4f, .2f)).IsTrue();
		state.EvaluateLowHealth(.2, .2f, true, .25f, .4f, .2f);
		AssertBool(state.EvaluateLowHealth(.1, .2f, true, .25f, .4f, .2f)).IsFalse();
		state.ClearTarget();
		AssertBool(state.EvaluateLowHealth(.1, .2f, true, .25f, .4f, .2f)).IsFalse();
		AssertBool(state.EvaluateLowHealth(.1, .35f, true, .25f, .4f, .2f)).IsFalse();
		state.EvaluateLowHealth(.1, .5f, true, .25f, .4f, .2f);
		AssertBool(state.EvaluateLowHealth(.1, .2f, true, .25f, .4f, .2f)).IsTrue();
	}

	[TestCase]
	public void LosingTargetClearsPendingAttackRetreatAndInstancesDoNotShareState()
	{
		var first = new EnemyTacticsState();
		var second = new EnemyTacticsState();
		first.TrackAttack(new(1, 1));
		first.AttackFinished(new(1, 1), false);
		AssertInt((int)second.EvaluatePostAttack(.1, 1, 4, 1, .2f)).IsEqual((int)EnemyRetreatPhase.None);
		first.ClearTarget();
		AssertInt((int)first.EvaluatePostAttack(.1, 1, 4, 1, .2f)).IsEqual((int)EnemyRetreatPhase.None);
	}
}