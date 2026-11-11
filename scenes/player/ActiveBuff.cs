using System;
using Godot;

public partial class ActiveBuff : Node
{
	[Signal] public delegate void BuffExpiredEventHandler(ActiveBuff buff);

	public IBuffTarget Target { get; private set; }
	public Buff Buff { get; private set; }
	public object Source { get; private set; }
	/// <summary>The refresh identity this activation was applied under; empty for stacking buffs.</summary>
	public string StackKey { get; private set; } = "";
	public double RemainingDuration { get; private set; }
	public double TickProgress => _tickAccumulator;

	/// <summary>A non-positive duration means the buff lasts until it is explicitly removed.</summary>
	public bool IsPermanent => _isPermanent;
	public bool IsExpired => !IsPermanent && RemainingDuration <= 0;

	private double _tickAccumulator = 0;
	private bool _removed;
	private bool _isPermanent;

	public override void _Ready() => UpdateProcessing();

	public void Initialize(IBuffTarget target, Buff buff)
	{
		Target = target;
		Buff = buff;
		StackKey = buff.RefreshKey;
		_isPermanent = buff.Duration <= 0;
		RemainingDuration = buff.Duration;

		Buff.OnApply(this);
	}

	/// <summary>Refreshes a condition activation without disturbing other sources.</summary>
	public void Refresh(float duration)
	{
		_isPermanent = duration <= 0;
		RemainingDuration = duration;
		if (IsInsideTree()) UpdateProcessing();
	}
	public void SetSource(object source) => Source = source;
	/// <summary>Restore a prevalidated clock without replaying periodic ticks.</summary>
	public void RestoreClock(double remaining, double tickProgress)
	{
		RemainingDuration = remaining;
		_isPermanent = Buff.Duration <= 0;
		_tickAccumulator = tickProgress;
	}

	/// <summary>
	/// Reverses the buff exactly once. Both natural expiry and explicit removal funnel
	/// through here so <see cref="Buff.OnRemove"/> is never skipped or applied twice.
	/// </summary>
	public void Deactivate()
	{
		if (_removed || Buff == null)
		{
			return;
		}

		_removed = true;
		if (IsInsideTree()) SetPhysicsProcess(false);
		Buff.OnRemove(this);
	}

	private void UpdateProcessing() => SetPhysicsProcess(!_removed && (!IsPermanent || Buff is PeriodicBuff));

	public override void _PhysicsProcess(double delta)
	{
		if (_removed)
		{
			return;
		}

		// Only count time up to what remains, so a large frame near expiry cannot drive
		// extra ticks past the buff's end. Permanent buffs accrue the full frame.
		double step = IsPermanent ? delta : System.Math.Min(delta, RemainingDuration);

		if (!IsPermanent)
		{
			RemainingDuration -= delta;
		}

		if (Buff is PeriodicBuff periodicBuff && periodicBuff.TicksPerSecond > 0)
		{
			double interval = 1.0 / periodicBuff.TicksPerSecond;
			_tickAccumulator += step;
			while (_tickAccumulator >= interval)
			{
				periodicBuff.OnTick(Target);
				if (_removed || Target?.Health?.IsDead == true) return;
				_tickAccumulator -= interval;
			}
		}

		if (IsExpired)
		{
			EmitSignalBuffExpired(this);
			Deactivate();
		}
	}
}
