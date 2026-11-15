/// <summary>
/// One monster's flank episodes under a shared <see cref="FlankStrategy"/>. An episode starts when its movement strategy
/// closes in; the monster flanks until it is in position, close or out of time, then commits and closes in. The episode
/// ends, and the next one starts afresh, when the strategy stops closing in or the target changes.
/// </summary>
public sealed class FlankRuntime(FlankStrategy settings)
{
	private double _elapsed;
	private bool _committed;

	/// <summary>The movement to make instead of <paramref name="intent"/>: a flank, or the intent itself.</summary>
	public EnemyMovementIntent Apply(EnemyContext context, EnemyMovementIntent intent)
	{
		bool closing = intent.AllowAttack && intent.Goal is EnemyMovementGoal.Approach or EnemyMovementGoal.Hold;
		if (!closing)
		{
			Reset();
			return intent;
		}
		if (_committed) return intent;
		if (context.HorizontalTargetDistance <= settings.CommitDistance || _elapsed >= settings.MaximumDuration
			|| context.RingBearingError(settings.Arc) <= settings.Tolerance)
		{
			_committed = true;
			return intent;
		}
		_elapsed += context.Delta;
		return new(EnemyMovementGoal.Flank, false, settings.Distance, settings.Arc, settings.OrbitStep);
	}

	/// <summary>Ends the current episode.</summary>
	public void Reset()
	{
		_elapsed = 0;
		_committed = false;
	}
}
