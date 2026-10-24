using System;

/// <summary>A tactical choice plus a stable diagnostic reason; components still validate actual execution.</summary>
public readonly record struct EnemyTacticalDecision(EnemyMovementIntent Intent, string Reason);

/// <summary>Per-actor priority selector. Retreat has a deadline, retry delay and optional intervening attack requirement.</summary>
public sealed class EnemyTacticalSelector(float maximumRetreatDuration, float retryDelay,
	float standAndFightDistance, bool requireAttackAfterRetreat)
{
	private double _retreatRemaining;
	private double _retryRemaining;
	private bool _attackRequired;
	private AttackIdentity _attack;
	public bool IsRetreating { get; private set; }

	/// <summary>Advance wall-clock deadlines even while an action owns the actor.</summary>
	public void Tick(double delta)
	{
		_retryRemaining = Math.Max(0, _retryRemaining - delta);
		if (IsRetreating) _retreatRemaining = Math.Max(0, _retreatRemaining - delta);
	}

	public EnemyTacticalDecision Select(EnemyMovementIntent preference, float distance, bool canAttack)
	{
		if (preference.Goal != EnemyMovementGoal.Retreat)
		{
			EndRetreat();
			return new(preference, "movement_preference");
		}
		string rejection = standAndFightDistance > 0 && distance <= standAndFightDistance ? "stand_and_fight"
			: IsRetreating && _retreatRemaining <= 0 ? "retreat_deadline"
			: !IsRetreating && _attackRequired ? "attack_opportunity"
			: !IsRetreating && _retryRemaining > 0 ? "retreat_retry_delay" : null;
		if (rejection != null)
		{
			EndRetreat();
			return Fallback(canAttack, rejection);
		}
		if (!IsRetreating)
		{
			IsRetreating = true;
			_retreatRemaining = maximumRetreatDuration;
		}
		return new(preference, "retreat_opportunity");
	}

	/// <summary>A rejected destination ends this opportunity immediately; do not retry it every physics frame.</summary>
	public EnemyTacticalDecision RetreatBlocked(bool canAttack)
	{
		EndRetreat();
		return Fallback(canAttack, "retreat_blocked");
	}

	private static EnemyTacticalDecision Fallback(bool canAttack, string reason) => new(
		new(canAttack ? EnemyMovementGoal.Hold : EnemyMovementGoal.Approach, canAttack), reason);

	private void EndRetreat()
	{
		if (!IsRetreating) return;
		IsRetreating = false;
		_retryRemaining = retryDelay;
		_attackRequired = requireAttackAfterRetreat;
	}

	/// <summary>Track accepted attacks only. Rejected requests cannot satisfy the intervening attack requirement.</summary>
	public void AttackAccepted(AttackIdentity identity)
	{
		EndRetreat();
		_attack = identity;
	}

	/// <summary>A completed or interrupted attempt releases the requirement, without granting immunity or a free shot.</summary>
	public void AttackFinished(AttackIdentity identity)
	{
		if (identity == default || identity != _attack) return;
		_attack = default;
		_attackRequired = false;
	}

	public void ClearTarget()
	{
		IsRetreating = false;
		_retreatRemaining = _retryRemaining = 0;
		_attackRequired = false;
		_attack = default;
	}
}