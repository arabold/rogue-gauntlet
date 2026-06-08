/// <summary>
/// A small countdown helper for time-gated logic: start it with a duration, tick it down each
/// frame, and check whether it has elapsed. Replaces the hand-rolled <c>float -= delta; if (&lt;= 0)</c>
/// timers scattered across the project so each timer reads by intent (scan cadence, path refresh,
/// search duration, effect update throttle, lifetime, ...).
/// </summary>
/// <remarks>
/// It is a mutable value type: keep instances in fields and mutate them in place via
/// <see cref="Start"/>/<see cref="Tick"/>. Do not expose one through a property or pass it by value
/// expecting the original to change.
/// </remarks>
public struct Cooldown
{
	private float _remaining;

	/// <summary>True once the cooldown has elapsed (or was never started).</summary>
	public readonly bool IsReady => _remaining <= 0f;

	/// <summary>(Re)starts the cooldown with the given duration in seconds.</summary>
	public void Start(float seconds)
	{
		_remaining = seconds;
	}

	/// <summary>
	/// Advances the cooldown by <paramref name="delta"/> seconds and returns whether it is ready
	/// (has elapsed) afterwards.
	/// </summary>
	public bool Tick(double delta)
	{
		_remaining -= (float)delta;
		return _remaining <= 0f;
	}
}
