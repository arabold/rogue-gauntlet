using System;

public enum EnemyRetreatPhase { None, Retreat, Wait }

/// <summary>Per-actor tactical state; no timer, execution identity or health latch lives on a shared Resource.</summary>
public sealed class EnemyTacticsState
{
	private AttackIdentity _expectedAttack;
	private bool _postAttackPending;
	private EnemyRetreatPhase _phase;
	private double _remaining;
	private bool _lowHealthLatched;
	private double _lowHealthRemaining;

	public void TrackAttack(AttackIdentity identity) => _expectedAttack = identity;

	/// <summary>Only completion of the tracked execution can schedule a retreat; interruption consumes it.</summary>
	public void AttackFinished(AttackIdentity identity, bool interrupted)
	{
		if (identity != _expectedAttack || identity == default) return;
		_expectedAttack = default;
		_postAttackPending = !interrupted;
	}

	public EnemyRetreatPhase EvaluatePostAttack(double delta, float distance, float desiredDistance, float duration, float delay)
	{
		if (_postAttackPending)
		{
			_postAttackPending = false;
			_phase = EnemyRetreatPhase.Retreat;
			_remaining = duration;
		}
		if (_phase == EnemyRetreatPhase.Retreat && (distance >= desiredDistance || _remaining <= 0))
		{
			_phase = EnemyRetreatPhase.Wait;
			_remaining = delay;
		}
		if (_phase == EnemyRetreatPhase.Wait && _remaining <= 0) _phase = EnemyRetreatPhase.None;
		_remaining = Math.Max(0, _remaining - delta);
		return _phase;
	}

	/// <summary>One bounded retreat per injury episode; losing a target does not rearm it.</summary>
	public bool EvaluateLowHealth(double delta, float fraction, bool hasTarget, float enter, float exit, float duration)
	{
		if (fraction >= exit) { _lowHealthLatched = false; _lowHealthRemaining = 0; }
		if (!hasTarget) { _lowHealthRemaining = 0; return false; }
		if (!_lowHealthLatched && fraction <= enter)
		{
			_lowHealthLatched = true;
			_lowHealthRemaining = duration;
		}
		bool active = _lowHealthRemaining > 0;
		_lowHealthRemaining = Math.Max(0, _lowHealthRemaining - delta);
		return active;
	}

	public void ClearTarget()
	{
		_expectedAttack = default;
		_postAttackPending = false;
		_phase = EnemyRetreatPhase.None;
		_remaining = _lowHealthRemaining = 0;
	}
}