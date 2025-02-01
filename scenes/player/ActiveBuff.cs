using System;
using Godot;

public partial class ActiveBuff : Node
{
	[Signal] public delegate void BuffExpiredEventHandler(ActiveBuff buff);

	public Player Player { get; private set; }
	public Buff Buff { get; private set; }
	public double RemainingDuration { get; private set; }
	public bool IsExpired => Buff.Duration >= 0 && RemainingDuration <= 0;
	private double _tickAccumulator = 0;

	public void Initialize(Player player, Buff buff)
	{
		Player = player;
		Buff = buff;
		RemainingDuration = buff.Duration;

		Buff.OnApply(Player);
	}

	public override void _Process(double delta)
	{
		if (IsExpired)
		{
			return;
		}

		if (Buff is PeriodicBuff periodicBuff)
		{
			_tickAccumulator += Math.Min(delta, RemainingDuration);
			RemainingDuration -= delta;

			while (_tickAccumulator >= 1.0 / periodicBuff.TicksPerSecond)
			{
				periodicBuff.OnTick(Player);
				_tickAccumulator -= 1.0 / periodicBuff.TicksPerSecond;
			}
		}

		if (IsExpired)
		{
			GD.Print("Buff is expired");
			EmitSignalBuffExpired(this);
			Buff.OnRemove(Player);
		}
	}
}
