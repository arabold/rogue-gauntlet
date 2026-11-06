using System;

/// <summary>Clock and legal phase transitions for one accepted attack execution.</summary>
public sealed class AttackExecution
{
	private float _duration;
	private float _contactStart;
	private float _contactEnd;
	private float _elapsed;
	private float _hitStopRemaining;
	private bool _hitStopUsed;

	public ActorActionPhase Phase { get; private set; }
	public float Elapsed => _elapsed;
	public float Remaining => IsRunning ? Math.Max(0, _duration - _elapsed) : 0;
	public bool IsRunning => Phase != ActorActionPhase.None;
	public bool IsClockStopped => _hitStopRemaining > 0;

	/// <summary>Accept timing only when all phase boundaries are finite and ordered inside the duration.</summary>
	public bool TryStart(float duration, float contactStart, float contactEnd)
	{
		if (IsRunning || !float.IsFinite(duration) || !float.IsFinite(contactStart) || !float.IsFinite(contactEnd)
			|| duration <= 0 || contactStart < 0 || contactEnd < contactStart || contactEnd > duration) return false;

		_duration = duration;
		_contactStart = contactStart;
		_contactEnd = contactEnd;
		_elapsed = 0;
		_hitStopRemaining = 0;
		_hitStopUsed = false;
		Phase = PhaseAt(_elapsed);
		return true;
	}

	/// <summary>Advance attack time, preserving the existing whole-tick hit-stop behavior.</summary>
	public AttackExecutionStep Advance(float delta)
	{
		if (!float.IsFinite(delta) || delta < 0) return new(_elapsed, _elapsed, Phase, IsClockStopped, false);
		if (!IsRunning) return new(_elapsed, _elapsed, Phase, false, false);

		float previous = _elapsed;
		if (_hitStopRemaining > 0)
		{
			_hitStopRemaining = Math.Max(0, _hitStopRemaining - delta);
			return new(previous, _elapsed, Phase, true, false);
		}

		_elapsed = Math.Min(_duration, _elapsed + delta);
		Phase = PhaseAt(_elapsed);
		return new(previous, _elapsed, Phase, false, _elapsed >= _duration);
	}

	/// <summary>Reserve the once-per-execution hit stop, including a zero-scaled authored stop.</summary>
	public bool TryStartHitStop(float duration)
	{
		if (!IsRunning || _hitStopUsed || !float.IsFinite(duration)) return false;
		_hitStopUsed = true;
		_hitStopRemaining = Math.Max(0, duration);
		return true;
	}

	/// <summary>Finish is safe to call repeatedly during interruption and teardown.</summary>
	public void Finish()
	{
		Phase = ActorActionPhase.None;
		_elapsed = 0;
		_duration = 0;
		_contactStart = 0;
		_contactEnd = 0;
		_hitStopRemaining = 0;
		_hitStopUsed = false;
	}

	private ActorActionPhase PhaseAt(float elapsed) => elapsed < _contactStart ? ActorActionPhase.WindUp
		: elapsed <= _contactEnd ? ActorActionPhase.Active : ActorActionPhase.Recovery;
}

/// <summary>Result of advancing an attack clock by one simulation tick.</summary>
public readonly record struct AttackExecutionStep(float PreviousTime, float CurrentTime, ActorActionPhase Phase,
	bool ClockStopped, bool Completed);
